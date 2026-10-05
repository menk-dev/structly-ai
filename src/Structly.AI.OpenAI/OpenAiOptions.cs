namespace Structly.AI.OpenAI;

/// <summary>Bindable settings for a hosted OpenAI client. Runtime callbacks can be configured in code.</summary>
public sealed class OpenAiOptions : IStructuredUsageOptions
{
    /// <summary>The default configuration section.</summary>
    public const string SectionName = "OpenAI";
    /// <summary>Gets or sets the API key supplied by configuration. A custom resolver takes precedence.</summary>
    public string? ApiKey { get; set; }
    /// <summary>Gets whether OPENAI_API_KEY is read per execution when no configured credentials exist.</summary>
    public bool UseEnvironmentApiKey { get; set; }
    /// <summary>Gets the total execution budget (positive, at most 24 hours).</summary>
    public TimeSpan TotalTimeout { get; set; } = TimeSpan.FromSeconds(120);
    /// <summary>Gets the default SSE inactivity budget, applied only to streaming requests.</summary>
    public TimeSpan? InactivityTimeout { get; set; }
    /// <summary>Gets the default best-effort usage observer.</summary>
    public Func<StructuredUsageEvent, CancellationToken, ValueTask>? UsageObserver { get; set; }
    /// <summary>Gets the clock used for monotonic budgets and provider retry dates.</summary>
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;
    /// <summary>Gets required default model selection.</summary>
    public ModelSelection DefaultModel { get; set; } = new();
    /// <summary>Gets named response model profiles, each selecting an explicit ID.</summary>
    public Dictionary<string, ModelSelection> Profiles { get; set; } = new Dictionary<string, ModelSelection>();
    /// <summary>Gets named embedding model profiles, selecting explicit IDs without reasoning.</summary>
    public Dictionary<string, ModelSelection> EmbeddingProfiles { get; set; } = new Dictionary<string, ModelSelection>();
    /// <summary>Gets named image model profiles, selecting explicit IDs without reasoning.</summary>
    public Dictionary<string, ModelSelection> ImageProfiles { get; set; } = new Dictionary<string, ModelSelection>();
    /// <summary>Gets cache compatibility by exact resolved model ID; unknown models reject advanced cache controls.</summary>
    public Dictionary<string, OpenAiCacheCompatibility> CacheCompatibility { get; set; } = new Dictionary<string, OpenAiCacheCompatibility>();
    /// <summary>Gets the default credential resolver.</summary>
    public Func<CancellationToken, ValueTask<string?>>? CredentialResolver { get; set; }
    /// <summary>Gets an absolute HTTP(S) API directory URI, ending in a slash.</summary>
    public Uri BaseAddress { get; set; } = new("https://api.openai.com/v1/");
    /// <summary>Gets the maximum response/text/embedding envelope size in bytes.</summary>
    public int MaxResponseBytes { get; set; } = 16 * 1024 * 1024;
    /// <summary>Gets the maximum image envelope size in bytes, including base64 data.</summary>
    public int MaxImageResponseBytes { get; set; } = 128 * 1024 * 1024;
    internal OpenAiClientOptions ToClientOptions(Func<StructuredUsageEvent, CancellationToken, ValueTask>? usageObserver = null) => new()
    {
        DefaultModel = DefaultModel,
        Profiles = Profiles,
        EmbeddingProfiles = EmbeddingProfiles,
        ImageProfiles = ImageProfiles,
        CacheCompatibility = CacheCompatibility,
        BaseAddress = BaseAddress,
        TotalTimeout = TotalTimeout,
        InactivityTimeout = InactivityTimeout,
        MaxResponseBytes = MaxResponseBytes,
        MaxImageResponseBytes = MaxImageResponseBytes,
        TimeProvider = TimeProvider,
        UsageObserver = usageObserver ?? UsageObserver,
        CredentialResolver = CredentialResolver ?? (String.IsNullOrWhiteSpace(ApiKey) ? UseEnvironmentApiKey ? Credentials.FromEnvironment("OPENAI_API_KEY") : null : Credentials.FromStatic(ApiKey)),
    };
}
