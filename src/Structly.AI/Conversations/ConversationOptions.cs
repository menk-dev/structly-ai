namespace Structly.AI;

/// <summary>Configuration overrides resolved when creating a conversation.</summary>
public sealed record ConversationOptions
{
    /// <summary>Gets instructions overriding the task.</summary>
    public string? Instructions { get; init; }
    /// <summary>Gets model selection overriding task and client defaults.</summary>
    public ModelSelection? ModelSelection { get; init; }
    /// <summary>Gets whether turns request reasoning summaries in metadata and streaming progress.</summary>
    public bool IncludeReasoningSummary { get; init; }
}
