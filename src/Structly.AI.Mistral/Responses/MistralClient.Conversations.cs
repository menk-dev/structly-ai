using System.Text.Json;

namespace Structly.AI.Mistral;

public sealed partial class MistralClient
{
    /// <summary>Creates a conversation that retains responses at Mistral, without resolving credentials or sending HTTP.</summary>
    public StructuredConversation<T> CreateConversation<T>(StructuredTask<T> task, ConversationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(task);
        return CreateConversation(task.BindOutput(), options);
    }

    /// <summary>Creates a conversation using Mistral's beta stored conversation API, with independent provider positions for branches.</summary>
    public StructuredConversation<T> CreateConversation<T>(BoundOutput<T> output, ConversationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(output);
        var backend = new MistralConversationBackend(this, output.Task.CredentialResolver, output.Task.ExecutionDefaults);
        var configuration = backend.Resolve(new()
        {
            Instructions = options?.Instructions ?? output.Task.Instructions!,
            ModelSelection = options?.ModelSelection ?? output.Task.ModelSelection ?? _defaultModel,
            IncludeReasoningSummary = options?.IncludeReasoningSummary ?? false,
        });
        return new(output, configuration, backend);
    }

    sealed class MistralConversationBackend(MistralClient client, Func<CancellationToken, ValueTask<string?>>? credentials, TaskExecutionDefaults defaults) : ConversationBackend
    {
        List<JsonElement> _history = [];
        StoredPosition? _position;

        public override ConversationConfiguration Resolve(ConversationConfiguration configuration)
        {
            if(String.IsNullOrWhiteSpace(configuration.Instructions) || configuration.ModelSelection is null || configuration.IncludeReasoningSummary)
                throw new ArgumentException("Conversation requires instructions and a model; reasoning summaries are unsupported.");

            return configuration with { ModelSelection = client.SelectModel(configuration.ModelSelection) };
        }

        public override ConversationBackend Branch() => new MistralConversationBackend(client, credentials, defaults) { _history = new(_history), _position = _position };

        public override async Task<StructuredResult<T>> Execute<T>(BoundOutput<T> output, ConversationConfiguration configuration,
            ConversationRequest request, CancellationToken cancellationToken)
        {
            var controls = defaults.Apply(new StructuredRequest
            {
                Input = request.Input,
                Messages = request.Messages?.Select(x => x is null ? null! : x with { Content = x.Content?.ToArray()! }).ToArray(),
                Instructions = configuration.Instructions,
                ModelSelection = configuration.ModelSelection,
                Stream = request.Stream,
                Progress = request.Progress,
                InactivityTimeout = request.InactivityTimeout,
                TotalTimeout = request.TotalTimeout,
                MaxOutputTokens = request.MaxOutputTokens,
                CredentialResolver = request.CredentialResolver,
                UsageObserver = request.UsageObserver,
                CorrelationId = request.CorrelationId,
            });
            JsonElement? assistant = null;
            StoredPosition? nextPosition = null;
            var storedResult = await client.RunStored(output, controls, credentials, _history, _position,
                (position, message) =>
                {
                    nextPosition = position;
                    assistant = message;
                }, cancellationToken).ConfigureAwait(false);
            if(storedResult.IsSuccess && assistant is { } entry)
            {
                _position = nextPosition;
                _history.AddRange(Messages(controls).Select(x => JsonSerializer.SerializeToElement(x)));
                _history.Add(entry);
            }

            return storedResult;
        }
    }
}
