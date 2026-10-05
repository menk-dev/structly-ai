using System.Text.Json;

namespace Structly.AI;

/// <summary>Identity and accounting retained even when output processing fails.</summary>
public sealed record StructuredMetadata
{
    readonly JsonElement? _rawResponse;
    /// <summary>Gets the batch identifier.</summary>
    public string? BatchId { get; init; }
    /// <summary>Gets an uploaded file retained when batch creation fails.</summary>
    public string? UploadedFileId { get; init; }
    /// <summary>Gets the caller batch item identifier.</summary>
    public string? BatchCustomId { get; init; }
    /// <summary>Gets whether this is a batch item.</summary>
    public bool IsBatch { get; init; }
    /// <summary>Gets a local execution identifier.</summary>
    public Guid ExecutionId { get; init; } = Guid.NewGuid();
    /// <summary>Gets the operation name.</summary>
    public string? Operation { get; init; }
    /// <summary>Gets the resolved schema name for typed operations.</summary>
    public string? SchemaName { get; init; }
    /// <summary>Gets the caller correlation identifier.</summary>
    public string? CorrelationId { get; init; }
    /// <summary>Gets the provider name.</summary>
    public string? Provider { get; init; }
    /// <summary>Gets the requested model.</summary>
    public string? RequestedModel { get; init; }
    /// <summary>Gets the resolved model.</summary>
    public string? ResolvedModel { get; init; }
    /// <summary>Gets the provider response identifier.</summary>
    public string? ResponseId { get; init; }
    /// <summary>Gets the provider request identifier.</summary>
    public string? ProviderRequestId { get; init; }
    /// <summary>Gets reported usage without inferred counts.</summary>
    public StructuredUsage? Usage { get; init; }
    /// <summary>Gets provider cache comparison diagnostics without inferring reuse.</summary>
    public OpenAI.CacheDiagnostics? CacheDiagnostics { get; init; }
    /// <summary>Gets output text only when explicitly captured.</summary>
    public string? OutputText { get; init; }
    /// <summary>Gets a detached raw envelope only when explicitly captured.</summary>
    public JsonElement? RawResponse
    {
        get => _rawResponse;
        init => _rawResponse = value?.Clone();
    }
}
