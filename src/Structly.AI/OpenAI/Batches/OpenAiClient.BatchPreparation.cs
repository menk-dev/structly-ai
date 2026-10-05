using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Structly.AI.OpenAI;

public sealed partial class OpenAiClient
{
    /// <summary>Validates and snapshots one-model embedding inputs into JSONL and a versioned manifest.</summary>
    public PreparedBatch PrepareEmbeddingBatch(IEnumerable<EmbeddingBatchItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        using var lines = new MemoryStream();
        var manifest = new List<BatchManifestItem>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var inputs = 0;
        foreach(var item in items)
        {
            var request = item.Request;
            if(request.CredentialResolver is not null || request.TotalTimeout is not null || request.UsageObserver is not null || request.IdempotencyKey is not null)
                throw new ArgumentException("Use operation controls for batch transport.");

            var texts = request.Inputs.ToArray();
            if(texts.Length is < 1 or > 2048 || texts.Any(String.IsNullOrWhiteSpace) || request.Dimensions is <= 0)
                throw new ArgumentException("Invalid embedding inputs.");

            inputs = checked(inputs + texts.Length);
            if(inputs > 50000)
                throw new ArgumentException("Embedding batches support at most 50,000 inputs.");

            var model = SelectModel(request.ModelSelection, _options.EmbeddingProfiles);
            if(model.ReasoningEffort is not null)
                throw new ArgumentException("Embedding reasoning is unsupported.");

            var body = new Dictionary<string, object?> { ["model"] = model.ModelId, ["input"] = texts, ["encoding_format"] = "float" };
            if(request.Dimensions is { } dimensions)
                body["dimensions"] = dimensions;

            CheckPreparedItem(manifest, ids, item.CustomId, model.ModelId!);
            WriteBatchLine(lines, new { custom_id = item.CustomId, method = "POST", url = "/v1/embeddings", body });
            manifest.Add(new() { CustomId = item.CustomId, Position = manifest.Count, ExecutionId = Guid.NewGuid(), CorrelationId = request.CorrelationId, RequestedModel = model.ModelId!, InputCount = texts.Length, Dimensions = request.Dimensions, CaptureRawResponse = request.CaptureRawResponse });
        }

        return Prepare(lines, manifest, "/v1/embeddings");
    }

    /// <summary>Validates and snapshots homogeneous typed Responses inputs, including vocabulary and schema fingerprints.</summary>
    public PreparedBatch PrepareResponseBatch<T>(StructuredTask<T> task, IEnumerable<ResponseBatchItem> items)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(items);
        using var lines = new MemoryStream();
        var manifest = new List<BatchManifestItem>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach(var item in items)
        {
            var request = item.Request;
            ValidateResponseRequest(request);
            if(request.Stream || request.Progress is not null || request.CredentialResolver is not null || request.TotalTimeout is not null || request.UsageObserver is not null || request.OpenAi.IdempotencyKey is not null)
                throw new ArgumentException("Batch items cannot specify streaming or transport controls.");

            var instructions = request.Instructions ?? task.Instructions;
            if(String.IsNullOrWhiteSpace(instructions))
                throw new ArgumentException("Typed execution requires instructions.");

            var vocabularies = request.Vocabularies?.ToDictionary(x => x.Key, x => x.Value.ToArray(), StringComparer.Ordinal);
            var values = vocabularies?.ToDictionary(x => x.Key, x => (IReadOnlyList<string>)x.Value);
            var schema = task.CreateSchema(values);
            var model = SelectModel(request.ModelSelection ?? task.ModelSelection ?? _options.DefaultModel);
            var body = ResponsePayload(request, model, instructions, request.OutputSpecification is { } specification ? task.CreateOutputSpecification(specification, values) : null);
            var format = new Dictionary<string, object?> { ["type"] = "json_schema", ["name"] = task.SchemaName, ["schema"] = schema, ["strict"] = true };
            if(task.Description is not null)
                format["description"] = task.Description;

            body["text"] = new { format };
            CheckPreparedItem(manifest, ids, item.CustomId, model.ModelId!);
            WriteBatchLine(lines, new { custom_id = item.CustomId, method = "POST", url = "/v1/responses", body });
            manifest.Add(new() { CustomId = item.CustomId, Position = manifest.Count, ExecutionId = Guid.NewGuid(), CorrelationId = request.CorrelationId, RequestedModel = model.ModelId!, Vocabularies = vocabularies, SchemaFingerprint = Fingerprint(schema), CaptureRawResponse = request.OpenAi.CaptureRawResponse, CaptureOutputText = request.OpenAi.CaptureOutputText });
        }

        return Prepare(lines, manifest, "/v1/responses");
    }

    static string Fingerprint(JsonElement schema) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(schema.GetRawText())));
    static void CheckPreparedItem(List<BatchManifestItem> items, HashSet<string> ids, string id, string model)
    {
        if(items.Count >= 50000 || String.IsNullOrWhiteSpace(id) || id.Length > 64 || !ids.Add(id))
            throw new ArgumentException("Batch custom IDs must be nonblank, unique and at most 64 characters; jobs support at most 50,000 requests.");

        if(items.Count > 0 && items[0].RequestedModel != model)
            throw new ArgumentException("A batch must use one resolved model.");
    }

    static void WriteBatchLine(MemoryStream destination, object line)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(line);
        if(destination.Length + bytes.Length + 1 > 200L * 1024 * 1024)
            throw new ArgumentException("Batch files support at most 200 MiB.");

        destination.Write(bytes);
        destination.WriteByte(10);
    }

    static PreparedBatch Prepare(MemoryStream lines, List<BatchManifestItem> items, string endpoint)
    {
        ValidateManifest(new() { Endpoint = endpoint, Items = items }, endpoint);
        return new(lines.ToArray(), new() { Endpoint = endpoint, Items = items.AsReadOnly() });
    }

}
