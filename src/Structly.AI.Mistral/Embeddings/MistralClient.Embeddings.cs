using Structly.AI.Embeddings;
using System.Net.Http.Json;
using System.Text.Json;

namespace Structly.AI.Mistral;

public sealed partial class MistralClient
{
    /// <summary>Returns immutable finite float vectors in input order.</summary>
    public Task<StructuredResult<IReadOnlyList<IReadOnlyList<float>>>> EmbedAsync(EmbeddingRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var controls = new MistralRequest
        {
            TotalTimeout = request.TotalTimeout,
            CredentialResolver = request.CredentialResolver,
            UsageObserver = request.UsageObserver,
            CorrelationId = request.CorrelationId,
            CaptureRawResponse = request.CaptureRawResponse,
        };
        return RunOperation(controls, "Embeddings", async execution =>
        {
            if(request.IdempotencyKey is not null)
                throw new ArgumentException("Mistral does not document idempotency keys.");

            var payload = EmbeddingPayload(request, out var inputs, out var model);
            execution.Metadata = execution.Metadata with { RequestedModel = model.ModelId };
            return await Send(execution, controls, null, HttpMethod.Post, "embeddings", JsonContent.Create(payload), async response =>
            {
                using var document = await ReadEnvelope(response, execution).ConfigureAwait(false);
                RetainEnvelope(document.RootElement, controls, execution);
                return ReadEmbeddings(document.RootElement, inputs.Length, request.Dimensions, execution);
            }).ConfigureAwait(false);
        }, cancellationToken);
    }

    Dictionary<string, object?> EmbeddingPayload(EmbeddingRequest request, out string[] inputs, out ModelSelection model)
    {
        inputs = request.Inputs?.ToArray() ?? throw new ArgumentException("Inputs are required.");
        if(inputs.Length == 0 || inputs.Any(String.IsNullOrWhiteSpace) || request.Dimensions is <= 0 || request.ModelSelection is null)
            throw new ArgumentException("Invalid embedding settings.");

        model = SelectModel(request.ModelSelection, _embeddingProfiles);
        if(model.ReasoningEffort is not null)
            throw new ArgumentException("Embedding reasoning is unsupported.");

        var payload = new Dictionary<string, object?> { ["model"] = model.ModelId, ["input"] = inputs, ["encoding_format"] = "float", ["output_dtype"] = "float" };
        if(request.Dimensions is { } dimensions)
            payload["output_dimension"] = dimensions;

        return payload;
    }

    static JsonElement Property(JsonElement root, string name) => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value) ? value : default;

    static StructuredResult<IReadOnlyList<IReadOnlyList<float>>> ReadEmbeddings(JsonElement root, int count, int? dimensions,
        MistralExecution execution)
    {
        StructuredResult<IReadOnlyList<IReadOnlyList<float>>> Invalid() => execution.Failure<IReadOnlyList<IReadOnlyList<float>>>(StructuredErrorKind.InvalidResponse);
        var data = Property(root, "data");
        if(String.IsNullOrWhiteSpace(StringValue(root, "model")) || data.ValueKind != JsonValueKind.Array || data.GetArrayLength() != count)
            return Invalid();

        var ordered = new IReadOnlyList<float>[count];
        int? length = dimensions;
        foreach(var item in data.EnumerateArray())
        {
            var index = Property(item, "index");
            var embedding = Property(item, "embedding");
            if(index.ValueKind != JsonValueKind.Number || !index.TryGetInt32(out var position) || position < 0 || position >= count ||
                ordered[position] is not null || embedding.ValueKind != JsonValueKind.Array || embedding.GetArrayLength() == 0)
                return Invalid();

            length ??= embedding.GetArrayLength();
            if(embedding.GetArrayLength() != length)
                return Invalid();

            var vector = new float[length.Value];
            var component = 0;
            foreach(var value in embedding.EnumerateArray())
            {
                if(value.ValueKind != JsonValueKind.Number || !value.TryGetSingle(out var number) || !Single.IsFinite(number))
                    return Invalid();

                vector[component++] = number;
            }

            ordered[position] = Array.AsReadOnly(vector);
        }

        if(ordered.Any(x => x is null))
            return Invalid();

        return StructuredResult<IReadOnlyList<IReadOnlyList<float>>>.Success(Array.AsReadOnly(ordered), execution.Metadata);
    }
}
