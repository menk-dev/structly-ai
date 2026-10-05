using Structly.AI.Mistral;
using System.Net;
using System.Text;

namespace Structly.AI.Tests;

static class MistralFixture
{
    public const string Chat = """{"id":"response","model":"resolved","usage":{"prompt_tokens":3,"completion_tokens":4,"total_tokens":7},"choices":[{"index":0,"finish_reason":"stop","message":{"role":"assistant","content":"{\"value\":\"answer\"}"}}]}""";
    public const string Vectors = """{"id":"embedding","model":"resolved","usage":{"prompt_tokens":3,"total_tokens":3},"data":[{"index":1,"embedding":[3,4]},{"index":0,"embedding":[1,2]}]}""";
    public const string Job = """{"id":"job","status":"SUCCESS","model":"offline","input_files":["input"],"endpoint":"/v1/chat/completions","output_file":"output","error_file":null}""";
    public static MistralOptions Options() => new() { DefaultModel = new() { ModelId = "offline" }, ApiKey = "test" };
    public sealed record Answer(string Value);

    public sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public Handler(string envelope) : this(_ => Response(envelope)) { }
        public List<string> Payloads { get; } = [];
        public List<Uri> Uris { get; } = [];
        public List<HttpMethod> Methods { get; } = [];
        public static HttpResponseMessage Response(string envelope, HttpStatusCode status = HttpStatusCode.OK, string mediaType = "application/json")
            => new(status) { Content = new StringContent(envelope, Encoding.UTF8, mediaType) };
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Payloads.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
            Uris.Add(request.RequestUri!);
            Methods.Add(request.Method);
            return respond(request);
        }
    }
}
