namespace Structly.AI.OpenAI;

/// <summary>A rubric level. Its position in the task defines its zero-based score index.</summary>
public sealed record DecisionLevel(string Label, string? Description = null);
