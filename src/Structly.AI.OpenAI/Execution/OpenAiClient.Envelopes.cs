using System.Text.Json;

namespace Structly.AI.OpenAI;

public sealed partial class OpenAiClient
{
    static JsonElement Property(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value : default;
    static string? Text(JsonElement element, string name) => Property(element, name) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

    static StructuredUsage? ParseUsage(JsonElement root, List<StructuredWarning> warnings)
    {
        var usage = Property(root, "usage");
        if(usage.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return null;

        if(usage.ValueKind != JsonValueKind.Object)
        {
            warnings.Add(new("InvalidUsage", "Provider usage metadata is malformed."));
            return null;
        }

        long? Count(JsonElement element, string name)
        {
            var value = Property(element, name);
            if(value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
                return null;

            if(value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) && number >= 0)
                return number;

            warnings.Add(new("InvalidUsage", "A provider usage count is invalid."));
            return null;
        }
        var input = Property(usage, "input_tokens_details");
        var output = Property(usage, "output_tokens_details");
        if(input.ValueKind is not (JsonValueKind.Object or JsonValueKind.Null or JsonValueKind.Undefined) ||
            output.ValueKind is not (JsonValueKind.Object or JsonValueKind.Null or JsonValueKind.Undefined))
            warnings.Add(new("InvalidUsage", "Provider usage details are malformed."));

        var result = new StructuredUsage
        {
            InputTokens = Count(usage, "input_tokens") ?? Count(usage, "prompt_tokens"),
            OutputTokens = Count(usage, "output_tokens"),
            TotalTokens = Count(usage, "total_tokens"),
            CachedInputTokens = Count(input, "cached_tokens"),
            CacheWriteTokens = Count(input, "cache_write_tokens"),
            ReasoningTokens = Count(output, "reasoning_tokens"),
            InputTextTokens = Count(input, "text_tokens"),
            InputImageTokens = Count(input, "image_tokens"),
            OutputImageTokens = Count(output, "image_tokens"),
            OutputTextTokens = Count(output, "text_tokens"),
        };
        return result.InputTokens is not null || result.OutputTokens is not null || result.TotalTokens is not null ||
            result.CachedInputTokens is not null || result.CacheWriteTokens is not null || result.ReasoningTokens is not null || result.InputTextTokens is not null || result.InputImageTokens is not null || result.OutputImageTokens is not null || result.OutputTextTokens is not null ? result : null;
    }

}
