namespace Structly.AI.Mistral;

/// <summary>Transport controls shared by batch and file operations.</summary>
public sealed record BatchOperationOptions
{
    /// <summary>Gets operation credentials overriding the client without fallback.</summary>
    public Func<CancellationToken, ValueTask<string?>>? CredentialResolver { get; init; }
    /// <summary>Gets a total operation budget overriding the client default.</summary>
    public TimeSpan? TotalTimeout { get; init; }
    /// <summary>Gets the application correlation ID.</summary>
    public string? CorrelationId { get; init; }
    /// <summary>Gets the import observer overriding the client observer.</summary>
    public Func<StructuredUsageEvent, CancellationToken, ValueTask>? UsageObserver { get; init; }
    /// <summary>Gets the aggregate import download ceiling; defaults to 256 MiB.</summary>
    public long MaxDownloadBytes { get; init; } = 256L * 1024 * 1024;
}
