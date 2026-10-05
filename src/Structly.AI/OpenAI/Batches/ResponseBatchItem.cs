namespace Structly.AI.OpenAI;

/// <summary>One typed Responses request with a unique application custom ID.</summary>
public sealed record ResponseBatchItem(string CustomId, StructuredRequest Request);
