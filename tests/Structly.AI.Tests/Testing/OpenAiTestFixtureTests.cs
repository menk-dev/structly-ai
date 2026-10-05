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

    sealed record Answer(string Value);
}
