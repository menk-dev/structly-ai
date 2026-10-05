using Structly.AI.OpenAI;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace Structly.AI.Tests;

static class ReliabilityTestSupport
{
    internal static CancellationToken TestToken => TestContext.Current.CancellationToken;
    internal static StructuredTask<Answer> Contract() => StructuredTask.Create<Answer>(new() { Instructions = "Extract" });
    internal static string Envelope(string status = "completed", string text = "{\"value\":42}", bool usage = true) => JsonSerializer.Serialize(new
    {
        id = "response",
        model = "resolved",
        status,
        usage = usage ? new { input_tokens = 10, output_tokens = 5 } : null,
        output = new[] { new { type = "message", role = "assistant", status = "completed", content = new[] { new { type = "output_text", text } } } },
    });
    internal static string Event(string type, string envelope) => $"event: {type}\r\ndata: {{\"type\":\"{type}\",\"response\":{envelope}}}\r\n\r\n";
    internal static HttpResponseMessage Response(Stream stream, bool sse = true)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) };
        response.Content.Headers.ContentType = new(sse ? "text/event-stream" : "application/json");
        response.Headers.Add("x-request-id", "request");
        return response;
    }
    internal static OpenAiClient Client(HttpClient http, ReliabilityClock clock, Func<StructuredUsageEvent, CancellationToken, ValueTask>? observer = null) => new(http,
        new()
        {
            DefaultModel = new() { ModelId = "configured" },
            CredentialResolver = Credentials.FromStatic("synthetic"),
            TimeProvider = clock,
            TotalTimeout = TimeSpan.FromSeconds(20),
            UsageObserver = observer,
        });
    internal static TaskCompletionSource<T> Gate<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal static readonly AsyncLocal<Action?> _materializationCancellation = new();
    public sealed class CancellingConstructor
    {
        public int Value { get; }
        public CancellingConstructor(int value) => _materializationCancellation.Value!();
    }

    internal sealed class DeferredContent(Task<Stream> stream, TaskCompletionSource<bool> entered) : HttpContent
    {
        protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken)
        {
            entered.TrySetResult(true);
            return stream;
        }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => throw new NotSupportedException();
        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    public sealed record Choice
    {
        [DynamicVocabulary("choices")]
        public required string Value { get; init; }
    }
    public sealed record Answer { public int Value { get; init; } }
    internal sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int Calls { get; set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return send(request);
        }
    }
    internal sealed class FragmentStream(string body, int fragment = 4096) : MemoryStream(Encoding.UTF8.GetBytes(body))
    {
        public TaskCompletionSource<bool> Disposed { get; } = Gate<bool>();
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(buffer.Length, fragment)], cancellationToken);
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if(disposing)
                Disposed.TrySetResult(true);
        }
    }
    internal sealed class ControlledStream : Stream
    {
        readonly Channel<byte[]> _chunks = Channel.CreateUnbounded<byte[]>();
        readonly Channel<bool> _reads = Channel.CreateUnbounded<bool>();
        public bool IsDisposed { get; set; }
        public void Push(string text) => _chunks.Writer.TryWrite(Encoding.UTF8.GetBytes(text));
        public async Task NextRead() => await _reads.Reader.ReadAsync(TestToken);
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            _reads.Writer.TryWrite(true);
            // Deliberately ignore cancellation to prove that the library abandons the wait.
            var chunk = await _chunks.Reader.ReadAsync();
            chunk.CopyTo(buffer);
            return chunk.Length;
        }
        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            _chunks.Writer.TryComplete();
            base.Dispose(disposing);
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

}
