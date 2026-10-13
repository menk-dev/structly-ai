namespace Structly.AI.OpenAI;

/// <summary>Evidence and controls for one decision execution. Instructions and choices belong to the task.</summary>
public sealed record OpenAiDecisionRequest
{
    /// <summary>Gets text evidence. Supply exactly one of Input or Messages.</summary>
    public string? Input { get; init; }
    /// <summary>Gets user messages containing text and inline base64 images, snapshotted before credentials.</summary>
    public IReadOnlyList<StructuredMessage>? Messages { get; init; }
    /// <summary>Gets an explicit model or decision profile, overriding DefaultDecisionModel.</summary>
    public ModelSelection? ModelSelection { get; init; }
    /// <summary>Gets the total timeout, overriding the client budget.</summary>
    public TimeSpan? TotalTimeout { get; init; }
    /// <summary>Gets per-call credentials, overriding the client resolver.</summary>
    public Func<CancellationToken, ValueTask<string?>>? CredentialResolver { get; init; }
    /// <summary>Gets the best-effort usage observer, overriding the client observer.</summary>
    public Func<StructuredUsageEvent, CancellationToken, ValueTask>? UsageObserver { get; init; }
    /// <summary>Gets a local correlation identifier, never sent to the provider.</summary>
    public string? CorrelationId { get; init; }
    /// <summary>Gets an opaque end-user identifier sent to the provider, at most 128 characters.</summary>
    public string? SafetyIdentifier { get; init; }
    /// <summary>Gets whether to retain the potentially sensitive raw response.</summary>
    public bool CaptureRawResponse { get; init; }
}
