namespace Structly.AI.OpenAI;

/// <summary>Validates and snapshots OpenAI settings without resolving credentials.</summary>
public sealed class OpenAiClientFactory : IStructuredClientFactory<OpenAiClient, OpenAiOptions>
{
    /// <summary>Validates settings without resolving credentials.</summary>
    public void Validate(OpenAiOptions options)
    {
        using var http = new HttpClient();
        _ = Create(http, options);
    }
    /// <summary>Creates a client with a settings snapshot.</summary>
    public OpenAiClient Create(HttpClient http, OpenAiOptions options) => new(http, options.ToClientOptions());
}
