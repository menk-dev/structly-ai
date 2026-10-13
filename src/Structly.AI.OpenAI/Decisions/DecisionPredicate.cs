namespace Structly.AI.OpenAI;

/// <summary>The estimated probability that a question's condition is true. Applications choose their thresholds.</summary>
public sealed record DecisionPredicate(double Probability);
