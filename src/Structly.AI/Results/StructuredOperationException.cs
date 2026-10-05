namespace Structly.AI;

/// <summary>An explicitly requested exception view of a categorized failure.</summary>
public sealed class StructuredOperationException : Exception
{
    /// <summary>Creates an exception retaining the original error and accounting metadata.</summary>
    public StructuredOperationException(StructuredError error, StructuredMetadata metadata,
        IReadOnlyList<StructuredWarning> warnings) : base((error ?? throw new ArgumentNullException(nameof(error))).Message)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(warnings);
        Error = error;
        Metadata = metadata;
        Warnings = Array.AsReadOnly(warnings.ToArray());
    }
    /// <summary>Gets the failure.</summary>
    public StructuredError Error { get; }
    /// <summary>Gets execution metadata.</summary>
    public StructuredMetadata Metadata { get; }
    /// <summary>Gets immutable warnings.</summary>
    public IReadOnlyList<StructuredWarning> Warnings { get; }
}
