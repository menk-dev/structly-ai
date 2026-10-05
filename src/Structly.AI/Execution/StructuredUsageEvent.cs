namespace Structly.AI;

/// <summary>One best-effort usage notification, with outcome before observer execution.</summary>
public sealed record StructuredUsageEvent
{
    /// <summary>Gets the provider.</summary>
    public required string Provider { get; init; }
    /// <summary>Gets the requested model, the accounting fallback when resolved model is absent.</summary>
    public required string RequestedModel { get; init; }
    /// <summary>Gets reported usage; unknown counts remain null.</summary>
    public required StructuredUsage Usage { get; init; }
    /// <summary>Gets the resolved model when reported.</summary>
    public string? ResolvedModel => Metadata.ResolvedModel;
    /// <summary>Gets accounting metadata; use ExecutionId to deduplicate against results.</summary>
    public required StructuredMetadata Metadata { get; init; }
    /// <summary>Gets whether output processing succeeded.</summary>
    public bool Succeeded { get; init; }
    /// <summary>Gets the failure kind, if any.</summary>
    public StructuredErrorKind? FailureKind { get; init; }
    /// <summary>Gets whether the caller had cancelled before notification.</summary>
    public bool CallerCancelled { get; init; }
}
