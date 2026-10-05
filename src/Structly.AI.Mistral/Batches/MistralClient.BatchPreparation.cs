using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Structly.AI.Mistral;

public sealed partial class MistralClient
{
    /// <summary>Validates and snapshots one-model embedding inputs into JSONL and a versioned manifest.</summary>
    public PreparedBatch PrepareEmbeddingBatch(IEnumerable<EmbeddingBatchItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        using var lines = new MemoryStream();
        var manifest = new List<BatchManifestItem>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach(var item in items)
        {
            var request = item.Request;
            if(request.CredentialResolver is not null || request.TotalTimeout is not null || request.UsageObserver is not null || request.IdempotencyKey is not null)
                throw new ArgumentException("Use operation controls for batch transport.");

            var body = EmbeddingPayload(request, out var texts, out var model);
            body.Remove("model");
            CheckPreparedItem(manifest, ids, item.CustomId, model.ModelId!);
            WriteBatchLine(lines, new { custom_id = item.CustomId, body });
            manifest.Add(new() { CustomId = item.CustomId, Position = manifest.Count, ExecutionId = Guid.NewGuid(), CorrelationId = request.CorrelationId, RequestedModel = model.ModelId!, InputCount = texts.Length, Dimensions = request.Dimensions, CaptureRawResponse = request.CaptureRawResponse });
        }

        return Prepare(lines, manifest, "/v1/embeddings");
    }

    /// <summary>Validates and snapshots homogeneous typed chat completion inputs, including vocabulary and schema fingerprints.</summary>
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
            if(request.Stream || request.Progress is not null || request.InactivityTimeout is not null || request.CredentialResolver is not null || request.TotalTimeout is not null || request.UsageObserver is not null)
                throw new ArgumentException("Batch items cannot specify streaming or transport controls.");

            request = request with { MaxOutputTokens = request.MaxOutputTokens ?? task.ExecutionDefaults.MaxOutputTokens };
            var output = task.BindOutput(new() { Vocabularies = request.Vocabularies, OutputSpecification = request.OutputSpecification });
            var vocabularies = request.Vocabularies?.ToDictionary(x => x.Key, x => x.Value.ToArray(), StringComparer.Ordinal);
            var schema = output.CreateSchema();
            var model = SelectModel(request.ModelSelection ?? task.ModelSelection ?? _defaultModel);
            var body = ChatPayload(request, model, request.Instructions ?? task.Instructions, output);
            body.Remove("model");
            CheckPreparedItem(manifest, ids, item.CustomId, model.ModelId!);
            WriteBatchLine(lines, new { custom_id = item.CustomId, body });
            manifest.Add(new() { CustomId = item.CustomId, Position = manifest.Count, ExecutionId = Guid.NewGuid(), CorrelationId = request.CorrelationId, RequestedModel = model.ModelId!, Vocabularies = vocabularies, SchemaFingerprint = Fingerprint(schema), CaptureRawResponse = request is MistralRequest { CaptureRawResponse: true }, CaptureOutputText = request is MistralRequest { CaptureOutputText: true } });
        }

        return Prepare(lines, manifest, "/v1/chat/completions");
    }

    static string Fingerprint(JsonElement schema) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(schema.GetRawText())));
    static void CheckPreparedItem(List<BatchManifestItem> items, HashSet<string> ids, string id, string model)
    {
        if(items.Count >= 1000000 || String.IsNullOrWhiteSpace(id) || id.Length > 64 || !ids.Add(id))
            throw new ArgumentException("Batch custom IDs must be nonblank, unique and at most 64 characters; jobs support at most 1,000,000 requests.");

        if(items.Count > 0 && items[0].RequestedModel != model)
            throw new ArgumentException("A batch must use one resolved model.");
    }

    static void WriteBatchLine(MemoryStream destination, object line)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(line);
        if(destination.Length + bytes.Length + 1 > 512L * 1024 * 1024)
            throw new ArgumentException("Batch files support at most 512 MiB.");

        destination.Write(bytes);
        destination.WriteByte(10);
    }

    static PreparedBatch Prepare(MemoryStream lines, List<BatchManifestItem> items, string endpoint)
    {
        ValidateManifest(new() { Endpoint = endpoint, Items = items }, endpoint);
        return new(lines.ToArray(), new() { Endpoint = endpoint, Items = items.AsReadOnly() });
    }

}
