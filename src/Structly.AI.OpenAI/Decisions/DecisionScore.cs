namespace Structly.AI.OpenAI;

/// <summary>A fractional, probability-weighted rubric index with confidence and an immutable distribution.</summary>
public sealed record DecisionScore
{
    /// <summary>Creates an answer with a detached, read-only distribution.</summary>
    public DecisionScore(double score, double confidence, IReadOnlyList<DecisionLevelProbability> probabilities)
    {
        ArgumentNullException.ThrowIfNull(probabilities);
        Score = score;
        Confidence = confidence;
        Probabilities = Array.AsReadOnly(probabilities.ToArray());
    }

    /// <summary>Gets the probability-weighted average of zero-based rubric indices.</summary>
    public double Score { get; }
    /// <summary>Gets the provider's confidence, independently of the distribution.</summary>
    public double Confidence { get; }
    /// <summary>Gets probabilities in task rubric order.</summary>
    public IReadOnlyList<DecisionLevelProbability> Probabilities { get; }
}
