namespace Structly.AI.OpenAI;

/// <summary>Legacy retention controls; model support remains provider policy.</summary>
public enum OpenAiCacheRetention
{
    /// <summary>In-memory retention.</summary>
    InMemory,
    /// <summary>Extended retention.</summary>
    Hours24,
}
