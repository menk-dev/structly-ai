namespace Structly.AI;

/// <summary>Ordered message content, snapshotted before credential resolution.</summary>
public sealed record StructuredMessage(MessageRole Role, IReadOnlyList<ContentPart> Content);
