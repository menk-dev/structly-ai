using Microsoft.Extensions.DependencyInjection;

namespace Structly.AI.Hosting;

sealed class ScopedUsageObserver(Type observerType)
{
    public Func<StructuredUsageEvent, CancellationToken, ValueTask> CreateCallback(IServiceScopeFactory scopeFactory)
        => async (usage, token) =>
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var observer = (IStructuredUsageObserver)scope.ServiceProvider.GetRequiredService(observerType);
            await observer.ObserveAsync(usage, token).ConfigureAwait(false);
        };
}
