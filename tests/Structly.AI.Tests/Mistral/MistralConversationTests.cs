using Structly.AI.Mistral;
using System.Text.Json;
using static Structly.AI.Tests.MistralFixture;

namespace Structly.AI.Tests;

public sealed class MistralConversationTests
{
    [Fact]
    public async Task Branches_replay_successful_history_and_preserve_thinking()
    {
        var thinking = """{"model":"resolved","choices":[{"finish_reason":"stop","message":{"role":"assistant","content":[{"type":"thinking","thinking":[{"type":"text","text":"trace"}]},{"type":"text","text":"{\"value\":\"answer\"}"}]}}]}""";
        using var handler = new Handler(thinking);
        using var http = new HttpClient(handler);
        var client = new MistralClient(http, Options());
        var task = StructuredTask.Create<Answer>("Extract");
        var conversation = client.CreateConversation(task);
        Assert.True((await conversation.ExecuteAsync(new() { Input = "first" }, TestContext.Current.CancellationToken)).IsSuccess);
        var branch = conversation.ChangeOutput(task.BindOutput());
        Assert.True((await conversation.ExecuteAsync(new() { Input = "second" }, TestContext.Current.CancellationToken)).IsSuccess);
        Assert.True((await branch.ExecuteAsync(new() { Input = "branch" }, TestContext.Current.CancellationToken)).IsSuccess);
        using var document = JsonDocument.Parse(handler.Payloads[2]);
        var messages = document.RootElement.GetProperty("messages");
        Assert.Equal(4, messages.GetArrayLength());
        Assert.Equal("first", messages[1].GetProperty("content").GetString());
        Assert.Equal("thinking", messages[2].GetProperty("content")[0].GetProperty("type").GetString());
        Assert.Equal("branch", messages[3].GetProperty("content").GetString());
        Assert.DoesNotContain("second", handler.Payloads[2]);
    }

    [Fact]
    public async Task Failed_turns_do_not_advance_and_configuration_branches_replace_instructions()
    {
        var count = 0;
        using var handler = new Handler(_ => Handler.Response(count++ == 1 ? Chat.Replace("answer", "bad").Replace("stop", "length") : Chat));
        using var http = new HttpClient(handler);
        var conversation = new MistralClient(http, Options()).CreateConversation(StructuredTask.Create<Answer>("Extract"));
        Assert.True((await conversation.ExecuteAsync(new() { Input = "first" }, TestContext.Current.CancellationToken)).IsSuccess);
        Assert.False((await conversation.ExecuteAsync(new() { Input = "failed" }, TestContext.Current.CancellationToken)).IsSuccess);
        var branch = conversation.ChangeConfiguration(new() { Instructions = "Correct", ModelSelection = new() { ModelId = "other" } });
        Assert.True((await branch.ExecuteAsync(new() { Input = "third" }, TestContext.Current.CancellationToken)).IsSuccess);
        using var document = JsonDocument.Parse(handler.Payloads[2]);
        Assert.Equal("other", document.RootElement.GetProperty("model").GetString());
        Assert.Equal("Correct", document.RootElement.GetProperty("messages")[0].GetProperty("content").GetString());
        Assert.DoesNotContain("failed", handler.Payloads[2]);
    }

    [Fact]
    public async Task Streaming_turns_are_retained_for_followups()
    {
        const string stream = "data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"{\\\"value\\\":\\\"answer\\\"}\"},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n";
        var count = 0;
        using var handler = new Handler(_ => Handler.Response(count++ == 0 ? stream : Chat));
        using var http = new HttpClient(handler);
        var conversation = new MistralClient(http, Options()).CreateConversation(StructuredTask.Create<Answer>("Extract"));
        Assert.True((await conversation.ExecuteAsync(new() { Input = "first", Stream = true }, TestContext.Current.CancellationToken)).IsSuccess);
        Assert.True((await conversation.ExecuteAsync(new() { Input = "second" }, TestContext.Current.CancellationToken)).IsSuccess);
        using var document = JsonDocument.Parse(handler.Payloads[1]);
        Assert.Equal("{\"value\":\"answer\"}", document.RootElement.GetProperty("messages")[2].GetProperty("content")[0].GetProperty("text").GetString());
    }

    [Fact]
    public async Task Streamed_thinking_is_reassembled_with_the_closing_signature_before_replay()
    {
        const string stream = """
            data: {"choices":[{"index":0,"delta":{"content":[{"type":"thinking","thinking":[{"type":"text","text":"first"}],"closed":false}]},"finish_reason":null}]}

            data: {"choices":[{"index":0,"delta":{"content":[{"type":"thinking","thinking":[{"type":"text","text":"second"}],"signature":"signed","closed":true},{"type":"text","text":"{\"value\":\"answer\"}"}]},"finish_reason":"stop"}]}

            data: [DONE]


            """;
        var count = 0;
        using var handler = new Handler(_ => Handler.Response(count++ == 0 ? stream : Chat));
        using var http = new HttpClient(handler);
        var conversation = new MistralClient(http, Options()).CreateConversation(StructuredTask.Create<Answer>("Extract"));
        Assert.True((await conversation.ExecuteAsync(new() { Input = "first", Stream = true }, TestContext.Current.CancellationToken)).IsSuccess);
        Assert.True((await conversation.ExecuteAsync(new() { Input = "second" }, TestContext.Current.CancellationToken)).IsSuccess);
        using var document = JsonDocument.Parse(handler.Payloads[1]);
        var thinking = document.RootElement.GetProperty("messages")[2].GetProperty("content")[0];
        Assert.Equal(2, thinking.GetProperty("thinking").GetArrayLength());
        Assert.Equal("signed", thinking.GetProperty("signature").GetString());
        Assert.True(thinking.GetProperty("closed").GetBoolean());
    }
}
