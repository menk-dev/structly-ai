namespace Structly.AI;

/// <summary>Describes the name and purpose of an output DTO.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public sealed class SchemaAttribute : Attribute
{
    /// <summary>Gets or sets the optional provider schema name.</summary>
    public string? Name { get; init; }
    /// <summary>Gets or sets the optional schema description.</summary>
    public string? Description { get; init; }
}

/// <summary>Restricts a string or the items of a string collection to a request vocabulary.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class DynamicVocabularyAttribute(string setName) : Attribute
{
    /// <summary>Gets the vocabulary key. Values are supplied when resolving a schema or output.</summary>
    public string SetName { get; } = setName;
}

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

/// <summary>Constrains a number with inclusive bounds. Unspecified bounds use NaN.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class NumberConstraintAttribute : Attribute
{
    /// <summary>Gets or sets the inclusive minimum, or NaN when unspecified.</summary>
    public double Minimum { get; init; } = Double.NaN;
    /// <summary>Gets or sets the inclusive maximum, or NaN when unspecified.</summary>
    public double Maximum { get; init; } = Double.NaN;
}

/// <summary>Constrains collection cardinality. Unspecified bounds use -1.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class CollectionConstraintAttribute : Attribute
{
    /// <summary>Gets or sets the minimum item count, or -1 when unspecified.</summary>
    public int MinItems { get; init; } = -1;
    /// <summary>Gets or sets the maximum item count, or -1 when unspecified.</summary>
    public int MaxItems { get; init; } = -1;
}
