using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Structly.AI.OpenAI;

namespace Structly.AI.Hosting;

/// <summary>Registers Structly.AI with .NET dependency injection and configuration.</summary>
public static class OpenAiServiceCollectionExtensions
{
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
        if (configure is not null) options.Configure(configure);
        options.ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<OpenAiHostingOptions>, OpenAiHostingOptionsValidator>());
        return services.AddHttpClient(typeof(OpenAiClient).FullName!, http => http.Timeout = Timeout.InfiniteTimeSpan)
            .AddTypedClient<OpenAiClient>((http, provider) => new OpenAiClient(http,
                provider.GetRequiredService<IOptionsMonitor<OpenAiHostingOptions>>().CurrentValue.ToClientOptions()));
    }
}

sealed class OpenAiHostingOptionsValidator : IValidateOptions<OpenAiHostingOptions>
{
    public ValidateOptionsResult Validate(string? name, OpenAiHostingOptions options)
    {
        try
        {
            using var http = new HttpClient();
            _ = new OpenAiClient(http, options.ToClientOptions());
            return ValidateOptionsResult.Success;
        }
        catch (ArgumentException)
        {
            return ValidateOptionsResult.Fail("Invalid Structly OpenAI settings. Check model selections, profiles, cache compatibility, API directory URI, deadlines and response size limits.");
        }
    }
}
