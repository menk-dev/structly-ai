using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Structly.AI.Hosting;

/// <summary>Registers Structly.AI with .NET dependency injection and configuration.</summary>
public static class UsageObserverServiceCollectionExtensions
{
    /// <summary>Registers an observer resolved in a new async DI scope for each notification.</summary>
    /// <remarks>Overrides the options observer. Request and import observers take precedence.</remarks>
    public static IHttpClientBuilder AddUsageObserver<TObserver>(this IHttpClientBuilder builder)
        where TObserver : class, IStructuredUsageObserver
    {
        ArgumentNullException.ThrowIfNull(builder);
        if(!builder.Services.Any(descriptor => descriptor.ServiceType == typeof(StructlyAi)))
            throw new ArgumentException("Usage observers require a Structly provider registration.", nameof(builder));

        if(builder.Services.Any(descriptor => descriptor.ServiceType == typeof(ScopedUsageObserver)))
            throw new InvalidOperationException("A usage observer is already registered.");

        builder.Services.TryAddScoped<TObserver>();
        builder.Services.AddSingleton(new ScopedUsageObserver(typeof(TObserver)));
        return builder;
    }

}
