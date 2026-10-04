namespace Structly.AI.OpenAI;

/// <summary>Configuration snapshotted by the client; transport remains caller-owned.</summary>
public sealed record OpenAiClientOptions
{
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
    /// <summary>Gets named structured-output model profiles, each selecting an explicit ID.</summary>
    public IReadOnlyDictionary<string, ModelSelection> Profiles { get; init; } = new Dictionary<string, ModelSelection>();
    /// <summary>Gets the default credential resolver.</summary>
    public Func<CancellationToken, ValueTask<string?>>? CredentialResolver { get; init; }
    /// <summary>Gets an absolute HTTP(S) API directory URI, ending in a slash.</summary>
    public Uri BaseAddress { get; init; } = new("https://api.openai.com/v1/");
    /// <summary>Gets the maximum response envelope size in bytes.</summary>
    public int MaxResponseBytes { get; init; } = 16 * 1024 * 1024;
}

/// <summary>Explicit provider request and sensitive capture controls.</summary>
public sealed record OpenAiResponseOptions
{
    /// <summary>Gets whether the provider may store this response. Defaults to false.</summary>
    public bool Store { get; init; }
    /// <summary>Gets optional idempotency header passthrough; no deduplication guarantee.</summary>
    public string? IdempotencyKey { get; init; }
    /// <summary>Gets provider metadata (at most 16 pairs, keys 64 and values 512 characters).</summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
    /// <summary>Gets whether to retain the sensitive raw response envelope.</summary>
    public bool CaptureRawResponse { get; init; }
    /// <summary>Gets whether to retain sensitive output text.</summary>
    public bool CaptureOutputText { get; init; }
}
