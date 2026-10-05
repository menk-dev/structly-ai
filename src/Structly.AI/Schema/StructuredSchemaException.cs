namespace Structly.AI;

/// <summary>A static schema or vocabulary cannot satisfy the output contract.</summary>
public sealed class StructuredSchemaException : ArgumentException
{
    /// <summary>Creates an exception from schema diagnostics.</summary>
    public StructuredSchemaException(IEnumerable<StructuredIssue> issues)
        : base("The structured output contract is invalid. Inspect Issues for paths and corrections.")
    {
        ArgumentNullException.ThrowIfNull(issues);
        Issues = Array.AsReadOnly(issues.ToArray());
        if(Issues.Count == 0)
            throw new ArgumentException("At least one issue is required.", nameof(issues));
    }

    /// <summary>Gets immutable actionable diagnostics.</summary>
    public IReadOnlyList<StructuredIssue> Issues { get; }
}
