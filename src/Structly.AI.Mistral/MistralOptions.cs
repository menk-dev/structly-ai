namespace Structly.AI.Mistral;

/// <summary>Configuration for Mistral chat completions.</summary>
public sealed class MistralOptions : IStructuredUsageOptions
{
    /// <summary>The default hosting configuration section.</summary>
    public const string SectionName = "Mistral";
    /// <summary>Gets or sets a configured API key.</summary>
    public string? ApiKey { get; set; }
    /// <summary>Gets or sets whether MISTRAL_API_KEY is read for each execution.</summary>
    public bool UseEnvironmentApiKey { get; set; }
    /// <summary>Gets or sets credentials overriding configured and environment keys.</summary>
    public Func<CancellationToken, ValueTask<string?>>? CredentialResolver { get; set; }
    /// <summary>Gets or sets the default model selection.</summary>
    public ModelSelection DefaultModel { get; set; } = new();
    /// <summary>Gets or sets named model profiles.</summary>
    public Dictionary<string, ModelSelection> Profiles { get; set; } = new(StringComparer.Ordinal);
    /// <summary>Gets or sets the absolute HTTP API directory, ending in a slash.</summary>
    public Uri BaseAddress { get; set; } = new("https://api.mistral.ai/v1/");
    /// <summary>Gets or sets the total budget, including credential resolution and response processing.</summary>
    public TimeSpan TotalTimeout { get; set; } = TimeSpan.FromSeconds(120);
    /// <summary>Gets or sets the maximum response envelope size in bytes.</summary>
    public int MaxResponseBytes { get; set; } = 16 * 1024 * 1024;
    /// <summary>Gets or sets the best-effort usage observer.</summary>
    public Func<StructuredUsageEvent, CancellationToken, ValueTask>? UsageObserver { get; set; }
}
