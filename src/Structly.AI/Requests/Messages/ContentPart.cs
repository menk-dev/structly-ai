namespace Structly.AI;

/// <summary>A supported input part.</summary>
public abstract record ContentPart
{
    /// <summary>Gets whether to mark an explicit provider cache boundary after this part.</summary>
    public bool CacheBreakpoint { get; init; }
}
