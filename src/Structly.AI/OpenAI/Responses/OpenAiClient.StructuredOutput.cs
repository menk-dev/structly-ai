namespace Structly.AI.OpenAI;

public sealed partial class OpenAiClient
{
    /// <summary>Validates locally, sends one request and validates the returned typed output.</summary>
    public Task<StructuredResult<T>> ExecuteAsync<T>(StructuredTask<T> task, StructuredRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(request);
        return RunOperation(request, "Structured", execution => ExecuteCore(task, request, execution), cancellationToken, task.SchemaName);
    }

    async Task<StructuredResult<T>> ExecuteCore<T>(StructuredTask<T> task, StructuredRequest request, OpenAiExecution execution)
    {
        execution.CheckCancellation();
        Dictionary<string, IReadOnlyList<string>>? vocabularies;
        Dictionary<string, object?> payload;
        try
        {
            ValidateResponseRequest(request);
            if(String.IsNullOrWhiteSpace(request.Instructions ?? task.Instructions))
                throw new ArgumentException("Typed execution requires instructions.");

            vocabularies = request.Vocabularies?.ToDictionary(x => x.Key,
                x => (IReadOnlyList<string>)(x.Value ?? throw new ArgumentException("Vocabulary values cannot be null.")).ToArray(), StringComparer.Ordinal);
            var schema = task.CreateSchema(vocabularies);
            var guidance = request.OutputSpecification is { } specification ? task.CreateOutputSpecification(specification, vocabularies) : null;
            var model = SelectModel(request.ModelSelection ?? task.ModelSelection ?? _options.DefaultModel);
            execution.Metadata = execution.Metadata with { RequestedModel = model.ModelId };
            payload = ResponsePayload(request, model, request.Instructions ?? task.Instructions, guidance);
            var format = new Dictionary<string, object?> { ["type"] = "json_schema", ["name"] = task.SchemaName, ["schema"] = schema, ["strict"] = true };
            if(task.Description is not null)
                format["description"] = task.Description;

            payload["text"] = new { format };
        }
        catch(StructuredSchemaException exception)
        {
            return LocalFailure<T>(execution, StructuredErrorKind.UnsupportedSchema, issues: exception.Issues);
        }
        catch(ArgumentException)
        {
            return InvalidOptions<T>(execution);
        }

        return await Send(payload, "responses", request, execution, task.CredentialResolver,
            root => ProcessResponse(root, request, execution, (text, metadata) => task.ReadOutput(text, vocabularies, metadata))).ConfigureAwait(false);
    }

}
