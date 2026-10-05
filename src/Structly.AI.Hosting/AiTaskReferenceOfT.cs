namespace Structly.AI.Hosting;

/// <summary>A named task identity carrying its output type. Definitions are registered separately.</summary>
public sealed record AiTaskReference<T>
{
    /// <summary>Creates a reference with a nonblank, case-sensitive name.</summary>
    public AiTaskReference(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    /// <summary>Gets the registered task name.</summary>
    public string Name { get; }
}
