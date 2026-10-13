namespace Structly.AI.OpenAI;

/// <summary>An answer associated with an application-supplied question identifier.</summary>
public sealed record DecisionEntry<TAnswer>(string Key, TAnswer Answer);
