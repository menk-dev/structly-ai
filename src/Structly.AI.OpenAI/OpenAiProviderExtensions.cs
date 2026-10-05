namespace Structly.AI.OpenAI;

/// <summary>Selects the OpenAI provider.</summary>
public static class OpenAiProviderExtensions
{
    /// <summary>Selects OpenAI and its deferred options callback.</summary>
    public static void ConfigureOpenAiProvider(this AiProviderBuilder builder, Action<OpenAiOptions>? configure = null)
        => builder.ConfigureProvider(new AiProviderDescriptor<OpenAiClient, OpenAiOptions>(OpenAiOptions.SectionName, new OpenAiClientFactory()), configure);
}
