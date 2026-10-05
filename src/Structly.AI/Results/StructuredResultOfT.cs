namespace Structly.AI;

/// <summary>A typed success or categorized failure with retained metadata.</summary>
public sealed class StructuredResult<T>
{
    StructuredResult(bool success, T? value, StructuredError? error, StructuredMetadata metadata,
        IEnumerable<StructuredWarning>? warnings)
    {
        IsSuccess = success;
        Value = value;
        Error = error;
        Metadata = metadata;
        Warnings = Array.AsReadOnly(warnings?.ToArray() ?? []);
    }

    /// <summary>Gets success independently of the value's default value.</summary>
    public bool IsSuccess { get; }
    /// <summary>Gets the successful value, or default on failure.</summary>
    public T? Value { get; }
    /// <summary>Gets the failure, or null on success.</summary>
    public StructuredError? Error { get; }
    /// <summary>Gets retained execution metadata.</summary>
    public StructuredMetadata Metadata { get; }
    /// <summary>Gets immutable nonfatal diagnostics.</summary>
    public IReadOnlyList<StructuredWarning> Warnings { get; }

    /// <summary>Creates a successful result.</summary>
    public static StructuredResult<T> Success(T value, StructuredMetadata metadata,
        IEnumerable<StructuredWarning>? warnings = null)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(metadata);
        return new(true, value, null, metadata, warnings);
    }

    /// <summary>Creates a failed result without a usable value.</summary>
    public static StructuredResult<T> Failure(StructuredError error, StructuredMetadata metadata,
        IEnumerable<StructuredWarning>? warnings = null)
    {
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(metadata);
        return new(false, default, error, metadata, warnings);
    }

    /// <summary>Returns the successful value or throws with the retained failure metadata.</summary>
    public T EnsureSuccess() => IsSuccess ? Value! : throw new StructuredOperationException(Error!, Metadata, Warnings);
}
