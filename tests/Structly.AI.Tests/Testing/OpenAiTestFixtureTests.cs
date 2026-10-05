using Structly.AI.OpenAI;
using Structly.AI.Testing;
using System.Text.Json;

namespace Structly.AI.Tests;

public sealed class OpenAiTestFixtureTests
{
    [Fact]
    public async Task TextOverloadsRetainSchemaInstructionsAndUsage()
    {
        using var fixture = new OpenAiTestFixture();
        var task = StructuredTask.Create<Answer>("Extract the answer.");
        fixture.Enqueue(ResponseEnvelopes.CompletedText("{\"value\":\"first\"}", usage: new() { TotalTokens = 7 }));
        fixture.Enqueue(ResponseEnvelopes.CompletedText("{\"value\":\"second\"}"));
        var result = await fixture.Client.ExecuteAsync(task, "first input", TestContext.Current.CancellationToken);
        Assert.Equal("first", result.EnsureSuccess().Value);
        Assert.Equal(7, result.Metadata.Usage!.TotalTokens);
        Assert.Equal("second", (await fixture.Client.ExecuteAsync(task.BindOutput(), "second input", TestContext.Current.CancellationToken)).EnsureSuccess().Value);
        Assert.Equal(0, fixture.PendingResponseCount);
        var requests = fixture.Requests;
        Assert.Equal(2, requests.Count);
        Assert.Equal(HttpMethod.Post, requests[0].Method);
        Assert.Equal("/v1/responses", requests[0].Uri.AbsolutePath);
        var json = requests[0].ReadJson();
        Assert.Equal("Extract the answer.", json.GetProperty("instructions").GetString());
        Assert.Contains("first input", requests[0].Body!);
        Assert.Equal(task.SchemaName, json.GetProperty("text").GetProperty("format").GetProperty("name").GetString());
        fixture.Enqueue(ResponseEnvelopes.Refusal());
        await fixture.Client.ExecuteAsync(task, "third input", TestContext.Current.CancellationToken);
        Assert.Equal(2, requests.Count);
        Assert.Equal(3, fixture.Requests.Count);
    }

    [Fact]
    public async Task InvalidOutputIsNotRepairedAndEnvelopeIsDetached()
    {
        using var fixture = new OpenAiTestFixture();
        using(var document = JsonDocument.Parse(ResponseEnvelopes.CompletedText("{broken", usage: new() { TotalTokens = 9 }).GetRawText()))
            fixture.Enqueue(document.RootElement);

        var result = await fixture.Client.ExecuteAsync(StructuredTask.Create<Answer>("Extract"), "input", TestContext.Current.CancellationToken);
        Assert.Equal(StructuredErrorKind.InvalidOutput, result.Error!.Kind);
        Assert.Equal(9, result.Metadata.Usage!.TotalTokens);
    }

    [Fact]
    public async Task LocalValidationAndCancellationDoNotConsumeResponses()
    {
        using var fixture = new OpenAiTestFixture();
        fixture.Enqueue(ResponseEnvelopes.Refusal());
        var task = StructuredTask.Create<Answer>("Extract");
        Assert.False((await fixture.Client.ExecuteAsync(task, " ", TestContext.Current.CancellationToken)).IsSuccess);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<StructuredOperationCanceledException>(() => fixture.Client.ExecuteAsync(task.BindOutput(), "input", cancellation.Token));
        Assert.Empty(fixture.Requests);
        Assert.Equal(1, fixture.PendingResponseCount);
        Assert.Equal(StructuredErrorKind.Refused, (await fixture.Client.ExecuteAsync(task, "input", TestContext.Current.CancellationToken)).Error!.Kind);
    }

    [Fact]
    public async Task ExhaustionThrowsAndDisposalPreservesCapturedRequests()
    {
        var fixture = new OpenAiTestFixture();
        try
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Client.ExecuteAsync(StructuredTask.Create<Answer>("Extract"), "input", TestContext.Current.CancellationToken));
            Assert.Contains("No response is queued", exception.Message);
            fixture.Dispose();
            fixture.Dispose();
            Assert.Single(fixture.Requests);
            Assert.Equal(JsonValueKind.Object, fixture.Requests[0].ReadJson().ValueKind);
            Assert.Throws<ObjectDisposedException>(() => fixture.Enqueue(ResponseEnvelopes.Refusal()));
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task QueuedHttpStatusUsesRealErrorClassification()
    {
        using var fixture = new OpenAiTestFixture();
        fixture.Enqueue(ResponseEnvelopes.Refusal(), System.Net.HttpStatusCode.ServiceUnavailable);
        var result = await fixture.Client.ExecuteAsync(StructuredTask.Create<Answer>("Extract"), "input", TestContext.Current.CancellationToken);
        Assert.Equal(StructuredErrorKind.ProviderUnavailable, result.Error!.Kind);
        Assert.Equal(System.Net.HttpStatusCode.ServiceUnavailable, result.Error.HttpStatusCode);
    }

    [Fact]
    public void OptionsOverloadRemainsUnambiguousAndInstructionsAreValidated()
    {
        Assert.NotNull(StructuredTask.Create<Answer>(new()));
        Assert.Throws<ArgumentException>(() => StructuredTask.Create<Answer>(" "));
        Assert.Throws<ArgumentNullException>(() => StructuredTask.Create<Answer>((string)null!));
    }

    [Fact]
    public async Task ResponderCompletesTheActualRequestAndQueuesTakePrecedence()
    {
        using var fixture = new OpenAiTestFixture(null, (request, token) =>
        {
            Assert.False(token.IsCancellationRequested);
            return ValueTask.FromResult(ResponseEnvelopes.ToHttpResponse(ResponseEnvelopes.CompletedJson(
                ResponseEnvelopes.CompleteFromRequest(request.ReadJson(), "{}"))));
        });
        var task = StructuredTask.Create<Answer>("Extract");
        fixture.Enqueue(ResponseEnvelopes.CompletedText("{\"value\":\"queued\"}"));
        Assert.Equal("queued", (await fixture.Client.ExecuteAsync(task, "input", TestContext.Current.CancellationToken)).EnsureSuccess().Value);
        Assert.Equal("", (await fixture.Client.ExecuteAsync(task, "input", TestContext.Current.CancellationToken)).EnsureSuccess().Value);
        Assert.Equal(2, fixture.Requests.Count);
    }

    [Fact]
    public async Task RequestResponderUsesEachCallsVocabulary()
    {
        using var fixture = new OpenAiTestFixture(null, (request, _) => ValueTask.FromResult(
            ResponseEnvelopes.ToHttpResponse(ResponseEnvelopes.CompletedJson(
                ResponseEnvelopes.CompleteFromRequest(request.ReadJson(), "{}")))));
        var task = StructuredTask.Create<VocabularyAnswer>("Extract");
        foreach(var choice in new[] { "first", "second" })
        {
            var result = await fixture.Client.ExecuteAsync(task, new()
            {
                Input = "input",
                Vocabularies = new Dictionary<string, IReadOnlyList<string>> { ["choices"] = [choice] },
            }, TestContext.Current.CancellationToken);
            Assert.Equal(choice, result.EnsureSuccess().Choice);
        }
    }

    [Fact]
    public async Task ConstructionOptionsAllowMissingCredentialsAndShortDeadlines()
    {
        using var missing = new OpenAiTestFixture(new() { DefaultModel = new() { ModelId = "test" } });
        var task = StructuredTask.Create<Answer>("Extract");
        Assert.Equal(StructuredErrorKind.CredentialsMissing,
            (await missing.Client.ExecuteAsync(task, "input", TestContext.Current.CancellationToken)).Error!.Kind);
        Assert.Empty(missing.Requests);
        var clock = new ReliabilityClock();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var fixture = new OpenAiTestFixture(new()
        {
            DefaultModel = new() { ModelId = "test" },
            CredentialResolver = Credentials.FromStatic("fake"),
            TotalTimeout = TimeSpan.FromMilliseconds(50),
            TimeProvider = clock,
        }, async (_, token) =>
        {
            entered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException();
        });
        var pending = fixture.Client.ExecuteAsync(task, "input", TestContext.Current.CancellationToken);
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromMilliseconds(50));
        var result = await pending.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(StructuredErrorKind.DeadlineExceeded, result.Error!.Kind);
        Assert.Equal(TimeSpan.FromMilliseconds(50), result.Error.TotalTimeout);
    }

    sealed record VocabularyAnswer
    {
        [DynamicVocabulary("choices")]
        public required string Choice { get; init; }
    }

    sealed record Answer(string Value);
}
