namespace Structly.AI;

/// <summary>Immutable resolved conversation configuration.</summary>
public sealed record ConversationConfiguration
{
    /// <summary>Gets required nonblank instructions.</summary>
    public required string Instructions { get; init; }
    /// <summary>Gets the resolved explicit model and reasoning selection.</summary>
    public required ModelSelection ModelSelection { get; init; }
    /// <summary>Gets whether turns request reasoning summaries; turns must stream.</summary>
    public bool IncludeReasoningSummary { get; init; }
}
