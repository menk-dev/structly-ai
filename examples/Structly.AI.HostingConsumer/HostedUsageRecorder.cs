using Structly.AI;
using Structly.AI.Hosting;

sealed class HostedUsageRecorder(UsageCounter counter) : IStructuredUsageObserver
{
    public ValueTask ObserveAsync(StructuredUsageEvent usage, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        counter.Record();
        return ValueTask.CompletedTask;
    }
}
