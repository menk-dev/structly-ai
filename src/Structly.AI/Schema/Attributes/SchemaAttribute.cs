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
