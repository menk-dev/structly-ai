namespace Structly.AI;

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
