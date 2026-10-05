namespace Structly.AI.OpenAI;

/// <summary>Provider batch state and available result file identifiers.</summary>
public sealed record OpenAiBatch
{
    /// <summary>Gets the provider resource identifier.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the provider batch status.</summary>
    public required string Status { get; init; }
    /// <summary>Gets the submitted batch file ID.</summary>
    public required string InputFileId { get; init; }
    /// <summary>Gets the batch endpoint, /v1/embeddings or /v1/responses.</summary>
    public required string Endpoint { get; init; }
    /// <summary>Gets the available output file ID, if any.</summary>
    public string? OutputFileId { get; init; }
    /// <summary>Gets the available error file ID, if any.</summary>
    public string? ErrorFileId { get; init; }
    /// <summary>Gets whether status is completed, failed, expired or cancelled.</summary>
    public bool IsTerminal => Status is "completed" or "failed" or "expired" or "cancelled";
}
