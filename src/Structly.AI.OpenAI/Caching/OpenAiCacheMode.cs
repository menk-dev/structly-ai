namespace Structly.AI.OpenAI;

/// <summary>Modern cache mode.</summary>
public enum OpenAiCacheMode
{
    /// <summary>Provider-selected boundaries plus optional explicit markers.</summary>
    Implicit,
    /// <summary>Only caller-marked boundaries.</summary>
    Explicit,
}
