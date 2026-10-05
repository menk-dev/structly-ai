using Structly.AI.Embeddings;

namespace Structly.AI.Mistral;

/// <summary>One embedding request with a unique application custom ID.</summary>
public sealed record EmbeddingBatchItem(string CustomId, EmbeddingRequest Request);
