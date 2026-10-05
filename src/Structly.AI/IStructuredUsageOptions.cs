namespace Structly.AI;

/// <summary>Provider settings supporting a default usage observer.</summary>
public interface IStructuredUsageOptions
{
    /// <summary>Gets or sets the provider's best-effort usage observer.</summary>
    Func<StructuredUsageEvent, CancellationToken, ValueTask>? UsageObserver { get; set; }
}
