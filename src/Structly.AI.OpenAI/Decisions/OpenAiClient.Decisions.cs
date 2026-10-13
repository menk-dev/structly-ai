namespace Structly.AI.OpenAI;

public sealed partial class OpenAiClient
{
    /// <summary>Answers every task question about text evidence using the configured decision model.</summary>
    public Task<StructuredResult<T>> DecideAsync<T>(DecisionTask<T> task, string input, CancellationToken cancellationToken = default)
        => DecideAsync(task, new OpenAiDecisionRequest { Input = input }, cancellationToken);

    /// <summary>Validates locally, sends one decision request and constructs a complete typed answer. Any refusal fails the result.</summary>
    [System.Runtime.CompilerServices.OverloadResolutionPriority(1)]
    public Task<StructuredResult<T>> DecideAsync<T>(DecisionTask<T> task, OpenAiDecisionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(request);
        var controls = new OpenAiRequest
        {
            TotalTimeout = request.TotalTimeout,
            CredentialResolver = request.CredentialResolver,
            UsageObserver = request.UsageObserver,
            CorrelationId = request.CorrelationId,
            OpenAi = new() { CaptureRawResponse = request.CaptureRawResponse },
        };
        return RunOperation(controls, "Decisions", execution => DecisionCore(task, request, controls, execution), cancellationToken);
    }

    async Task<StructuredResult<T>> DecisionCore<T>(DecisionTask<T> task, OpenAiDecisionRequest request,
        StructuredRequest controls, OpenAiExecution execution)
    {
        execution.CheckCancellation();
        Dictionary<string, object?> payload;
        try
        {
            ValidateCommonRequest(controls);
            var selection = request.ModelSelection ?? _options.DefaultDecisionModel ?? throw new ArgumentException("Select a decision model.");
            var model = SelectModel(selection, _options.DecisionProfiles);
            if(model.ReasoningEffort is not null || request.SafetyIdentifier is { Length: > 128 })
                throw new ArgumentException("Invalid decision model or safety identifier.");

            execution.Metadata = execution.Metadata with { RequestedModel = model.ModelId };
            payload = new()
            {
                ["model"] = model.ModelId,
                ["input"] = DecisionInput(request),
                ["questions"] = task.Questions(),
            };
            if(request.SafetyIdentifier is { } identifier)
                payload["safety_identifier"] = identifier;
        }
        catch(ArgumentException)
        {
            return InvalidOptions<T>(execution);
        }

        return await Send(payload, "decisions", controls, execution, null,
            root => ValueTask.FromResult(task.Read(root, execution.Metadata, execution.Warnings))).ConfigureAwait(false);
    }

    static object DecisionInput(OpenAiDecisionRequest request)
    {
        // Copy mutable message collections before validation or credential resolution.
        var messages = request.Messages?.ToArray().Select(message =>
        {
            if(message is null || message.Role != MessageRole.User || message.Content is null)
                throw new ArgumentException("Decisions accept only user evidence messages.");

            return message with { Content = message.Content.ToArray() };
        }).ToArray();
        var images = 0;
        foreach(var message in messages ?? [])
            foreach(var part in message.Content)
            {
                if(part is null || part.CacheBreakpoint || part is ImagePart image && (image.Url is null || !image.Url.StartsWith("data:", StringComparison.Ordinal)))
                    throw new ArgumentException("Decisions accept text and inline base64 images without cache breakpoints.");

                if(part is ImagePart && ++images > 128)
                    throw new ArgumentException("Decisions accept at most 128 images.");
            }

        return CreateInput(new StructuredRequest { Input = request.Input, Messages = messages }, null).Input;
    }
}
