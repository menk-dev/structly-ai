namespace Structly.AI.Mistral;

/// <summary>One typed chat completion request with a unique application custom ID.</summary>
public sealed record ResponseBatchItem(string CustomId, StructuredRequest Request);
