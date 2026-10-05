namespace Structly.AI.Mistral;

/// <summary>A numbered page of batch jobs.</summary>
public sealed record MistralBatchPage
{
    /// <summary>Gets jobs in provider order.</summary>
    public required IReadOnlyList<MistralBatch> Data { get; init; }
    /// <summary>Gets the provider-reported total job count.</summary>
    public required long Total { get; init; }
}
