namespace Structly.AI.Mistral;

/// <summary>Provider batch state and available result files.</summary>
public sealed record MistralBatch
{
    /// <summary>Gets the job identifier.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the provider status.</summary>
    public required string Status { get; init; }
    /// <summary>Gets the submitted file identifiers.</summary>
    public required IReadOnlyList<string> InputFileIds { get; init; }
    /// <summary>Gets the inference endpoint.</summary>
    public required string Endpoint { get; init; }
    /// <summary>Gets the selected model.</summary>
    public string? Model { get; init; }
    /// <summary>Gets the output file identifier.</summary>
    public string? OutputFileId { get; init; }
    /// <summary>Gets the error file identifier.</summary>
    public string? ErrorFileId { get; init; }
    /// <summary>Gets whether the job has finished, including partial cancellation results.</summary>
    public bool IsTerminal => Status is "SUCCESS" or "FAILED" or "TIMEOUT_EXCEEDED" or "CANCELLED";
}
