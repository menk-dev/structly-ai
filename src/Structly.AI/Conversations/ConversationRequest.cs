namespace Structly.AI;

/// <summary>Input and execution controls for a conversation turn.</summary>
public sealed record ConversationRequest
{
    /// <summary>Gets the total execution budget, overriding the client default (positive, at most 24 hours).</summary>
    public TimeSpan? TotalTimeout { get; init; }
    /// <summary>Gets the SSE inactivity budget; valid only with streaming.</summary>
    public TimeSpan? InactivityTimeout { get; init; }
    /// <summary>Gets whether to use SSE, independently of progress observation.</summary>
    public bool Stream { get; init; }
    /// <summary>Gets an ordered, best-effort streaming progress callback.</summary>
    public Func<StructuredProgress, CancellationToken, ValueTask>? Progress { get; init; }
    /// <summary>Gets a best-effort accounting callback overriding the client observer.</summary>
    public Func<StructuredUsageEvent, CancellationToken, ValueTask>? UsageObserver { get; init; }
    /// <summary>Gets nonblank text input, mutually exclusive with Messages.</summary>
    public string? Input { get; init; }
    /// <summary>Gets ordered messages, mutually exclusive with Input.</summary>
    public IReadOnlyList<StructuredMessage>? Messages { get; init; }
    /// <summary>Gets credentials overriding task and client without fallback.</summary>
    public Func<CancellationToken, ValueTask<string?>>? CredentialResolver { get; init; }
    /// <summary>Gets a positive output token cap.</summary>
    public int? MaxOutputTokens { get; init; }
    /// <summary>Gets a local correlation identifier.</summary>
    public string? CorrelationId { get; init; }
}
