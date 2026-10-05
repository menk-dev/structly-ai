namespace Structly.AI.Mistral;

/// <summary>Constructs Mistral clients without resolving credentials during validation.</summary>
public sealed class MistralClientFactory : IStructuredClientFactory<MistralClient, MistralOptions>
{
    /// <summary>Validates configuration without resolving credentials.</summary>
    public void Validate(MistralOptions options)
    {
        using var http = new HttpClient();
        _ = Create(http, options);
    }

    /// <summary>Creates a client that snapshots the supplied options.</summary>
    public MistralClient Create(HttpClient http, MistralOptions options) => new(http, options);
}
