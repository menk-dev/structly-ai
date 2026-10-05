namespace Structly.AI.OpenAI;

/// <summary>Prewarm input and optional strict format. No generation or continuation controls.</summary>
public sealed record OpenAiPrewarmRequest
{
    /// <summary>Gets input/cache/execution controls; generation/progress/continuation options are rejected.</summary>
    public required StructuredRequest Request { get; init; }
    /// <summary>Gets optional nonblank instructions.</summary>
    public string? Instructions { get; init; }
}
