namespace Structly.AI;

/// <summary>A path-specific diagnostic using serialized names and [] for collection items.</summary>
public sealed record StructuredIssue(string Path, string Code, string Message);
