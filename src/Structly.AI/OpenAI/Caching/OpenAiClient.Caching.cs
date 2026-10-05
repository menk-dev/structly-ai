using System.Text.Json;

namespace Structly.AI.OpenAI;

public sealed partial class OpenAiClient
{
    void ApplyCache(Dictionary<string, object?> payload, OpenAiResponseOptions options, string model, int breakpoints, bool prewarm)
    {
        var modern = options.Cache is not null || breakpoints != 0 || prewarm;
        if(!modern && options.CacheRetention is null)
            return;

        if(!_options.CacheCompatibility.TryGetValue(model, out var compatibility))
            throw new ArgumentException("Configure model cache compatibility.");

        if(modern)
        {
            var cache = options.Cache;
            if(compatibility != OpenAiCacheCompatibility.Modern || options.CacheRetention is not null ||
                cache?.Mode is { } mode && !Enum.IsDefined(mode) || cache?.Ttl is { } ttl && ttl != "30m" ||
                cache?.ComparisonResponseId is { } comparison && String.IsNullOrWhiteSpace(comparison) ||
                cache?.Mode == OpenAiCacheMode.Explicit && breakpoints == 0 ||
                breakpoints > (cache?.Mode == OpenAiCacheMode.Explicit ? 4 : 3))
                throw new ArgumentException("Invalid modern cache controls or breakpoint count.");

            var wire = new Dictionary<string, object?>();
            if(cache?.Mode is { } selectedMode)
                wire["mode"] = selectedMode.ToString().ToLowerInvariant();

            if(cache?.Ttl is { } selectedTtl)
                wire["ttl"] = selectedTtl;

            if(cache?.ComparisonResponseId is { } selectedComparison)
                wire["comparison_response_id"] = selectedComparison;

            if(prewarm)
                wire["prewarm"] = true;

            payload["prompt_cache_options"] = wire;
        }
        else
        {
            if(compatibility != OpenAiCacheCompatibility.Legacy || !Enum.IsDefined(options.CacheRetention!.Value))
                throw new ArgumentException("Invalid legacy retention.");

            payload["prompt_cache_retention"] = options.CacheRetention == OpenAiCacheRetention.InMemory ? "in_memory" : "24h";
        }
    }

    static CacheDiagnostics? ParseCacheDiagnostics(JsonElement root, List<StructuredWarning> warnings)
    {
        var diagnostic = Property(root, "prompt_cache_diagnostics");
        if(diagnostic.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;

        if(diagnostic.ValueKind != JsonValueKind.Object)
        {
            warnings.Add(new("InvalidCacheDiagnostics", "Provider cache diagnostics are malformed."));
            return null;
        }

        long? Count(string name)
        {
            var value = Property(diagnostic, name);
            if(value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                return null;

            if(value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var count) && count >= 0)
                return count;

            warnings.Add(new("InvalidCacheDiagnostics", "Provider cache diagnostic count is invalid."));
            return null;
        }
        return new()
        {
            Type = Text(diagnostic, "type"),
            Reason = Text(diagnostic, "reason"),
            ComparisonReusableTokens = Count("comparison_reusable_tokens"),
            CacheMissedTokens = Count("cache_missed_tokens"),
        };
    }

}
