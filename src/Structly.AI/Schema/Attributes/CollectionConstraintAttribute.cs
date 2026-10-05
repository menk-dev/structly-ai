namespace Structly.AI;

/// <summary>Constrains collection cardinality. Unspecified bounds use -1.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class CollectionConstraintAttribute : Attribute
{
    /// <summary>Gets or sets the minimum item count, or -1 when unspecified.</summary>
    public int MinItems { get; init; } = -1;
    /// <summary>Gets or sets the maximum item count, or -1 when unspecified.</summary>
    public int MaxItems { get; init; } = -1;
}
