using Structly.AI.Mistral;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using static Structly.AI.Tests.MistralFixture;

namespace Structly.AI.Tests;

public sealed class MistralReliabilityTests
{
    [Fact]
    public async Task Noncooperative_transport_is_abandoned_at_deadline_and_late_response_is_disposed()
    {
        var clock = new ReliabilityClock();
        var response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new DelayedHandler(response, started);
        using var http = new HttpClient(handler);
        var options = Options();
        options.TimeProvider = clock;
        options.TotalTimeout = TimeSpan.FromSeconds(2);
        var pending = new MistralClient(http, options).GenerateTextAsync(new() { Request = new() { Input = "input" } }, TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(StructuredErrorKind.DeadlineExceeded, (await pending).Error!.Kind);
        var content = new ProbeContent();
        response.SetResult(new(HttpStatusCode.OK) { Content = content });
        await content.Disposed.Task.WaitAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Usage_callback_has_a_separate_five_second_budget_and_safe_warnings()
    {
        var clock = new ReliabilityClock();
        using var handler = new Handler(Chat);
        using var http = new HttpClient(handler);
        var options = Options();
        options.TimeProvider = clock;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new MistralClient(http, options).GenerateTextAsync(new()
        {
            Request = new()
            {
                Input = "input",
                UsageObserver = async (_, _) =>
                {
                    started.SetResult();
                    await release.Task;
                },
            },
        }, TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromSeconds(5));
        var result = await pending;
        release.SetResult();
        Assert.True(result.IsSuccess);
        Assert.Contains(result.Warnings, x => x.Code == "UsageObserverTimedOut");
    }

    [Fact]
    public async Task Malformed_usage_fields_do_not_replace_valid_output_or_other_counts()
    {
        using var handler = new Handler(Chat.Replace("\"prompt_tokens\":3", "\"prompt_tokens\":-1"));
        using var http = new HttpClient(handler);
        StructuredUsageEvent? observed = null;
        var result = await new MistralClient(http, Options()).GenerateTextAsync(new()
        {
            Request = new()
            {
                Input = "input",
                UsageObserver = (usage, _) =>
            {
                observed = usage;
                return ValueTask.CompletedTask;
            },
            },
        }, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Null(result.Metadata.Usage!.InputTokens);
        Assert.Equal(4, result.Metadata.Usage.OutputTokens);
        Assert.Equal(7, observed!.Usage.TotalTokens);
        Assert.Contains(result.Warnings, x => x.Code == "InvalidUsage");
    }

    [Fact]
    public async Task Retry_dates_use_the_configured_clock()
    {
        var clock = new ReliabilityClock();
        using var handler = new Handler(_ =>
        {
            var response = Handler.Response("secret", HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(clock.GetUtcNow().AddSeconds(3));
            return response;
        });
        using var http = new HttpClient(handler);
        var options = Options();
        options.TimeProvider = clock;
        var result = await new MistralClient(http, options).GenerateTextAsync(new() { Request = new() { Input = "input" } }, TestContext.Current.CancellationToken);
        Assert.Equal(TimeSpan.FromSeconds(3), result.Error!.RetryAfter);
    }

    [Fact]
    public async Task Chat_embedding_and_image_profiles_are_independent_snapshots()
    {
        using var handler = new Handler(request => Handler.Response(request.RequestUri!.AbsolutePath.EndsWith("/embeddings", StringComparison.Ordinal) ? Vectors : Chat));
        using var http = new HttpClient(handler);
        var options = Options();
        options.Profiles["shared"] = new() { ModelId = "chat", ReasoningEffort = ReasoningEffort.Low };
        options.EmbeddingProfiles["shared"] = new() { ModelId = "embed" };
        options.ImageProfiles["shared"] = new() { ModelId = "image" };
        var client = new MistralClient(http, options);
        options.EmbeddingProfiles["shared"] = new() { ModelId = "changed" };
        Assert.True((await client.GenerateTextAsync(new()
        {
            Request = new() { Input = "input", ModelSelection = new() { ProfileName = "shared", ReasoningEffort = ReasoningEffort.High } },
        }, TestContext.Current.CancellationToken)).IsSuccess);
        Assert.True((await client.EmbedAsync(new() { Inputs = ["one", "two"], ModelSelection = new() { ProfileName = "shared" } }, TestContext.Current.CancellationToken)).IsSuccess);
        using var chat = JsonDocument.Parse(handler.Payloads[0]);
        using var embed = JsonDocument.Parse(handler.Payloads[1]);
        Assert.Equal("chat", chat.RootElement.GetProperty("model").GetString());
        Assert.Equal("high", chat.RootElement.GetProperty("reasoning_effort").GetString());
        Assert.Equal("embed", embed.RootElement.GetProperty("model").GetString());
    }

    sealed class DelayedHandler(TaskCompletionSource<HttpResponseMessage> response, TaskCompletionSource started) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            started.SetResult();
            return response.Task;
        }
    }

    sealed class ProbeContent : HttpContent
    {
        public TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(Encoding.UTF8.GetBytes(Chat)).AsTask();
        protected override bool TryComputeLength(out long length)
        {
            length = Encoding.UTF8.GetByteCount(Chat);
            return true;
        }
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            Disposed.TrySetResult();
        }
    }
}
