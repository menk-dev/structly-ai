namespace Structly.AI.Mistral;

/// <summary>Selects the Mistral provider for hosting.</summary>
public static class MistralProviderExtensions
{
    /// <summary>Selects Mistral with a deferred configuration callback.</summary>
    public static void ConfigureMistralProvider(this AiProviderBuilder builder, Action<MistralOptions>? configure = null)
        => builder.ConfigureProvider(new AiProviderDescriptor<MistralClient, MistralOptions>(MistralOptions.SectionName, new MistralClientFactory()), configure);
}
