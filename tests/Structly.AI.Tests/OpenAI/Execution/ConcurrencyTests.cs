using System.Text.Json;
using System.Threading.Channels;
using static Structly.AI.Tests.ReliabilityTestSupport;

namespace Structly.AI.Tests;

public sealed class ConcurrencyTests
{
    [Fact]
    public async Task ConcurrentTaskReuseIsolatesVocabularyCredentialsOptionsAndObservers()
    {
        var entered = Channel.CreateUnbounded<bool>();
        var release = Gate<bool>();
        var snapshots = new System.Collections.Concurrent.ConcurrentDictionary<string, JsonElement>();
        using var handler = new Handler(async message =>
        {
            using var document = JsonDocument.Parse(await message.Content!.ReadAsStringAsync(TestToken));
            var root = document.RootElement;
            var input = root.GetProperty("input").GetString()!;
            Assert.Equal(input + "-key", message.Headers.Authorization!.Parameter);
            snapshots[input] = root.Clone();
            entered.Writer.TryWrite(true);
            await release.Task.WaitAsync(TestToken);
            return Response(new FragmentStream(Envelope(text: JsonSerializer.Serialize(new { value = input }))), false);
        });
        using var http = new HttpClient(handler);
        var client = Client(http, new(), (_, _) => throw new InvalidOperationException("Default observer must be overridden"));
        var task = StructuredTask.Create<Choice>(new() { Instructions = "Choose" });
        var wordsA = new List<string> { "apple" };
        var wordsB = new List<string> { "pear" };
        StructuredUsageEvent? eventA = null;
        StructuredUsageEvent? eventB = null;
        var first = client.ExecuteAsync(task, new()
        {
            Input = "apple",
            CredentialResolver = Credentials.FromStatic("apple-key"),
            Vocabularies = new Dictionary<string, IReadOnlyList<string>> { ["choices"] = wordsA },
            MaxOutputTokens = 50,
            ModelSelection = new() { ModelId = "model-a" },
            OpenAi = new() { CaptureOutputText = true, Metadata = new Dictionary<string, string> { ["job"] = "a" } },
            UsageObserver = (item, _) =>
            {
                eventA = item;
                throw new InvalidOperationException("secret");
            },
        }, TestToken);
        var second = client.ExecuteAsync(task, new()
        {
            Input = "pear",
            CredentialResolver = Credentials.FromStatic("pear-key"),
            Vocabularies = new Dictionary<string, IReadOnlyList<string>> { ["choices"] = wordsB },
            MaxOutputTokens = 100,
            ModelSelection = new() { ModelId = "model-b" },
            UsageObserver = (item, _) =>
            {
                eventB = item;
                return ValueTask.CompletedTask;
            },
        }, TestToken);
        await entered.Reader.ReadAsync(TestToken);
        await entered.Reader.ReadAsync(TestToken);
        wordsA[0] = "mutated";
        wordsB[0] = "mutated";
        release.SetResult(true);
        var results = await Task.WhenAll(first, second);
        Assert.Equal("apple", results[0].EnsureSuccess().Value);
        Assert.Equal("pear", results[1].EnsureSuccess().Value);
        Assert.Equal("UsageObserverFailed", Assert.Single(results[0].Warnings).Code);
        Assert.Empty(results[1].Warnings);
        Assert.NotNull(results[0].Metadata.OutputText);
        Assert.Null(results[1].Metadata.OutputText);
        Assert.NotEqual(eventA!.Metadata.ExecutionId, eventB!.Metadata.ExecutionId);
        foreach(var input in new[] { "apple", "pear" })
        {
            var wire = snapshots[input];
            Assert.Equal(input, wire.GetProperty("text").GetProperty("format").GetProperty("schema")
                .GetProperty("properties").GetProperty("value").GetProperty("enum")[0].GetString());
            Assert.Equal(input == "apple" ? "model-a" : "model-b", wire.GetProperty("model").GetString());
            Assert.Equal(input == "apple" ? 50 : 100, wire.GetProperty("max_output_tokens").GetInt32());
        }

        Assert.False(snapshots["pear"].TryGetProperty("metadata", out _));
        Assert.Null(http.DefaultRequestHeaders.Authorization);
    }

    [Fact]
    public async Task ConcurrentRequestsHaveIndependentDeadlineSources()
    {
        var clock = new ReliabilityClock();
        var responses = new Dictionary<string, TaskCompletionSource<HttpResponseMessage>> { ["short"] = Gate<HttpResponseMessage>(), ["long"] = Gate<HttpResponseMessage>() };
        using var handler = new Handler(async message =>
        {
            using var json = JsonDocument.Parse(await message.Content!.ReadAsStringAsync(TestToken));
            return await responses[json.RootElement.GetProperty("input").GetString()!].Task.WaitAsync(TestToken);
        });
        using var http = new HttpClient(handler);
        var client = Client(http, clock);
        var task = Contract();
        var shortCall = client.ExecuteAsync(task, new() { Input = "short", TotalTimeout = TimeSpan.FromSeconds(2) }, TestToken);
        var longCall = client.ExecuteAsync(task, new() { Input = "long", TotalTimeout = TimeSpan.FromSeconds(10) }, TestToken);
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(StructuredErrorKind.DeadlineExceeded, (await shortCall.WaitAsync(TestToken)).Error!.Kind);
        Assert.False(longCall.IsCompleted);
        responses["long"].SetResult(Response(new FragmentStream(Envelope()), false));
        Assert.True((await longCall.WaitAsync(TestToken)).IsSuccess);
        var late = new FragmentStream(Envelope());
        responses["short"].SetResult(Response(late, false));
        await late.Disposed.Task.WaitAsync(TestToken);
    }

}
