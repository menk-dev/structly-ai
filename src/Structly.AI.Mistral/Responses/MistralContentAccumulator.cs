using System.Text.Json;
using System.Text.Json.Nodes;

namespace Structly.AI.Mistral;

// Signed reasoning blocks must be reassembled before replaying streamed history.
sealed class MistralContentAccumulator
{
    readonly List<JsonObject> _chunks = [];

    public void Add(JsonElement content)
    {
        if(content.ValueKind == JsonValueKind.String)
            Append(new() { ["type"] = "text", ["text"] = content.GetString() });
        else if(content.ValueKind == JsonValueKind.Array)
        {
            foreach(var chunk in content.EnumerateArray())
                Append(JsonNode.Parse(chunk.GetRawText()) as JsonObject ?? throw new JsonException());
        }
    }

    public JsonElement CreateAssistantMessage() => JsonSerializer.SerializeToElement(new { role = "assistant", content = _chunks });

    void Append(JsonObject chunk)
    {
        var type = chunk["type"]?.GetValue<string>();
        var previous = _chunks.LastOrDefault();
        if(previous?["type"]?.GetValue<string>() != type || type == "thinking" && previous?["closed"]?.GetValue<bool>() == true)
        {
            _chunks.Add(chunk);
            return;
        }

        if(type == "text")
            previous!["text"] = previous["text"]?.GetValue<string>() + chunk["text"]?.GetValue<string>();
        else if(type == "thinking")
        {
            if(previous!["thinking"] is not JsonArray thinking || chunk["thinking"] is not JsonArray delta)
                throw new JsonException();

            foreach(var part in delta)
                thinking.Add(part?.DeepClone());

            if(chunk.TryGetPropertyValue("signature", out var signature) && signature is not null)
                previous["signature"] = signature.DeepClone();

            if(chunk.TryGetPropertyValue("closed", out var closed) && closed is not null)
                previous["closed"] = closed.DeepClone();
        }
        else
            throw new JsonException();
    }
}
