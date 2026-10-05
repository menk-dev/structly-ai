namespace Structly.AI.OpenAI;

/// <summary>Number of items reporting each usage field, including reported zeros.</summary>
public sealed record BatchUsageCoverage
{
    /// <summary>Gets the number of items reporting InputTokens.</summary>
    public int InputTokens { get; init; }
    /// <summary>Gets the number of items reporting OutputTokens.</summary>
    public int OutputTokens { get; init; }
    /// <summary>Gets the number of items reporting TotalTokens.</summary>
    public int TotalTokens { get; init; }
    /// <summary>Gets the number of items reporting CachedInputTokens.</summary>
    public int CachedInputTokens { get; init; }
    /// <summary>Gets the number of items reporting CacheWriteTokens.</summary>
    public int CacheWriteTokens { get; init; }
    /// <summary>Gets the number of items reporting ReasoningTokens.</summary>
    public int ReasoningTokens { get; init; }
    /// <summary>Gets the number of items reporting InputTextTokens.</summary>
    public int InputTextTokens { get; init; }
    /// <summary>Gets the number of items reporting InputImageTokens.</summary>
    public int InputImageTokens { get; init; }
    /// <summary>Gets the number of items reporting OutputImageTokens.</summary>
    public int OutputImageTokens { get; init; }
    /// <summary>Gets the number of items reporting OutputTextTokens.</summary>
    public int OutputTextTokens { get; init; }
}
