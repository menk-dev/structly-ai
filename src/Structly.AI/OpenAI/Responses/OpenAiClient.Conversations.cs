namespace Structly.AI.OpenAI;

public sealed partial class OpenAiClient
{
    /// <summary>Creates a conversation that retains responses at OpenAI, without resolving credentials or sending HTTP.</summary>
    public StructuredConversation<T> CreateConversation<T>(StructuredTask<T> task, ConversationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(task);
        return CreateConversation(task.BindOutput(), options);
    }

    /// <summary>Creates a conversation that retains responses at OpenAI, without resolving credentials or sending HTTP.</summary>
    public StructuredConversation<T> CreateConversation<T>(BoundOutput<T> output, ConversationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(output);
        var backend = new OpenAiConversationBackend(this, output.Task.CredentialResolver);
        var configuration = backend.Resolve(new()
        {
            Instructions = options?.Instructions ?? output.Task.Instructions!,
            ModelSelection = options?.ModelSelection ?? output.Task.ModelSelection ?? _options.DefaultModel,
            IncludeReasoningSummary = options?.IncludeReasoningSummary ?? false,
        });
        return new(output, configuration, backend);
    }

    sealed class OpenAiConversationBackend(OpenAiClient client, Func<CancellationToken, ValueTask<string?>>? credentials) : ConversationBackend
    {
        string? _previousResponseId;

        internal override ConversationConfiguration Resolve(ConversationConfiguration configuration)
        {
            if(String.IsNullOrWhiteSpace(configuration.Instructions) || configuration.ModelSelection is null)
                throw new ArgumentException("Conversation instructions and model selection are required.");

            return configuration with { ModelSelection = client.SelectModel(configuration.ModelSelection) };
        }

        internal override ConversationBackend Branch() => new OpenAiConversationBackend(client, credentials) { _previousResponseId = _previousResponseId };

        internal override async Task<StructuredResult<T>> Execute<T>(BoundOutput<T> output, ConversationConfiguration configuration,
            ConversationRequest request, CancellationToken cancellationToken)
        {
            var messages = request.Messages?.Select(message => message is null ? null! : message with { Content = message.Content?.ToArray()! }).ToArray();
            var advanced = new StructuredRequest
            {
                Input = request.Input,
                Messages = messages,
                Stream = request.Stream,
                Progress = request.Progress,
                UsageObserver = request.UsageObserver,
                TotalTimeout = request.TotalTimeout,
                InactivityTimeout = request.InactivityTimeout,
                MaxOutputTokens = request.MaxOutputTokens,
                CorrelationId = request.CorrelationId,
                CredentialResolver = request.CredentialResolver ?? credentials ?? client._options.CredentialResolver ?? (_ => ValueTask.FromResult<string?>(null)),
                Instructions = configuration.Instructions,
                ModelSelection = configuration.ModelSelection,
                IncludeReasoningSummary = configuration.IncludeReasoningSummary,
                OpenAi = new() { Store = true, PreviousResponseId = _previousResponseId },
            };
            var result = await client.RunOperation(advanced, "Structured", execution =>
            {
                if(messages is not null && messages.Any(message => message is null || message.Role != MessageRole.User ||
                    message.Content is null || message.Content.Any(part => part is null || part.CacheBreakpoint)))
                    return Task.FromResult(InvalidOptions<T>(execution));

                return client.ExecuteCore(output.Task, advanced, execution, output);
            }, cancellationToken, output.SchemaName).ConfigureAwait(false);
            if(result.IsSuccess)
                _previousResponseId = result.Metadata.ResponseId;

            return result;
        }
    }
}
