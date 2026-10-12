namespace Structly.AI.OpenAI;

/// <summary>A selected allowed value, confidence and immutable distribution. T is string, bool or an enum.</summary>
public sealed record DecisionChoice<T>
{
    /// <summary>Creates an answer with a detached, read-only distribution.</summary>
    public DecisionChoice(T choice, double confidence, IReadOnlyList<DecisionProbability<T>> probabilities)
    {
        ArgumentNullException.ThrowIfNull(probabilities);
        Choice = choice;
        Confidence = confidence;
        Probabilities = Array.AsReadOnly(probabilities.ToArray());
    }

    /// <summary>Gets the selected allowed value.</summary>
    public T Choice { get; }
    /// <summary>Gets the provider's confidence, independently of the distribution.</summary>
    public double Confidence { get; }
    /// <summary>Gets probabilities in task choice order.</summary>
    public IReadOnlyList<DecisionProbability<T>> Probabilities { get; }
}
