namespace Structly.AI.OpenAI;

/// <summary>Explicitly configured provider cache contract for an exact model ID.</summary>
public enum OpenAiCacheCompatibility
{
    /// <summary>Modern mode/TTL/breakpoint/diagnostic/prewarm controls.</summary>
    Modern,
    /// <summary>Earlier prompt_cache_retention controls.</summary>
    Legacy,
}
