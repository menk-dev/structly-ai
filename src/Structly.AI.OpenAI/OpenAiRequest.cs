namespace Structly.AI.OpenAI;

/// <summary>Structured execution settings with OpenAI response controls.</summary>
public sealed record OpenAiRequest : StructuredRequest
{
    readonly OpenAiResponseOptions _openAi = new();
    /// <summary>Gets provider settings; null selects defaults.</summary>
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public OpenAiResponseOptions OpenAi { get => _openAi; init => _openAi = value ?? new(); }
}
