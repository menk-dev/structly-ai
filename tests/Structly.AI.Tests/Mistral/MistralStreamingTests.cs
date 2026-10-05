using Structly.AI.Mistral;
using System.Net;
using System.Text;
using static Structly.AI.Tests.MistralFixture;

namespace Structly.AI.Tests;

public sealed class MistralStreamingTests
{
    const string _stream = """
        : keepalive

        data: {"id":"response","model":"resolved","choices":[{"index":0,"delta":{"content":[{"type":"thinking","thinking":[{"type":"text","text":"secret"}]},{"type":"text","text":"{\"value\":"}]},"finish_reason":null}]}

        data: {"choices":[{"index":0,"delta":{"content":"\"answer\"}"},"finish_reason":"stop"}],"usage":{"prompt_tokens":3,"completion_tokens":4,"total_tokens":7}}

        data: [DONE]


        """;

    [Fact]
    public async Task Streaming_validates_final_output_and_never_exposes_thinking_progress()
    {
        using var handler = new Handler(_stream);
        using var http = new HttpClient(handler);
        var progress = new List<StructuredProgress>();
        var result = await new MistralClient(http, Options()).ExecuteAsync(StructuredTask.Create<Answer>("Extract"), new MistralRequest
        {
            Input = "input",
            Stream = true,
            CaptureOutputText = true,
            ModelSelection = new() { ModelId = "offline", ReasoningEffort = ReasoningEffort.High },
            Progress = (item, _) =>
            {
                progress.Add(item);
                return ValueTask.CompletedTask;
            },
        }, TestContext.Current.CancellationToken);
        Assert.Equal("answer", result.EnsureSuccess().Value);
        Assert.Equal(7, result.Metadata.Usage!.TotalTokens);
        Assert.Equal("response", result.Metadata.ResponseId);
        Assert.Equal("{\"value\":\"answer\"}", result.Metadata.OutputText);
        Assert.Null(result.Metadata.RawResponse);
        Assert.Equal(new[] { StructuredProgressKind.Started, StructuredProgressKind.OutputTextDelta, StructuredProgressKind.OutputTextDelta, StructuredProgressKind.Completed }, progress.Select(x => x.Kind));
        Assert.DoesNotContain(progress, x => x.TextDelta?.Contains("secret", StringComparison.Ordinal) == true);
    }

    [Theory]
    [InlineData("data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"answer\"},\"finish_reason\":null}]}\n\ndata: [DONE]\n\n", StructuredErrorKind.InvalidResponse)]
    [InlineData("data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"partial\"},\"finish_reason\":\"length\"}]}\n\ndata: [DONE]\n\n", StructuredErrorKind.IncompleteOutput)]
    [InlineData("data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"answer\"},\"finish_reason\":\"stop\"}]}\n\n", StructuredErrorKind.InvalidResponse)]
    public async Task Truncated_or_incomplete_streams_are_failures(string stream, StructuredErrorKind kind)
    {
        using var handler = new Handler(stream);
        using var http = new HttpClient(handler);
        var result = await new MistralClient(http, Options()).GenerateTextAsync(new() { Request = new() { Input = "input", Stream = true } }, TestContext.Current.CancellationToken);
        Assert.Equal(kind, result.Error!.Kind);
    }

    [Fact]
    public async Task Stream_envelope_limit_is_enforced()
    {
        using var handler = new Handler(_stream);
        using var http = new HttpClient(handler);
        var options = Options();
        options.MaxResponseBytes = 10;
        var result = await new MistralClient(http, options).GenerateTextAsync(new() { Request = new() { Input = "input", Stream = true } }, TestContext.Current.CancellationToken);
        Assert.Equal(StructuredErrorKind.InvalidResponse, result.Error!.Kind);
    }

    [Fact]
    public async Task Progress_failures_are_best_effort()
    {
        using var handler = new Handler(_stream);
        using var http = new HttpClient(handler);
        var result = await new MistralClient(http, Options()).GenerateTextAsync(new()
        {
            Request = new() { Input = "input", Stream = true, Progress = (_, _) => throw new InvalidOperationException("observer") },
        }, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Contains(result.Warnings, x => x.Code == "ProgressObserverFailed");
    }

    [Theory]
    [InlineData("\r")]
    [InlineData("\r\n")]
    public async Task Sse_accepts_bom_multiline_data_and_cr_line_endings(string newline)
    {
        var stream = "\uFEFFdata: {\"choices\":[{\"index\":0,\n" +
            "data: \"delta\":{\"content\":\"answer\"},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n";
        using var handler = new Handler(stream.Replace("\n", newline));
        using var http = new HttpClient(handler);
        var result = await new MistralClient(http, Options()).GenerateTextAsync(new() { Request = new() { Input = "input", Stream = true } }, TestContext.Current.CancellationToken);
        Assert.Equal("answer", result.EnsureSuccess());
    }

    [Fact]
    public async Task Inactivity_deadline_and_caller_cancellation_stop_reads()
    {
        using var stream = new BlockingStream();
        using var handler = new Handler(_ => new(HttpStatusCode.OK) { Content = new StreamContent(stream) });
        using var http = new HttpClient(handler);
        var clock = new ReliabilityClock();
        var options = Options();
        options.TimeProvider = clock;
        var client = new MistralClient(http, options);
        var pendingRead = client.GenerateTextAsync(new()
        {
            Request = new() { Input = "input", Stream = true, InactivityTimeout = TimeSpan.FromSeconds(1) },
        }, TestContext.Current.CancellationToken);
        await stream.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromSeconds(1));
        var result = await pendingRead;
        Assert.Equal(StructuredErrorKind.InactivityExceeded, result.Error!.Kind);

        using var secondStream = new BlockingStream();
        using var secondHandler = new Handler(_ => new(HttpStatusCode.OK) { Content = new StreamContent(secondStream) });
        using var secondHttp = new HttpClient(secondHandler);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var pending = new MistralClient(secondHttp, Options()).GenerateTextAsync(new() { Request = new() { Input = "input", Stream = true } }, cancellation.Token);
        await secondStream.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
        cancellation.Cancel();
        var exception = await Assert.ThrowsAsync<StructuredOperationCanceledException>(() => pending);
        Assert.Equal(cancellation.Token, exception.CancellationToken);
    }

    sealed class BlockingStream : Stream
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
