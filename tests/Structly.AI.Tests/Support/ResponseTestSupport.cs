using Structly.AI.OpenAI;
using System.ComponentModel;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Structly.AI.Tests;

static class ResponseTestSupport
{
    internal static StructuredTask<T> Contract<T>() => StructuredTask.Create<T>(new() { Instructions = "Current instructions" });
    internal static OpenAiClient Client(HttpClient http, bool modern = true) => new(http, new()
    {
        DefaultModel = new() { ModelId = "response-model" },
        CredentialResolver = Credentials.FromStatic("offline"),
        Profiles = new Dictionary<string, ModelSelection> { ["fast"] = new() { ModelId = "response-model", ReasoningEffort = ReasoningEffort.Low } },
        CacheCompatibility = new Dictionary<string, OpenAiCacheCompatibility> { ["response-model"] = modern ? OpenAiCacheCompatibility.Modern : OpenAiCacheCompatibility.Legacy },
    });
    internal static string Envelope(string text = "Hello", string status = "completed", bool refusal = false, bool prewarm = false) => JsonSerializer.Serialize(new
    {
        id = "current",
        model = "resolved",
        status,
        output = prewarm ? [] : new object[] { new { type = "message", role = "assistant", status = "completed", content = refusal
            ? new object[] { new { type = "refusal", refusal = "private" } } : [new { type = "output_text", text }], }, },
        usage = new { input_tokens = 100, output_tokens = 3, input_tokens_details = new { cached_tokens = 40, cache_write_tokens = 10 } },
        prompt_cache_diagnostics = new { type = "future_type", reason = "future_reason", comparison_reusable_tokens = 50, cache_missed_tokens = 10 },
    });
    internal static HttpResponseMessage Response(string body, bool stream = false) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(stream ? "event: response.completed\ndata: " + JsonSerializer.Serialize(new { type = "response.completed", response = JsonSerializer.Deserialize<JsonElement>(body) }) + "\n\n" : body,
        Encoding.UTF8, stream ? "text/event-stream" : "application/json"),
    };
    internal static IReadOnlyDictionary<string, IReadOnlyList<string>> Vocabulary(params string[] values) => new Dictionary<string, IReadOnlyList<string>> { ["choices"] = values };

    public sealed record Guided
    {
        [JsonPropertyName("renamed"), Description("Explain this field"), StringConstraint(MinLength = 3, MaxLength = 4)]
        public required string Label { get; init; }
        [DynamicVocabulary("choices")]
        public required string Choice { get; init; }
        [CollectionConstraint(MinItems = 2, MaxItems = 3)]
        public required List<string?> Items { get; init; }
        [NumberConstraint(Minimum = -5, Maximum = -2)]
        public int Number { get; init; }
        public string? Optional { get; init; }
    }
    public sealed record Patterned
    {
        [StringConstraint(Pattern = "^[A-Z]{2}$")]
        public required string Code { get; init; }
    }
    public sealed record LargeSample
    {
        [CollectionConstraint(MinItems = 1000)]
        public required List<LargeSampleItem> Values { get; init; }
    }
    public sealed record LargeSampleItem
    {
        [CollectionConstraint(MinItems = 1000)]
        public required List<int> Values { get; init; }
    }
    public sealed record NullableCollections
    {
        [DynamicVocabulary("choices"), CollectionConstraint(MinItems = 1)]
        public required List<string?> Values { get; init; }
        public required List<Decision?> Enums { get; init; }
    }
    public enum Decision
    {
        [JsonStringEnumMemberName("yes")]
        Yes, No,
    }
    internal sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        int _calls;
        public int Calls => Volatile.Read(ref _calls);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return send(request);
        }
    }

}
