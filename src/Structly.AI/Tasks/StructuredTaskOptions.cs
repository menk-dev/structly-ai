namespace Structly.AI;

/// <summary>Startup settings for a reusable typed output contract.</summary>
public sealed record StructuredTaskOptions
{
    /// <summary>Gets optional task instructions; explicit blank values are invalid.</summary>
    public string? Instructions { get; init; }
    /// <summary>Gets vocabulary ordering.</summary>
    public VocabularyOrder VocabularyOrder { get; init; }
    /// <summary>Gets an optional explicit schema name.</summary>
    public string? SchemaName { get; init; }
    /// <summary>Gets an optional schema description overriding type metadata.</summary>
    public string? Description { get; init; }
    /// <summary>Gets supported immutable serialization settings.</summary>
    public SerializationProfile SerializationProfile { get; init; } = new();
    /// <summary>Gets reusable execution defaults, overridden by per-call settings.</summary>
    public TaskExecutionDefaults ExecutionDefaults { get; init; } = new();
    /// <summary>Gets task-specific model selection.</summary>
    public ModelSelection? ModelSelection { get; init; }
    /// <summary>Gets task-specific credentials.</summary>
    public Func<CancellationToken, ValueTask<string?>>? CredentialResolver { get; init; }
}
