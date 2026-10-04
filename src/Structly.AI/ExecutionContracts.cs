namespace Structly.AI;

/// <summary>Safe progress kinds; reasoning text is never exposed.</summary>
public enum StructuredProgressKind
{
    /// <summary>Execution started.</summary>
    Started,
    /// <summary>An output text fragment arrived.</summary>
    OutputTextDelta,
    /// <summary>An explicitly requested reasoning summary fragment arrived.</summary>
    ReasoningSummaryDelta,
    /// <summary>Terminal output validation finished.</summary>
    Completed
}

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

/// <summary>One best-effort usage notification, with outcome before observer execution.</summary>
public sealed record StructuredUsageEvent
{
    /// <summary>Gets the provider.</summary>
    public required string Provider { get; init; }
    /// <summary>Gets the requested model, the accounting fallback when resolved model is absent.</summary>
    public required string RequestedModel { get; init; }
    /// <summary>Gets reported usage; unknown counts remain null.</summary>
    public required StructuredUsage Usage { get; init; }
    /// <summary>Gets the resolved model when reported.</summary>
    public string? ResolvedModel => Metadata.ResolvedModel;
    /// <summary>Gets accounting metadata; use ExecutionId to deduplicate against results.</summary>
    public required StructuredMetadata Metadata { get; init; }
    /// <summary>Gets whether output processing succeeded.</summary>
    public bool Succeeded { get; init; }
    /// <summary>Gets the failure kind, if any.</summary>
    public StructuredErrorKind? FailureKind { get; init; }
    /// <summary>Gets whether the caller had cancelled before notification.</summary>
    public bool CallerCancelled { get; init; }
}

/// <summary>Caller cancellation retaining available accounting metadata.</summary>
public sealed class StructuredOperationCanceledException : OperationCanceledException
{
    /// <summary>Creates an exception with the original caller token and immutable warnings.</summary>
    public StructuredOperationCanceledException(StructuredMetadata metadata, IEnumerable<StructuredWarning> warnings,
        CancellationToken cancellationToken) : base("Structured operation cancelled by caller.", cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(warnings);
        Metadata = metadata;
        Warnings = Array.AsReadOnly(warnings.ToArray());
    }
    /// <summary>Gets captured accounting metadata.</summary>
    public StructuredMetadata Metadata { get; }
    /// <summary>Gets safe, immutable diagnostics.</summary>
    public IReadOnlyList<StructuredWarning> Warnings { get; }
}
