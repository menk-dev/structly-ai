namespace Structly.AI.Mistral;

/// <summary>Execution settings with Mistral chat completion controls.</summary>
public sealed record MistralRequest : StructuredRequest
{
    /// <summary>Gets a prompt cache routing key without a reuse guarantee.</summary>
    public string? PromptCacheKey { get; init; }
    /// <summary>Gets optional provider metadata.</summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
    /// <summary>Gets whether to retain the sensitive response envelope; unsupported with streaming.</summary>
    public bool CaptureRawResponse { get; init; }
    /// <summary>Gets whether to retain final output text.</summary>
    public bool CaptureOutputText { get; init; }
}
