namespace Structly.AI.OpenAI;

/// <summary>An allowed choice and an optional explanation of when it applies.</summary>
public sealed record DecisionOption<T>(T Value, string? Description = null);
