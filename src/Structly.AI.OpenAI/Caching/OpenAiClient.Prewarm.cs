namespace Structly.AI.OpenAI;

public sealed partial class OpenAiClient
{
    /// <summary>Prewarms input without generating output. No cache-write or reuse guarantee.</summary>
    public Task<StructuredResult<OpenAiPrewarmResult>> PrewarmAsync(OpenAiPrewarmRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Request);
        return RunOperation(request.Request, "Prewarm", execution => PrewarmCore(request, execution, null, null), cancellationToken);
    }

    /// <summary>Prewarms with the same strict format and vocabulary as a future typed call.</summary>
    public Task<StructuredResult<OpenAiPrewarmResult>> PrewarmAsync<T>(StructuredTask<T> task, OpenAiPrewarmRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Request);
        return RunOperation(request.Request, "Prewarm", execution => PrewarmCore(request, execution, task.CredentialResolver, () =>
        {
            if(String.IsNullOrWhiteSpace(request.Request.Instructions ?? task.Instructions))
                throw new ArgumentException("Typed prewarm requires instructions.");

            if(request.Instructions is not null)
                throw new ArgumentException("Typed prewarm uses task instructions.");

            var model = SelectModel(request.Request.ModelSelection ?? task.ModelSelection ?? _options.DefaultModel);
            execution.Metadata = execution.Metadata with { RequestedModel = model.ModelId };
            var payload = ResponsePayload(request.Request, model, request.Request.Instructions ?? task.Instructions, prewarm: true);
            var format = new Dictionary<string, object?>
            {
                ["type"] = "json_schema",
                ["name"] = task.SchemaName,
                ["schema"] = task.CreateSchema(request.Request.Vocabularies),
                ["strict"] = true,
            };
            if(task.Description is not null)
                format["description"] = task.Description;

            payload["text"] = new { format };
            return payload;
        }), cancellationToken, task.SchemaName);
    }

    async Task<StructuredResult<OpenAiPrewarmResult>> PrewarmCore(OpenAiPrewarmRequest warm, OpenAiExecution execution,
        Func<CancellationToken, ValueTask<string?>>? taskCredential, Func<Dictionary<string, object?>>? typedPayload)
    {
        execution.CheckCancellation();
        var request = warm.Request;
        Dictionary<string, object?> payload;
        try
        {
            ValidateResponseRequest(request, true);
            if(typedPayload is null)
            {
                if(request.Vocabularies is not null)
                    throw new ArgumentException("Untyped prewarm has no vocabularies.");

                var model = SelectModel(request.ModelSelection ?? _options.DefaultModel);
                execution.Metadata = execution.Metadata with { RequestedModel = model.ModelId };
                payload = ResponsePayload(request, model, request.Instructions ?? warm.Instructions, prewarm: true);
            }
            else
                payload = typedPayload();
        }
        catch(StructuredSchemaException exception)
        {
            return LocalFailure<OpenAiPrewarmResult>(execution, StructuredErrorKind.UnsupportedSchema, issues: exception.Issues);
        }
        catch(ArgumentException)
        {
            return InvalidOptions<OpenAiPrewarmResult>(execution);
        }

        return await Send(payload, "responses", request, execution, taskCredential,
            root => ProcessResponse(root, request, execution,
                (_, _) => throw new InvalidOperationException("Prewarm cannot process generated output."), true, new OpenAiPrewarmResult())).ConfigureAwait(false);
    }

}
