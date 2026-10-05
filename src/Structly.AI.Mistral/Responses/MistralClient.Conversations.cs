using System.Text.Json;

namespace Structly.AI.Mistral;

public sealed partial class MistralClient
{
    /// <summary>Creates a conversation retaining history locally and sending it with each chat completion.</summary>
    public StructuredConversation<T> CreateConversation<T>(StructuredTask<T> task, ConversationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(task);
        return CreateConversation(task.BindOutput(), options);
    }

    /// <summary>Creates a conversation with stable bound output and independent local branches.</summary>
    public StructuredConversation<T> CreateConversation<T>(BoundOutput<T> output, ConversationOptions? options = null)
        => CreateConversation(output, options, false);

    /// <summary>Creates a conversation using Mistral's beta stored conversation API.</summary>
    public StructuredConversation<T> CreateStoredConversation<T>(StructuredTask<T> task, ConversationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(task);
        return CreateStoredConversation(task.BindOutput(), options);
    }

    /// <summary>Creates a stored conversation; successful turns and branches retain independent provider positions.</summary>
    public StructuredConversation<T> CreateStoredConversation<T>(BoundOutput<T> output, ConversationOptions? options = null)
        => CreateConversation(output, options, true);

    StructuredConversation<T> CreateConversation<T>(BoundOutput<T> output, ConversationOptions? options, bool stored)
    {
        ArgumentNullException.ThrowIfNull(output);
        var backend = new MistralConversationBackend(this, output.Task.CredentialResolver, output.Task.ExecutionDefaults, stored);
        var configuration = backend.Resolve(new()
        {
            Instructions = options?.Instructions ?? output.Task.Instructions!,
            ModelSelection = options?.ModelSelection ?? output.Task.ModelSelection ?? _defaultModel,
            IncludeReasoningSummary = options?.IncludeReasoningSummary ?? false,
        });
        return new(output, configuration, backend);
    }

    sealed class MistralConversationBackend(MistralClient client, Func<CancellationToken, ValueTask<string?>>? credentials, TaskExecutionDefaults defaults, bool stored) : ConversationBackend
    {
        List<JsonElement> _history = [];
        StoredPosition? _position;

        public override ConversationConfiguration Resolve(ConversationConfiguration configuration)
        {
            if(String.IsNullOrWhiteSpace(configuration.Instructions) || configuration.ModelSelection is null || configuration.IncludeReasoningSummary)
                throw new ArgumentException("Conversation requires instructions and a model; reasoning summaries are unsupported.");

            return configuration with { ModelSelection = client.SelectModel(configuration.ModelSelection) };
        }

        public override ConversationBackend Branch() => new MistralConversationBackend(client, credentials, defaults, stored) { _history = new(_history), _position = _position };

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
            if(stored)
            {
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

            var result = await client.Run(controls, null, credentials, output.SchemaName, () =>
            {
                if(controls.Messages?.Any(x => x is null || x.Role != MessageRole.User) == true)
                    throw new ArgumentException("Conversation turns require user messages.");

                return output;
            }, configuration.Instructions, cancellationToken, _history, message => assistant = message).ConfigureAwait(false);
            if(result.IsSuccess && assistant is { } message)
            {
                _history.AddRange(Messages(controls).Select(x => JsonSerializer.SerializeToElement(x)));
                _history.Add(message);
            }

            return result;
        }
    }
}
