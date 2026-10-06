namespace Structly.AI.OpenAI;

/// <summary>Explicit provider request and sensitive capture controls.</summary>
public sealed record OpenAiResponseOptions
{
    /// <summary>Gets whether the provider may store this response. Null inherits the client setting, which defaults to true.</summary>
    public bool? Store { get; init; }
    /// <summary>Gets an earlier provider-stored response ID. Instructions and schema are resent.</summary>
    public string? PreviousResponseId { get; init; }
    /// <summary>Gets a cache accounting/routing key; no cache-hit guarantee.</summary>
    public string? PromptCacheKey { get; init; }
    /// <summary>Gets modern prompt cache controls.</summary>
    public OpenAiCacheOptions? Cache { get; init; }
    /// <summary>Gets legacy cache retention, mutually exclusive with modern options and breakpoints.</summary>
    public OpenAiCacheRetention? CacheRetention { get; init; }
    /// <summary>Gets optional idempotency header passthrough; no deduplication guarantee.</summary>
    public string? IdempotencyKey { get; init; }
    /// <summary>Gets provider metadata (at most 16 pairs, keys 64 and values 512 characters).</summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
    /// <summary>Gets whether to retain the sensitive raw response envelope.</summary>
    public bool CaptureRawResponse { get; init; }
    /// <summary>Gets whether to retain sensitive output text.</summary>
    public bool CaptureOutputText { get; init; }
}
