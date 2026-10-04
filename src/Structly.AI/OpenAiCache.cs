using System.Collections.Frozen;

namespace Structly.AI.OpenAI;

/// <summary>Explicitly configured provider cache contract for an exact model ID.</summary>
public enum OpenAiCacheCompatibility
{
    /// <summary>Modern mode/TTL/breakpoint/diagnostic/prewarm controls.</summary>
    Modern,
    /// <summary>Earlier prompt_cache_retention controls.</summary>
    Legacy
}

/// <summary>Modern cache mode.</summary>
public enum OpenAiCacheMode
{
    /// <summary>Provider-selected boundaries plus optional explicit markers.</summary>
    Implicit,
    /// <summary>Only caller-marked boundaries.</summary>
    Explicit
}

/// <summary>Legacy retention controls; model support remains provider policy.</summary>
public enum OpenAiCacheRetention
{
    /// <summary>In-memory retention.</summary>
    InMemory,
    /// <summary>Extended retention.</summary>
    Hours24
}

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

/// <summary>Provider diagnostics independent of actual usage; unknown strings are retained.</summary>
public sealed record CacheDiagnostics
{
    /// <summary>Gets the provider outcome string.</summary>
    public string? Type { get; init; }
    /// <summary>Gets the provider reason string.</summary>
    public string? Reason { get; init; }
    /// <summary>Gets reported reusable comparison tokens.</summary>
    public long? ComparisonReusableTokens { get; init; }
    /// <summary>Gets reported missed cache tokens.</summary>
    public long? CacheMissedTokens { get; init; }
}

/// <summary>A metadata-only completed cache prewarm.</summary>
public sealed record OpenAiPrewarmResult;

/// <summary>Prewarm input and optional strict format. No generation or continuation controls.</summary>
public sealed record OpenAiPrewarmRequest
{
    /// <summary>Gets input/cache/execution controls; generation/progress/continuation options are rejected.</summary>
    public required StructuredRequest Request { get; init; }
    /// <summary>Gets optional nonblank instructions.</summary>
    public string? Instructions { get; init; }
}

static class OpenAiCacheDefaults
{
    public static FrozenDictionary<string, OpenAiCacheCompatibility> Models { get; } = new Dictionary<string, OpenAiCacheCompatibility>(StringComparer.Ordinal)
    {
        ["gpt-6-luna"] = OpenAiCacheCompatibility.Modern,
        ["gpt-6-sol"] = OpenAiCacheCompatibility.Modern,
        ["gpt-6-astra"] = OpenAiCacheCompatibility.Modern
    }.ToFrozenDictionary(StringComparer.Ordinal);
}
