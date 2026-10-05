using Structly.AI.OpenAI;
using Structly.AI.Testing;
using System.Net;
using System.Text.Json;

namespace Structly.AI.Tests;

static class BatchTestSupport
{
    internal static OpenAiClient Client(HttpClient http, Func<StructuredUsageEvent, CancellationToken, ValueTask>? observer = null) => new(http, new() { DefaultModel = new() { ModelId = "gpt-6-sol" }, CredentialResolver = Credentials.FromStatic("key"), UsageObserver = observer });
    internal static EmbeddingBatchItem Item(string id) => new(id, new() { Inputs = ["input"], ModelSelection = new() { ModelId = "embedding" }, Dimensions = 2 });
    internal static HttpResponseMessage Response(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value)) };
    internal static object Job(string status = "completed", string endpoint = "/v1/embeddings") => new { id = "batch", status, endpoint, input_file_id = "input", output_file_id = "output", error_file_id = "errors" };
    internal static string Line(string id, JsonElement body) => JsonSerializer.Serialize(new { custom_id = id, response = new { status_code = 200, body } }) + "\n";
    internal static JsonElement Vectors() => ResponseEnvelopes.Embeddings([new float[] { 1, 2 }], "embedding", new() { InputTokens = 7 });

    public sealed class NestedAnswer
    {
        public required Answer Child { get; init; }
        public required string[] Items { get; init; }
        public string? Optional { get; init; }
    }
    public sealed class PatternAnswer
    {
        [StringConstraint(Pattern = "^a+$")]
        public required string Value { get; init; }
    }
    internal sealed class TrackedContent : StringContent
    {
        public TrackedContent() : base("{}") { }
        public TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if(disposing)
                Disposed.TrySetResult();
        }
    }

    public sealed class Answer { public required string Value { get; init; } }
    public sealed class OtherAnswer { public required int Count { get; init; } }
    internal sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(send(request));
        }
    }
    internal sealed class AsyncHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }

}
