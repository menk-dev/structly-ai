namespace Structly.AI;

/// <summary>Request-scoped progress; text and summaries are sensitive host-owned data.</summary>
public sealed record StructuredProgress
{
    /// <summary>Gets the event kind.</summary>
    public required StructuredProgressKind Kind { get; init; }
    /// <summary>Gets an optional fragment, never raw reasoning.</summary>
    public string? TextDelta { get; init; }
    /// <summary>Gets the provider output index.</summary>
    public int? OutputIndex { get; init; }
    /// <summary>Gets the provider summary index.</summary>
    public int? SummaryIndex { get; init; }
    /// <summary>Gets the known response ID.</summary>
    public string? ResponseId { get; init; }
    /// <summary>Gets the known resolved model.</summary>
    public string? ModelId { get; init; }
}
