namespace Structly.AI;

/// <summary>Restricts a string or the items of a string collection to a request vocabulary.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class DynamicVocabularyAttribute(string setName) : Attribute
{
    /// <summary>Gets the vocabulary key. Values are supplied when resolving a schema or output.</summary>
    public string SetName { get; } = setName;
}
