namespace Structly.AI;

/// <summary>Constrains a string. Unspecified lengths use -1.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class StringConstraintAttribute : Attribute
{
    /// <summary>Gets or sets the minimum Unicode scalar length, or -1 when unspecified.</summary>
    public int MinLength { get; init; } = -1;
    /// <summary>Gets or sets the maximum Unicode scalar length, or -1 when unspecified.</summary>
    public int MaxLength { get; init; } = -1;
    /// <summary>Gets or sets a portable regular expression with search semantics.</summary>
    public string? Pattern { get; init; }
    /// <summary>Gets or sets a supported JSON Schema format.</summary>
    public string? Format { get; init; }
}
