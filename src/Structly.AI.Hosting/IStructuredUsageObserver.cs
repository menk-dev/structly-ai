namespace Structly.AI.Hosting;

/// <summary>Records best-effort usage within a separate DI scope per notification.</summary>
public interface IStructuredUsageObserver
{
    /// <summary>Records usage, respecting the callback cancellation token.</summary>
    ValueTask ObserveAsync(StructuredUsageEvent usage, CancellationToken cancellationToken);
}
