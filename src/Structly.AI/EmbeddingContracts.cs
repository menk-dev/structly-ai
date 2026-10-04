namespace Structly.AI.Embeddings;

/// <summary>An ordered text batch for float embeddings.</summary>
public sealed record EmbeddingRequest
{
    /// <summary>Gets nonempty, nonblank input strings.</summary>
    public required IReadOnlyList<string> Inputs { get; init; }
    /// <summary>Gets explicit model/profile selection; reasoning is unsupported.</summary>
    public required ModelSelection ModelSelection { get; init; }
    /// <summary>Gets optional positive vector dimensions, for compatible models.</summary>
    public int? Dimensions { get; init; }
    /// <summary>Gets an optional total execution budget.</summary>
    public TimeSpan? TotalTimeout { get; init; }
    /// <summary>Gets credentials overriding the client without fallback.</summary>
    public Func<CancellationToken, ValueTask<string?>>? CredentialResolver { get; init; }
    /// <summary>Gets an optional usage observer overriding the client.</summary>
    public Func<StructuredUsageEvent, CancellationToken, ValueTask>? UsageObserver { get; init; }
    /// <summary>Gets a local correlation identifier.</summary>
    public string? CorrelationId { get; init; }
    /// <summary>Gets optional idempotency header passthrough without a deduplication guarantee.</summary>
    public string? IdempotencyKey { get; init; }
    /// <summary>Gets whether to retain the sensitive provider envelope.</summary>
    public bool CaptureRawResponse { get; init; }
}
