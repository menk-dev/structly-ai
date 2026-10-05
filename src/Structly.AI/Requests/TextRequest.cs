namespace Structly.AI;

/// <summary>Free text generation, with the same response execution controls.</summary>
public sealed record TextRequest
{
    /// <summary>Gets response input and execution controls. Vocabularies and output specifications are unsupported.</summary>
    public required StructuredRequest Request { get; init; }
    /// <summary>Gets optional nonblank instructions, resent on continuation.</summary>
    public string? Instructions { get; init; }
}
