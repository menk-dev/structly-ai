namespace Structly.AI.OpenAI;

public sealed partial class OpenAiClient
{    /// <summary>Generates free text without an output schema, using the same response and streaming policies.</summary>
    public Task<StructuredResult<string>> GenerateTextAsync(TextRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Request);
        return RunOperation(request.Request, "Text", execution => TextCore(request, execution), cancellationToken);
    }

    async Task<StructuredResult<string>> TextCore(TextRequest textRequest, OpenAiExecution execution)
    {
        execution.CheckCancellation();
        var request = textRequest.Request;
        Dictionary<string, object?> payload;
        try
        {
            ValidateResponseRequest(request);
            if(request.OutputSpecification is not null || request.Vocabularies is not null)
                throw new ArgumentException("Text has no output contract.");

            var model = SelectModel(request.ModelSelection ?? _options.DefaultModel);
            execution.Metadata = execution.Metadata with { RequestedModel = model.ModelId };
            payload = ResponsePayload(request, model, request.Instructions ?? textRequest.Instructions);
        }
        catch(ArgumentException)
        {
            return InvalidOptions<string>(execution);
        }

        return await Send(payload, "responses", request, execution, null,
            root => ProcessResponse(root, request, execution, (text, metadata) => String.IsNullOrWhiteSpace(text)
                ? LocalFailure<string>(execution, StructuredErrorKind.InvalidResponse)
                : StructuredResult<string>.Success(text, metadata))).ConfigureAwait(false);
    }

}
