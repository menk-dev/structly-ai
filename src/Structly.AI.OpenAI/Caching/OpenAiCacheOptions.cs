namespace Structly.AI.OpenAI;

/// <summary>Modern cache controls; reuse and writes are never guaranteed.</summary>
public sealed record OpenAiCacheOptions
{
    /// <summary>Gets an optional mode; omission keeps provider default.</summary>
    public OpenAiCacheMode? Mode { get; init; }
    /// <summary>Gets an optional TTL. Currently only 30m is supported.</summary>
    public string? Ttl { get; init; }
    /// <summary>Gets a response ID for diagnostics only; does not load conversation history.</summary>
    public string? ComparisonResponseId { get; init; }
}
