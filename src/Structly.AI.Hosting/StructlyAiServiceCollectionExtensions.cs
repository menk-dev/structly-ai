using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Structly.AI.Hosting;

/// <summary>Registers provider clients and named structured tasks.</summary>
public static class StructlyAiServiceCollectionExtensions
{
    /// <summary>Registers one provider and immutable named tasks.</summary>
    public static IHttpClientBuilder AddStructlyAi(this IServiceCollection services, Action<StructlyAiBuilder> configure)
        => AddStructlyAi(services, new ConfigurationBuilder().Build(), configure);

    /// <summary>Registers one provider and immutable named tasks.</summary>
    public static IHttpClientBuilder AddStructlyAi(this IServiceCollection services, IConfiguration configuration,
        Action<StructlyAiBuilder> configure, string sectionName = "Structly")
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(configure);
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);
        if(services.Any(x => x.ServiceType == typeof(StructlyAi)))
            throw new InvalidOperationException("StructlyAi is already registered.");

        var builder = new StructlyAiBuilder();
        System.Collections.Frozen.FrozenDictionary<string, object> tasks;
        try
        {
            configure(builder);
        }
        finally
        {
            tasks = builder.Freeze();
        }

        var visitor = new ProviderVisitor(services, configuration.GetSection(sectionName));
        builder.VisitProvider(visitor);
        services.AddSingleton(provider => new StructlyAi(provider.GetRequiredService<IServiceScopeFactory>(), tasks));
        return visitor.HttpBuilder!;
    }

    sealed class ProviderVisitor(IServiceCollection services, IConfiguration section) : IAiProviderVisitor
    {
        public IHttpClientBuilder? HttpBuilder { get; set; }
        /// <summary>Registers the selected concrete provider types.</summary>
        public void Visit<TClient, TOptions>(AiProviderDescriptor<TClient, TOptions> descriptor, Action<TOptions>? configure)
            where TClient : class, IStructuredClient where TOptions : class, new()
        {
            var options = services.AddOptions<TOptions>().Bind(section.GetSection(descriptor.SectionName));
            if(configure is not null)
                options.Configure(configure);

            options.PostConfigure<IServiceProvider>((settings, provider) =>
            {
                if(settings is IStructuredUsageOptions usage && provider.GetService<ScopedUsageObserver>() is { } observer)
                    usage.UsageObserver = observer.CreateCallback(provider.GetRequiredService<IServiceScopeFactory>());
            });

            options.ValidateOnStart();
            services.AddSingleton<IValidateOptions<TOptions>>(new ProviderValidator<TClient, TOptions>(descriptor.Factory));
            HttpBuilder = services.AddHttpClient(typeof(TClient).FullName!, http => http.Timeout = Timeout.InfiniteTimeSpan)
                .AddTypedClient<TClient>((http, provider) => descriptor.Factory.Create(http, provider.GetRequiredService<IOptionsMonitor<TOptions>>().CurrentValue));
            services.AddTransient<IStructuredClient>(provider => provider.GetRequiredService<TClient>());
        }
    }

    sealed class ProviderValidator<TClient, TOptions>(IStructuredClientFactory<TClient, TOptions> factory) : IValidateOptions<TOptions>
        where TClient : class, IStructuredClient where TOptions : class
    {
        /// <summary>Validates settings without resolving credentials.</summary>
        public ValidateOptionsResult Validate(string? name, TOptions options)
        {
            try
            {
                factory.Validate(options);
                return ValidateOptionsResult.Success;
            }
            catch(ArgumentException)
            {
                return ValidateOptionsResult.Fail("Invalid Structly provider settings. Check model selections, profiles, deadlines and response limits.");
            }
        }
    }
}
