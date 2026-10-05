namespace Structly.AI;

/// <summary>Runtime output settings captured when binding a task.</summary>
public sealed record OutputBindingOptions
{
    /// <summary>Gets vocabulary values to snapshot.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>>? Vocabularies { get; init; }
    /// <summary>Gets optional guidance to generate once.</summary>
    public OutputSpecificationOptions? OutputSpecification { get; init; }
}
