namespace Structly.AI.OpenAI;

/// <summary>The provider's probability for an allowed choice.</summary>
public sealed record DecisionProbability<T>(T Value, double Probability);
