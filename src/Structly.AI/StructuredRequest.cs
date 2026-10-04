namespace Structly.AI;

/// <summary>Explicit model or named profile selection.</summary>
public sealed record ModelSelection
{
    /// <summary>Gets an explicit provider model ID.</summary>
    public string? ModelId { get; init; }
    /// <summary>Gets a consumer-configured profile name.</summary>
    public string? ProfileName { get; init; }
    /// <summary>Gets optional reasoning effort.</summary>
    public ReasoningEffort? ReasoningEffort { get; init; }

    internal void Validate()
    {
        if ((ModelId is null) == (ProfileName is null) ||
            ModelId is not null && String.IsNullOrWhiteSpace(ModelId) ||
            ProfileName is not null && String.IsNullOrWhiteSpace(ProfileName) ||
            ReasoningEffort is { } effort && !Enum.IsDefined(effort))
            throw new ArgumentException("Select exactly one nonblank model ID or profile and a supported effort.");
    }
}

/// <summary>Provider reasoning effort; model support is provider-specific.</summary>
public enum ReasoningEffort
{
    /// <summary>Low effort.</summary>
    Low,
    /// <summary>Medium effort.</summary>
    Medium,
    /// <summary>High effort.</summary>
    High
}

/// <summary>Explicit credential factories; no ambient fallback.</summary>
public static class Credentials
{
    /// <summary>Uses a fixed credential.</summary>
    public static Func<CancellationToken, ValueTask<string?>> FromStatic(string credential)
    {
        if (String.IsNullOrWhiteSpace(credential)) throw new ArgumentException("Credential must be nonblank.", nameof(credential));
        return _ => ValueTask.FromResult<string?>(credential);
    }

    /// <summary>Reads the named environment variable on every execution.</summary>
    public static Func<CancellationToken, ValueTask<string?>> FromEnvironment(string variable)
    {
        if (String.IsNullOrWhiteSpace(variable)) throw new ArgumentException("Variable must be nonblank.", nameof(variable));
        return _ => ValueTask.FromResult(Environment.GetEnvironmentVariable(variable));
    }
}

/// <summary>Per-call settings for typed provider execution.</summary>
public sealed record StructuredRequest
{
    /// <summary>Gets the total execution budget, overriding the client default (positive, at most 24 hours).</summary>
    public TimeSpan? TotalTimeout { get; init; }
    /// <summary>Gets the SSE inactivity budget; valid only with streaming.</summary>
    public TimeSpan? InactivityTimeout { get; init; }
    /// <summary>Gets whether to use SSE, independently of progress observation.</summary>
    public bool Stream { get; init; }
    /// <summary>Gets whether to request and expose reasoning summaries; requires streaming.</summary>
    public bool IncludeReasoningSummary { get; init; }
    /// <summary>Gets an ordered, best-effort streaming progress callback.</summary>
    public Func<StructuredProgress, CancellationToken, ValueTask>? Progress { get; init; }
    /// <summary>Gets a best-effort accounting callback overriding the client observer.</summary>
    public Func<StructuredUsageEvent, CancellationToken, ValueTask>? UsageObserver { get; init; }
    /// <summary>Gets nonblank text input, mutually exclusive with Messages.</summary>
    public string? Input { get; init; }
    /// <summary>Gets ordered messages, mutually exclusive with Input.</summary>
    public IReadOnlyList<StructuredMessage>? Messages { get; init; }
    /// <summary>Gets optional contract-derived output guidance.</summary>
    public OutputSpecificationOptions? OutputSpecification { get; init; }
    /// <summary>Gets runtime vocabularies, snapshotted at execution entry.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>>? Vocabularies { get; init; }
    /// <summary>Gets model selection overriding task and client.</summary>
    public ModelSelection? ModelSelection { get; init; }
    /// <summary>Gets credentials overriding task and client without fallback.</summary>
    public Func<CancellationToken, ValueTask<string?>>? CredentialResolver { get; init; }
    /// <summary>Gets a positive output token cap.</summary>
    public int? MaxOutputTokens { get; init; }
    /// <summary>Gets a local correlation identifier.</summary>
    public string? CorrelationId { get; init; }
    readonly OpenAI.OpenAiResponseOptions _openAi = new();
    /// <summary>Gets provider-specific settings. Assigning null uses default settings.</summary>
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public OpenAI.OpenAiResponseOptions OpenAi { get => _openAi; init => _openAi = value ?? new(); }
    /// <summary>Gets optional nonblank instructions replacing task instructions for this call.</summary>
    public string? Instructions { get; init; }
}
