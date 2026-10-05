namespace Structly.AI.OpenAI;

/// <summary>One provider batch listing page.</summary>
public sealed record OpenAiBatchPage
{
    /// <summary>Gets the batches in this page.</summary>
    public required IReadOnlyList<OpenAiBatch> Data { get; init; }
    /// <summary>Gets whether another page is available.</summary>
    public bool HasMore { get; init; }
    /// <summary>Gets the cursor for a subsequent listing.</summary>
    public string? LastId { get; init; }
}
