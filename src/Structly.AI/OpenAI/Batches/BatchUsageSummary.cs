namespace Structly.AI.OpenAI;

/// <summary>Reported token sums and coverage for one accounting model.</summary>
public sealed record BatchUsageSummary
{
    /// <summary>Gets the resolved model, falling back to the requested model.</summary>
    public required string Model { get; init; }
    /// <summary>Gets the number of items, including failures and missing results.</summary>
    public int ItemCount { get; init; }
    /// <summary>Gets the number of items with a reported usage object.</summary>
    public int ItemsWithUsage { get; init; }
    /// <summary>Gets sums of reported counts; unreported or overflowing fields remain null.</summary>
    public required StructuredUsage ReportedUsage { get; init; }
    /// <summary>Gets the number of items reporting each field.</summary>
    public required BatchUsageCoverage Coverage { get; init; }
}
