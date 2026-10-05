namespace Structly.AI;

/// <summary>Describes a provider configuration section and client factory.</summary>
public sealed record AiProviderDescriptor<TClient, TOptions>(string SectionName, IStructuredClientFactory<TClient, TOptions> Factory)
    where TClient : class, IStructuredClient where TOptions : class, new();
