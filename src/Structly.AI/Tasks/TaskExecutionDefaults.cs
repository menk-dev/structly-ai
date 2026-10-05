namespace Structly.AI;

/// <summary>Reusable execution settings; per-call values take precedence.</summary>
public sealed record TaskExecutionDefaults
{
    /// <summary>Gets the total execution budget (positive, at most 24 hours).</summary>
    public TimeSpan? TotalTimeout { get; init; }
    /// <summary>Gets the positive SSE inactivity budget, used only for streaming calls.</summary>
    public TimeSpan? InactivityTimeout { get; init; }
    /// <summary>Gets a positive output token cap.</summary>
    public int? MaxOutputTokens { get; init; }

    internal void Validate()
    {
        if(TotalTimeout is { } total && (total <= TimeSpan.Zero || total > TimeSpan.FromHours(24)) ||
            InactivityTimeout is { } idle && idle <= TimeSpan.Zero || MaxOutputTokens is <= 0)
            throw new ArgumentException("Invalid task execution defaults.");
    }

    internal StructuredRequest Apply(StructuredRequest request) => request with
    {
        TotalTimeout = request.TotalTimeout ?? TotalTimeout,
        InactivityTimeout = request.InactivityTimeout ?? (request.Stream ? InactivityTimeout : null),
        MaxOutputTokens = request.MaxOutputTokens ?? MaxOutputTokens,
    };
}
