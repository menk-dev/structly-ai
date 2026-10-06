namespace Structly.AI.OpenAI;

/// <summary>Configuration snapshotted by the client; transport remains caller-owned.</summary>
public sealed record OpenAiClientOptions
{
    /// <summary>Gets whether ordinary responses are stored at OpenAI by default. Defaults to true; conversations always store responses.</summary>
    public bool Store { get; init; } = true;
    /// <summary>Gets the total execution budget (positive, at most 24 hours).</summary>
    public TimeSpan TotalTimeout { get; init; } = TimeSpan.FromSeconds(120);
    /// <summary>Gets the default SSE inactivity budget, applied only to streaming requests.</summary>
    public TimeSpan? InactivityTimeout { get; init; }
    /// <summary>Gets the default best-effort usage observer.</summary>
    public Func<StructuredUsageEvent, CancellationToken, ValueTask>? UsageObserver { get; init; }
    /// <summary>Gets the clock used for monotonic budgets and provider retry dates.</summary>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;
    /// <summary>Gets required default model selection.</summary>
    public required ModelSelection DefaultModel { get; init; }
    /// <summary>Gets named response model profiles, each selecting an explicit ID.</summary>
    public IReadOnlyDictionary<string, ModelSelection> Profiles { get; init; } = new Dictionary<string, ModelSelection>();
    /// <summary>Gets named embedding model profiles, selecting explicit IDs without reasoning.</summary>
    public IReadOnlyDictionary<string, ModelSelection> EmbeddingProfiles { get; init; } = new Dictionary<string, ModelSelection>();
    /// <summary>Gets named image model profiles, selecting explicit IDs without reasoning.</summary>
    public IReadOnlyDictionary<string, ModelSelection> ImageProfiles { get; init; } = new Dictionary<string, ModelSelection>();
    /// <summary>Gets exact model cache overrides; GPT versions 6 and later default to modern controls, other models require configuration.</summary>
    public IReadOnlyDictionary<string, OpenAiCacheCompatibility> CacheCompatibility { get; init; } = new Dictionary<string, OpenAiCacheCompatibility>();
    /// <summary>Gets the default credential resolver.</summary>
    public Func<CancellationToken, ValueTask<string?>>? CredentialResolver { get; init; }
    /// <summary>Gets an absolute HTTP(S) API directory URI, ending in a slash.</summary>
    public Uri BaseAddress { get; init; } = new("https://api.openai.com/v1/");
    /// <summary>Gets the maximum response/text/embedding envelope size in bytes.</summary>
    public int MaxResponseBytes { get; init; } = 16 * 1024 * 1024;
    /// <summary>Gets the maximum image envelope size in bytes, including base64 data.</summary>
    public int MaxImageResponseBytes { get; init; } = 128 * 1024 * 1024;
}
