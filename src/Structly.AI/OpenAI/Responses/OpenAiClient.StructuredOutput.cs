namespace Structly.AI.OpenAI;

public sealed partial class OpenAiClient
{
    /// <summary>Executes a reusable task with text input and default request settings.</summary>
    public Task<StructuredResult<T>> ExecuteAsync<T>(StructuredTask<T> task, string input,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(task, new StructuredRequest { Input = input }, cancellationToken);

    /// <summary>Executes bound output with text input and default request settings.</summary>
    public Task<StructuredResult<T>> ExecuteAsync<T>(BoundOutput<T> output, string input,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(output, new StructuredRequest { Input = input }, cancellationToken);

    /// <summary>Validates locally, sends one request and validates the returned typed output.</summary>
    [System.Runtime.CompilerServices.OverloadResolutionPriority(1)]
    public Task<StructuredResult<T>> ExecuteAsync<T>(StructuredTask<T> task, StructuredRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(request);
        request = task.ExecutionDefaults.Apply(request);
        return RunOperation(request, "Structured", execution => ExecuteCore(task, request, execution), cancellationToken, task.SchemaName);
    }

    /// <summary>Executes a bound contract. Request output settings must be absent.</summary>
    [System.Runtime.CompilerServices.OverloadResolutionPriority(1)]
    public Task<StructuredResult<T>> ExecuteAsync<T>(BoundOutput<T> output, StructuredRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(request);
        request = output.Task.ExecutionDefaults.Apply(request);
        return RunOperation(request, "Structured", execution => ExecuteCore(output.Task, request, execution, output), cancellationToken, output.SchemaName);
    }

    async Task<StructuredResult<T>> ExecuteCore<T>(StructuredTask<T> task, StructuredRequest request, OpenAiExecution execution, BoundOutput<T>? bound = null)
    {
        execution.CheckCancellation();
        BoundOutput<T> output;
        Dictionary<string, object?> payload;
        try
        {
            if(bound is not null && (request.Vocabularies is not null || request.OutputSpecification is not null))
                throw new ArgumentException("Bound output cannot be overridden.");

            ValidateResponseRequest(request);
            if(String.IsNullOrWhiteSpace(request.Instructions ?? task.Instructions))
                throw new ArgumentException("Typed execution requires instructions.");

            var vocabularies = bound is null ? request.Vocabularies?.ToDictionary(x => x.Key,
                x => (IReadOnlyList<string>)(x.Value ?? throw new ArgumentException("Vocabulary values cannot be null.")).ToArray(), StringComparer.Ordinal) : null;
            output = bound ?? task.BindOutput(new() { Vocabularies = vocabularies, OutputSpecification = request.OutputSpecification });
            var model = SelectModel(request.ModelSelection ?? task.ModelSelection ?? _options.DefaultModel);
            execution.Metadata = execution.Metadata with { RequestedModel = model.ModelId };
            payload = ResponsePayload(request, model, request.Instructions ?? task.Instructions, output.Guidance);
            payload["text"] = new { format = output.Format };
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
            root => ProcessResponse(root, request, execution, (text, metadata) => output.ReadOutput(text, metadata))).ConfigureAwait(false);
    }

}
