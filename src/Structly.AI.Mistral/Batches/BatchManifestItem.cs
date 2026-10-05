namespace Structly.AI.Mistral;

/// <summary>Persisted item identity, contract and capture settings; contains no credentials or delegates.</summary>
public sealed record BatchManifestItem
{
    /// <summary>Gets the application custom ID used to correlate provider results.</summary>
    public required string CustomId { get; init; }
    /// <summary>Gets the original zero-based input position.</summary>
    public required int Position { get; init; }
    /// <summary>Gets the execution ID generated once during preparation, reused on import.</summary>
    public required Guid ExecutionId { get; init; }
    /// <summary>Gets the application correlation ID.</summary>
    public string? CorrelationId { get; init; }
    /// <summary>Gets the resolved requested model ID used in the submitted payload.</summary>
    public required string RequestedModel { get; init; }
    /// <summary>Gets the embedding input count.</summary>
    public int InputCount { get; init; }
    /// <summary>Gets the requested embedding dimensions when specified.</summary>
    public int? Dimensions { get; init; }
    /// <summary>Gets whether import retains the sensitive provider envelope.</summary>
    public bool CaptureRawResponse { get; init; }
    /// <summary>Gets whether import retains sensitive output text.</summary>
    public bool CaptureOutputText { get; init; }
    /// <summary>Gets application-owned vocabulary snapshots used to regenerate the schema.</summary>
    public Dictionary<string, string[]>? Vocabularies { get; init; }
    /// <summary>Gets the SHA-256 fingerprint of the emitted typed schema.</summary>
    public string? SchemaFingerprint { get; init; }
}
