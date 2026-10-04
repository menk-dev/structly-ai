using System.Text.Json;
using Structly.AI.Embeddings;

namespace Structly.AI.OpenAI;

public sealed partial class OpenAiClient
{
    /// <summary>Embeds one text batch and returns immutable finite vectors in input order.</summary>
    public Task<StructuredResult<IReadOnlyList<IReadOnlyList<float>>>> EmbedAsync(EmbeddingRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var controls = new StructuredRequest
        {
            TotalTimeout = request.TotalTimeout,
            CredentialResolver = request.CredentialResolver,
            UsageObserver = request.UsageObserver,
            CorrelationId = request.CorrelationId,
            OpenAi = new() { IdempotencyKey = request.IdempotencyKey, CaptureRawResponse = request.CaptureRawResponse }
        };
        return RunOperation(controls, "Embeddings", execution => EmbeddingCore(request, controls, execution), cancellationToken);
    }

    async Task<StructuredResult<IReadOnlyList<IReadOnlyList<float>>>> EmbeddingCore(EmbeddingRequest request,
        StructuredRequest controls, OpenAiExecution execution)
    {
        execution.CheckCancellation();
        string[] inputs;
        Dictionary<string, object?> payload;
        try
        {
            ValidateCommonRequest(controls);
            inputs = request.Inputs?.ToArray() ?? throw new ArgumentException("Inputs are required.");
            if (inputs.Length == 0 || inputs.Any(String.IsNullOrWhiteSpace) || request.Dimensions is <= 0 || request.ModelSelection is null)
                throw new ArgumentException("Invalid embedding batch/options.");
            var model = SelectModel(request.ModelSelection, _options.EmbeddingProfiles);
            if (model.ReasoningEffort is not null) throw new ArgumentException("Embeddings do not support reasoning.");
            execution.Metadata = execution.Metadata with { RequestedModel = model.ModelId };
            payload = new() { ["model"] = model.ModelId, ["input"] = inputs, ["encoding_format"] = "float" };
            if (request.Dimensions is { } dimensions) payload["dimensions"] = dimensions;
        }
        catch (ArgumentException) { return InvalidOptions<IReadOnlyList<IReadOnlyList<float>>>(execution); }
        return await Send(payload, "embeddings", controls, execution, null,
            root => ValueTask.FromResult(ReadEmbeddings(root, inputs.Length, request.Dimensions, execution))).ConfigureAwait(false);
    }

    static StructuredResult<IReadOnlyList<IReadOnlyList<float>>> ReadEmbeddings(JsonElement root, int count, int? dimensions,
        OpenAiExecution execution)
    {
        StructuredResult<IReadOnlyList<IReadOnlyList<float>>> Invalid() => LocalFailure<IReadOnlyList<IReadOnlyList<float>>>(execution, StructuredErrorKind.InvalidResponse);
        var data = Property(root, "data");
        if (String.IsNullOrWhiteSpace(Text(root, "model")) || data.ValueKind != JsonValueKind.Array || data.GetArrayLength() != count) return Invalid();
        var ordered = new IReadOnlyList<float>[count];
        int? length = dimensions;
        foreach (var item in data.EnumerateArray())
        {
            var index = Property(item, "index");
            var embedding = Property(item, "embedding");
            if (index.ValueKind != JsonValueKind.Number || !index.TryGetInt32(out var position) || position < 0 || position >= count ||
                ordered[position] is not null || embedding.ValueKind != JsonValueKind.Array || embedding.GetArrayLength() == 0) return Invalid();
            length ??= embedding.GetArrayLength();
            if (embedding.GetArrayLength() != length) return Invalid();
            var vector = new float[length.Value];
            var component = 0;
            foreach (var value in embedding.EnumerateArray())
            {
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetSingle(out var number) || !Single.IsFinite(number)) return Invalid();
                vector[component++] = number;
            }
            ordered[position] = Array.AsReadOnly(vector);
        }
        if (ordered.Any(x => x is null)) return Invalid();
        return StructuredResult<IReadOnlyList<IReadOnlyList<float>>>.Success(Array.AsReadOnly(ordered), execution.Metadata, execution.Warnings);
    }
}
