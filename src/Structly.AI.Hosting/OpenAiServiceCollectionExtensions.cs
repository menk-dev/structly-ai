using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Structly.AI.OpenAI;

namespace Structly.AI.Hosting;

/// <summary>Registers Structly.AI with .NET dependency injection and configuration.</summary>
public static class OpenAiServiceCollectionExtensions
{
    /// <summary>Registers an observer resolved in a new async DI scope for each notification.</summary>
    /// <remarks>Overrides the options observer. Request and import observers take precedence.</remarks>
    public static IHttpClientBuilder AddUsageObserver<TObserver>(this IHttpClientBuilder builder)
        where TObserver : class, IStructuredUsageObserver
    {
        ArgumentNullException.ThrowIfNull(builder);
        if(builder.Name != typeof(OpenAiClient).FullName)
            throw new ArgumentException("Usage observers require the Structly OpenAI HTTP client builder.", nameof(builder));

        if(builder.Services.Any(descriptor => descriptor.ServiceType == typeof(ScopedUsageObserver)))
            throw new InvalidOperationException("A usage observer is already registered.");

        builder.Services.TryAddScoped<TObserver>();
        builder.Services.AddSingleton(new ScopedUsageObserver(typeof(TObserver)));
        return builder;
    }

    /// <summary>Registers a client using callback configuration and startup validation.</summary>
    public static IHttpClientBuilder AddStructlyOpenAi(this IServiceCollection services, Action<OpenAiHostingOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        return services.AddStructlyOpenAi(new ConfigurationBuilder().Build().GetSection(OpenAiHostingOptions.SectionName), configure);
    }

    /// <summary>Registers a transient client, binds the selected section and validates settings at host startup.</summary>
    /// <remarks>Returns the HTTP builder for handler and transport configuration. Existing clients retain their settings snapshot.</remarks>
    public static IHttpClientBuilder AddStructlyOpenAi(this IServiceCollection services,
        IConfiguration configuration, Action<OpenAiHostingOptions>? configure = null,
        string sectionName = OpenAiHostingOptions.SectionName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);
        return services.AddStructlyOpenAi(configuration.GetSection(sectionName), configure);
    }

    /// <summary>Registers a transient client using an explicit configuration section and optional runtime callbacks.</summary>
    public static IHttpClientBuilder AddStructlyOpenAi(this IServiceCollection services,
        IConfigurationSection section, Action<OpenAiHostingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(section);
        var options = services.AddOptions<OpenAiHostingOptions>().Bind(section);
        if(configure is not null)
            options.Configure(configure);

        options.ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<OpenAiHostingOptions>, OpenAiHostingOptionsValidator>());
        return services.AddHttpClient(typeof(OpenAiClient).FullName!, http => http.Timeout = Timeout.InfiniteTimeSpan)
            .AddTypedClient<OpenAiClient>((http, provider) => new OpenAiClient(http,
                provider.GetRequiredService<IOptionsMonitor<OpenAiHostingOptions>>().CurrentValue.ToClientOptions(
                    provider.GetService<ScopedUsageObserver>()?.CreateCallback(provider.GetRequiredService<IServiceScopeFactory>()))));
    }
}
