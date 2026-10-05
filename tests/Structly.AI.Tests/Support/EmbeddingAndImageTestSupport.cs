using Structly.AI.Embeddings;
using Structly.AI.Imaging;
using Structly.AI.OpenAI;
using System.Net;
using System.Text.Json;

namespace Structly.AI.Tests;

static class EmbeddingAndImageTestSupport
{
    internal static ModelSelection Model => new() { ProfileName = "balanced" };
    internal static OpenAiClient Client(HttpClient http, TimeProvider? clock = null) => new(http, new()
    {
        DefaultModel = new() { ModelId = "response" },
        CredentialResolver = Credentials.FromStatic("client-key"),
        EmbeddingProfiles = new Dictionary<string, ModelSelection> { ["balanced"] = new() { ModelId = "embedding" } },
        ImageProfiles = new Dictionary<string, ModelSelection> { ["balanced"] = new() { ModelId = "image" } },
        TimeProvider = clock ?? TimeProvider.System,
    });
    internal static EmbeddingRequest Embedding() => new() { Inputs = ["first", "second"], ModelSelection = Model, Dimensions = 2 };
    internal static ImageGenerationRequest Image() => new() { Prompt = "draw a house", ModelSelection = Model };
    internal static string EmbeddingEnvelope() => """
        {"model":"embedding-resolved","data":[{"index":1,"embedding":[3,4]},{"index":0,"embedding":[1,2]}],"usage":{"prompt_tokens":7,"total_tokens":7}}
        """;
    internal static string ImageEnvelope(string format = "png", int count = 1) => JsonSerializer.Serialize(new
    {
        output_format = format,
        data = Enumerable.Range(0, count).Select(_ => new { b64_json = Base64(format) }),
        usage = new
        {
            input_tokens = 10,
            output_tokens = 20,
            total_tokens = 30,
            input_tokens_details = new { text_tokens = 6, image_tokens = 4 },
            output_tokens_details = new { image_tokens = 18, text_tokens = 2 },
        },
    });
    internal static string Base64(string format) => Convert.ToBase64String(format switch
    { "jpeg" => [255, 216, 255, 0], "webp" => "RIFFabcdWEBPdata"u8.ToArray(), _ => [137, 80, 78, 71, 13, 10, 26, 10, 0], });
    internal static HttpResponseMessage Response(string body, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(body) };

    internal static async Task<StructuredErrorKind?> Observe<T>(Task<StructuredResult<T>> pending) => (await pending).Error?.Kind;

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

}
