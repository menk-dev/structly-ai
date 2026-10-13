namespace Structly.AI.OpenAI;

/// <summary>The provider's probability for a zero-based rubric level.</summary>
public sealed record DecisionLevelProbability
{
    /// <summary>Creates a probability for a rubric level.</summary>
    public DecisionLevelProbability(int value, string label, double probability)
    {
        Value = value;
        Label = label;
        Probability = probability;
    }

    /// <summary>Gets the zero-based level index.</summary>
    public int Value { get; }
    /// <summary>Gets the level label.</summary>
    public string Label { get; }
    /// <summary>Gets the estimated probability.</summary>
    public double Probability { get; }
}
