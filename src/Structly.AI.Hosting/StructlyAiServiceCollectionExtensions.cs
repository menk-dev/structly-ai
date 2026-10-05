using Microsoft.Extensions.DependencyInjection;
using System.Collections.Frozen;

namespace Structly.AI.Hosting;

/// <summary>Registers the named AI task service.</summary>
public static class StructlyAiServiceCollectionExtensions
{
    /// <summary>Configures immutable named tasks and registers a singleton executor. Register the OpenAI client separately.</summary>
    public static IServiceCollection AddStructlyAi(this IServiceCollection services, Action<StructlyAiBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        if(services.Any(descriptor => descriptor.ServiceType == typeof(StructlyAi)))
            throw new InvalidOperationException("StructlyAi is already registered.");

        var builder = new StructlyAiBuilder();
        FrozenDictionary<string, object> tasks;
        try
        {
            configure(builder);
        }
        finally
        {
            tasks = builder.Freeze();
        }

        services.AddSingleton(provider => new StructlyAi(provider.GetRequiredService<IServiceScopeFactory>(), tasks));
        return services;
    }
}
