using System.Text;
using System.Text.Json;
using Structly.AI.Embeddings;

namespace Structly.AI.OpenAI;

/// <summary>Transport controls shared by batch and file operations.</summary>
public sealed record BatchOperationOptions
{
    /// <summary>Gets operation credentials overriding the client without fallback.</summary>
    public Func<CancellationToken, ValueTask<string?>>? CredentialResolver { get; init; }
    /// <summary>Gets a total operation budget overriding the client default.</summary>
    public TimeSpan? TotalTimeout { get; init; }
    /// <summary>Gets the application correlation ID.</summary>
    public string? CorrelationId { get; init; }
    /// <summary>Gets an idempotency header for supported POST operations; the library does not retry.</summary>
    public string? IdempotencyKey { get; init; }
    /// <summary>Gets the import observer overriding the client observer.</summary>
    public Func<StructuredUsageEvent, CancellationToken, ValueTask>? UsageObserver { get; init; }
    /// <summary>Gets the aggregate import download ceiling; defaults to 256 MiB.</summary>
    public long MaxDownloadBytes { get; init; } = 256L * 1024 * 1024;
}

/// <summary>One embedding request with a unique application custom ID.</summary>
public sealed record EmbeddingBatchItem(string CustomId, EmbeddingRequest Request);
/// <summary>One typed Responses request with a unique application custom ID.</summary>
public sealed record ResponseBatchItem(string CustomId, StructuredRequest Request);

/// <summary>Persisted item identity, contract and capture settings; contains no credentials or delegates.</summary>
public sealed record BatchManifestItem
{
    /// <summary>Gets the application custom ID used to correlate provider results.</summary>
    public required string CustomId { get; init; }
    /// <summary>Gets the original zero-based input position.</summary>
    public required int Position { get; init; }
    /// <summary>Gets the execution ID generated once during preparation, reused on import.</summary>
    public required Guid ExecutionId { get; init; }
    /// <summary>Gets the application correlation ID.</summary>
    public string? CorrelationId { get; init; }
    /// <summary>Gets the resolved requested model ID used in the submitted payload.</summary>
    public required string RequestedModel { get; init; }
    /// <summary>Gets the embedding input count.</summary>
    public int InputCount { get; init; }
    /// <summary>Gets the requested embedding dimensions when specified.</summary>
    public int? Dimensions { get; init; }
    /// <summary>Gets whether import retains the sensitive provider envelope.</summary>
    public bool CaptureRawResponse { get; init; }
    /// <summary>Gets whether import retains sensitive output text.</summary>
    public bool CaptureOutputText { get; init; }
    /// <summary>Gets application-owned vocabulary snapshots used to regenerate the schema.</summary>
    public Dictionary<string, string[]>? Vocabularies { get; init; }
    /// <summary>Gets the SHA-256 fingerprint of the emitted typed schema.</summary>
    public string? SchemaFingerprint { get; init; }
}

/// <summary>Versioned persistence data for batch correlation and schema validation.</summary>
public sealed record BatchManifest
{
    /// <summary>Gets the manifest format version; currently 1.</summary>
    public int Version { get; init; } = 1;
    /// <summary>Gets the batch endpoint, /v1/embeddings or /v1/responses.</summary>
    public required string Endpoint { get; init; }
    /// <summary>Gets items in original input order.</summary>
    public required IReadOnlyList<BatchManifestItem> Items { get; init; }
    /// <summary>Serializes manifest persistence data without credentials or delegates.</summary>
    public string ToJson() => JsonSerializer.Serialize(this);
    /// <summary>Deserializes a manifest; imports validate its version and contract before HTTP execution.</summary>
    public static BatchManifest FromJson(string json) => JsonSerializer.Deserialize<BatchManifest>(json) ?? throw new ArgumentException("Manifest is required.", nameof(json));
}

/// <summary>Prepared immutable JSONL and manifest snapshots for submission and persistence.</summary>
public sealed class PreparedBatch
{
    readonly byte[] _jsonl;
    readonly string _manifest;
    internal PreparedBatch(byte[] jsonl, BatchManifest manifest) { _jsonl = jsonl; _manifest = manifest.ToJson(); }
    /// <summary>Gets a fresh manifest snapshot; caller mutation does not affect preparation.</summary>
    public BatchManifest Manifest => BatchManifest.FromJson(_manifest);
    /// <summary>Gets the complete UTF-8 JSONL as text.</summary>
    public string Jsonl => Encoding.UTF8.GetString(_jsonl);
    /// <summary>Writes prepared UTF-8 JSONL and leaves the destination stream open.</summary>
    public ValueTask WriteJsonlAsync(Stream destination, CancellationToken cancellationToken = default) => destination.WriteAsync(_jsonl, cancellationToken);
}

/// <summary>Batch file metadata returned by the provider.</summary>
public sealed record OpenAiFile
{
    /// <summary>Gets the provider resource identifier.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the provider filename.</summary>
    public required string Filename { get; init; }
    /// <summary>Gets the provider-reported file size in bytes.</summary>
    public required long Bytes { get; init; }
    /// <summary>Gets the provider file purpose.</summary>
    public string? Purpose { get; init; }
}

/// <summary>Provider batch state and available result file identifiers.</summary>
public sealed record OpenAiBatch
{
    /// <summary>Gets the provider resource identifier.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the provider batch status.</summary>
    public required string Status { get; init; }
    /// <summary>Gets the submitted batch file ID.</summary>
    public required string InputFileId { get; init; }
    /// <summary>Gets the batch endpoint, /v1/embeddings or /v1/responses.</summary>
    public required string Endpoint { get; init; }
    /// <summary>Gets the available output file ID, if any.</summary>
    public string? OutputFileId { get; init; }
    /// <summary>Gets the available error file ID, if any.</summary>
    public string? ErrorFileId { get; init; }
    /// <summary>Gets whether status is completed, failed, expired or cancelled.</summary>
    public bool IsTerminal => Status is "completed" or "failed" or "expired" or "cancelled";
}

/// <summary>One provider batch listing page.</summary>
public sealed record OpenAiBatchPage
{
    /// <summary>Gets the batches in this page.</summary>
    public required IReadOnlyList<OpenAiBatch> Data { get; init; }
    /// <summary>Gets whether another page is available.</summary>
    public bool HasMore { get; init; }
    /// <summary>Gets the cursor for a subsequent listing.</summary>
    public string? LastId { get; init; }
}
