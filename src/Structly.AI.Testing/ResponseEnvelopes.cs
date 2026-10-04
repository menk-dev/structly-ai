using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Structly.AI.Testing;

/// <summary>Detached provider envelopes for offline tests. Supplied output is never corrected implicitly.</summary>
public static class ResponseEnvelopes
{
    /// <summary>Builds a completed response preserving text exactly, including invalid JSON.</summary>
    public static JsonElement CompletedText(string text, string model = "test-model", string responseId = "test-response", StructuredUsage? usage = null)
        => Response("completed", new { type = "output_text", text }, model, responseId, usage);

    /// <summary>Builds a completed response from detached JSON.</summary>
    public static JsonElement CompletedJson(JsonElement json, string model = "test-model", string responseId = "test-response", StructuredUsage? usage = null)
        => CompletedText(json.GetRawText(), model, responseId, usage);

    /// <summary>Builds a refusal response.</summary>
    public static JsonElement Refusal(string refusal = "refused", string model = "test-model", string responseId = "test-response", StructuredUsage? usage = null)
        => Response("completed", new { type = "refusal", refusal }, model, responseId, usage);

    /// <summary>Builds an incomplete response, preserving supplied partial text.</summary>
    public static JsonElement Incomplete(string text = "", string model = "test-model", string responseId = "test-response", StructuredUsage? usage = null)
        => Response("incomplete", new { type = "output_text", text }, model, responseId, usage);

    static JsonElement Response(string status, object content, string model, string responseId, StructuredUsage? usage)
        => JsonSerializer.SerializeToElement(new { id = responseId, model, status, output = new[] { new { type = "message", role = "assistant", status = "completed", content = new[] { content } } }, usage = Usage(usage) });

    /// <summary>Builds an embedding envelope preserving vector values and order.</summary>
    public static JsonElement Embeddings(IReadOnlyList<IReadOnlyList<float>> vectors, string model = "test-model", StructuredUsage? usage = null)
        => JsonSerializer.SerializeToElement(new { model, data = vectors.Select((embedding, index) => new { index, embedding }), usage = Usage(usage) });

    static object? Usage(StructuredUsage? usage) => usage is null ? null : new
    {
        input_tokens = usage.InputTokens,
        output_tokens = usage.OutputTokens,
        total_tokens = usage.TotalTokens,
        input_tokens_details = new { cached_tokens = usage.CachedInputTokens, cache_write_tokens = usage.CacheWriteTokens, text_tokens = usage.InputTextTokens, image_tokens = usage.InputImageTokens },
        output_tokens_details = new { reasoning_tokens = usage.ReasoningTokens, text_tokens = usage.OutputTextTokens, image_tokens = usage.OutputImageTokens }
    };

    /// <summary>Creates a fresh HTTP response; the client may dispose it independently.</summary>
    public static HttpResponseMessage ToHttpResponse(JsonElement envelope, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(envelope.GetRawText(), Encoding.UTF8, "application/json") };

    /// <summary>Explicitly fills missing object properties, preserves supplied data, then validates the completed output.</summary>
    public static JsonElement CompleteExample<T>(StructuredTask<T> task, string partialJson,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? vocabularies = null)
    {
        ArgumentNullException.ThrowIfNull(task);
        var supplied = JsonNode.Parse(partialJson);
        var example = JsonNode.Parse(task.CreateExample(vocabularies).GetRawText());
        Fill(supplied, example);
        var json = supplied?.ToJsonString() ?? "null";
        if (!task.ReadOutput(json, vocabularies).IsSuccess) throw new ArgumentException("Completed example does not satisfy the contract.", nameof(partialJson));
        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    static void Fill(JsonNode? supplied, JsonNode? example)
    {
        if (supplied is not JsonObject target || example is not JsonObject source) return;
        foreach (var property in source)
            if (!target.ContainsKey(property.Key)) target[property.Key] = property.Value?.DeepClone();
            else Fill(target[property.Key], property.Value);
    }
}
