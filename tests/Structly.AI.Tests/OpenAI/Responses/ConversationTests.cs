using System.Text.Json;
using static Structly.AI.Tests.ResponseTestSupport;

namespace Structly.AI.Tests;

public sealed class ConversationTests
{
    [Fact]
    public void BindingSnapshotsAndValidates()
    {
        var values = new List<string> { "z", "a" };
        var dictionary = new Dictionary<string, IReadOnlyList<string>> { ["choices"] = values };
        var options = new OutputSpecificationOptions { IncludeExample = false };
        var output = Contract<Guided>().BindOutput(new() { Vocabularies = dictionary, OutputSpecification = options });
        var schema = output.CreateSchema().GetRawText();
        values.Clear();
        dictionary.Clear();
        options = options with { AdditionalInstructions = "changed" };
        Assert.Equal(schema, output.CreateSchema().GetRawText());
        Assert.Equal("a", output.CreateSchema().GetProperty("properties").GetProperty("choice").GetProperty("enum")[0].GetString());
        Assert.Throws<StructuredSchemaException>(() => Contract<Guided>().BindOutput());
        Assert.Throws<StructuredSchemaException>(() => Contract<Guided>().BindOutput(new() { Vocabularies = Vocabulary("a", "a") }));
        Assert.Throws<ArgumentException>(() => Contract<Patterned>().BindOutput(new() { OutputSpecification = new() }));
        var supplied = StructuredTask.Create<Guided>(new() { VocabularyOrder = VocabularyOrder.PreserveInput }).BindOutput(new() { Vocabularies = Vocabulary("z", "a") });
        Assert.Equal("z", supplied.CreateSchema().GetProperty("properties").GetProperty("choice").GetProperty("enum")[0].GetString());
    }

    [Fact]
    public async Task BoundOverridesFailBeforeCredentials()
    {
        using var handler = new Handler(_ => throw new InvalidOperationException());
        using var http = new HttpClient(handler);
        var task = StructuredTask.Create<Patterned>(new() { Instructions = "Read", CredentialResolver = _ => throw new InvalidOperationException() });
        foreach(var request in new StructuredRequest[] { new() { Input = "read", Vocabularies = new Dictionary<string, IReadOnlyList<string>>() }, new() { Input = "read", OutputSpecification = new() } })
            Assert.Equal(StructuredErrorKind.InvalidRequest, (await Client(http).ExecuteAsync(task.BindOutput(), request, TestContext.Current.CancellationToken)).Error!.Kind);

        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TurnsAndTransitionsBranchIndependently(bool stream)
    {
        var payloads = new List<JsonElement>();
        using var handler = new Handler(async message =>
        {
            payloads.Add(JsonDocument.Parse(await message.Content!.ReadAsStringAsync()).RootElement.Clone());
            var body = Envelope(payloads.Count == 3 ? "invalid" : "{\"code\":\"AB\"}").Replace("\"current\"", "\"id" + payloads.Count + "\"");
            return Response(body, stream);
        });
        using var http = new HttpClient(handler);
        var conversation = Client(http).CreateConversation(Contract<Patterned>());
        var request = new ConversationRequest { Input = "read", Stream = stream };
        Assert.True((await conversation.ExecuteAsync(request, TestContext.Current.CancellationToken)).IsSuccess);
        var branch = conversation.ChangeOutput(Contract<Patterned>().BindOutput());
        Assert.True((await conversation.ExecuteAsync(request, TestContext.Current.CancellationToken)).IsSuccess);
        Assert.False((await conversation.ExecuteAsync(request, TestContext.Current.CancellationToken)).IsSuccess);
        Assert.True((await conversation.ExecuteAsync(request, TestContext.Current.CancellationToken)).IsSuccess);
        Assert.True((await branch.ExecuteAsync(request, TestContext.Current.CancellationToken)).IsSuccess);
        Assert.Equal("id2", payloads[3].GetProperty("previous_response_id").GetString());
        Assert.Equal("id1", payloads[4].GetProperty("previous_response_id").GetString());
        var configured = branch.ChangeConfiguration(branch.Configuration with { Instructions = "New", ModelSelection = new() { ProfileName = "fast" } });
        await configured.ExecuteAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal("New", payloads[5].GetProperty("instructions").GetString());
        Assert.Equal("response-model", configured.Configuration.ModelSelection.ModelId);
        var changed = branch.ChangeOutput(Contract<Guided>().BindOutput(new() { Vocabularies = Vocabulary("new") }));
        await changed.ExecuteAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal("Current instructions", payloads[6].GetProperty("instructions").GetString());
        Assert.Equal("new", payloads[6].GetProperty("text").GetProperty("format").GetProperty("schema").GetProperty("properties").GetProperty("choice").GetProperty("enum")[0].GetString());
        Assert.All(payloads, payload => Assert.True(payload.GetProperty("store").GetBoolean()));
        Assert.Equal(payloads[0].GetProperty("text").GetRawText(), payloads[1].GetProperty("text").GetRawText());
    }

    [Theory]
    [InlineData("refusal")]
    [InlineData("invalid")]
    [InlineData("incomplete")]
    [InlineData("missingId")]
    [InlineData("transport")]
    [InlineData("http")]
    public async Task FailedTurnsRetainPositionAndAccounting(string failure)
    {
        var calls = 0;
        using var handler = new Handler(async message =>
        {
            using var document = JsonDocument.Parse(await message.Content!.ReadAsStringAsync());
            if(++calls > 1)
                Assert.Equal("current", document.RootElement.GetProperty("previous_response_id").GetString());

            if(calls != 2)
                return Response(Envelope("{\"code\":\"AB\"}"));

            if(failure == "transport")
                throw new HttpRequestException("offline");

            if(failure == "http")
                return new(System.Net.HttpStatusCode.BadRequest);

            var envelope = Envelope(failure == "invalid" ? "{}" : "{\"code\":\"AB\"}",
                failure == "incomplete" ? "incomplete" : "completed", refusal: failure == "refusal");
            if(failure == "missingId")
                envelope = envelope.Replace("\"id\":\"current\",", "");

            return Response(envelope);
        });
        using var http = new HttpClient(handler);
        var conversation = Client(http).CreateConversation(Contract<Patterned>());
        Assert.True((await conversation.ExecuteAsync(new() { Input = "first" }, TestContext.Current.CancellationToken)).IsSuccess);
        var failed = await conversation.ExecuteAsync(new() { Input = "failed" }, TestContext.Current.CancellationToken);
        Assert.False(failed.IsSuccess);
        if(failure is "refusal" or "invalid" or "incomplete")
        {
            Assert.Equal("current", failed.Metadata.ResponseId);
            Assert.Equal(100, failed.Metadata.Usage!.InputTokens);
        }

        Assert.True((await conversation.ExecuteAsync(new() { Input = "again" }, TestContext.Current.CancellationToken)).IsSuccess);
    }

    [Fact]
    public async Task GuardAndSnapshotsSurviveCancellation()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new Handler(async message =>
        {
            entered.TrySetResult();
            await release.Task;
            return Response(Envelope("{\"code\":\"AB\"}"));
        });
        using var http = new HttpClient(handler);
        var conversation = Client(http).CreateConversation(Contract<Patterned>());
        using var cancellation = new CancellationTokenSource();
        var turn = conversation.ExecuteAsync(new() { Input = "read" }, cancellation.Token);
        await entered.Task;
        Assert.Throws<InvalidOperationException>(() => conversation.ChangeOutput(conversation.Output));
        await Assert.ThrowsAsync<InvalidOperationException>(() => conversation.ExecuteAsync(new() { Input = "overlap" }, TestContext.Current.CancellationToken));
        cancellation.Cancel();
        release.SetResult();
        await Assert.ThrowsAsync<StructuredOperationCanceledException>(() => turn);
        Assert.True((await conversation.ExecuteAsync(new() { Input = "again" }, TestContext.Current.CancellationToken)).IsSuccess);
    }

    [Fact]
    public async Task OutputTransitionDoesNotAdoptNewTaskCredentials()
    {
        using var handler = new Handler(_ => throw new InvalidOperationException());
        using var http = new HttpClient(handler);
        var client = new Structly.AI.OpenAI.OpenAiClient(http, new() { DefaultModel = new() { ModelId = "offline" } });
        var conversation = client.CreateConversation(Contract<Patterned>());
        var next = StructuredTask.Create<Patterned>(new()
        {
            Instructions = "Other instructions",
            CredentialResolver = _ => throw new InvalidOperationException("New task credentials must not run"),
        });
        var result = await conversation.ChangeOutput(next.BindOutput()).ExecuteAsync(new() { Input = "read" }, TestContext.Current.CancellationToken);
        Assert.False(result.IsSuccess);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task ConversationValidatesConfigurationAndUserRoles()
    {
        using var handler = new Handler(_ => throw new InvalidOperationException());
        using var http = new HttpClient(handler);
        var client = Client(http);
        Assert.Throws<ArgumentException>(() => client.CreateConversation(StructuredTask.Create<Patterned>(new())));
        var conversation = client.CreateConversation(Contract<Patterned>(), new() { IncludeReasoningSummary = true });
        Assert.Equal(StructuredErrorKind.InvalidRequest, (await conversation.ExecuteAsync(new() { Input = "read" }, TestContext.Current.CancellationToken)).Error!.Kind);
        foreach(var role in new[] { MessageRole.Developer, MessageRole.Assistant })
            Assert.Equal(StructuredErrorKind.InvalidRequest, (await conversation.ExecuteAsync(new() { Stream = true, Messages = [new(role, [new TextPart("read")])] }, TestContext.Current.CancellationToken)).Error!.Kind);

        Assert.Equal(0, handler.Calls);
    }
}
