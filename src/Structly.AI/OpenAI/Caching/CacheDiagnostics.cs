namespace Structly.AI.OpenAI;

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
