namespace Structly.AI;

/// <summary>Safe progress kinds; reasoning text is never exposed.</summary>
public enum StructuredProgressKind
{
    /// <summary>Execution started.</summary>
    Started,
    /// <summary>An output text fragment arrived.</summary>
    OutputTextDelta,
    /// <summary>An explicitly requested reasoning summary fragment arrived.</summary>
    ReasoningSummaryDelta,
    /// <summary>Terminal output validation finished.</summary>
    Completed,
}
