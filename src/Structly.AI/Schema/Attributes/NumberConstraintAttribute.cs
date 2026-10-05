namespace Structly.AI;

/// <summary>Constrains a number with inclusive bounds. Unspecified bounds use NaN.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class NumberConstraintAttribute : Attribute
{
    /// <summary>Gets or sets the inclusive minimum, or NaN when unspecified.</summary>
    public double Minimum { get; init; } = Double.NaN;
    /// <summary>Gets or sets the inclusive maximum, or NaN when unspecified.</summary>
    public double Maximum { get; init; } = Double.NaN;
}
