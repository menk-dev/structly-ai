using System.Collections.Frozen;

namespace Structly.AI.OpenAI;

static class OpenAiCacheDefaults
{
    public static FrozenDictionary<string, OpenAiCacheCompatibility> Models { get; } = new Dictionary<string, OpenAiCacheCompatibility>(StringComparer.Ordinal)
    {
        ["gpt-6-luna"] = OpenAiCacheCompatibility.Modern,
        ["gpt-6-sol"] = OpenAiCacheCompatibility.Modern,
        ["gpt-6-astra"] = OpenAiCacheCompatibility.Modern,
    }.ToFrozenDictionary(StringComparer.Ordinal);
}
