using Structly.AI.Mistral;
using System.Text.Json;
using static Structly.AI.Tests.MistralFixture;

namespace Structly.AI.Tests;

public sealed class MistralConversationTests
{
    const string _envelope = """{"conversation_id":"conversation","outputs":[{"object":"entry","type":"message.output","role":"assistant","id":"entry","model":"resolved","content":"{\"value\":\"answer\"}"}],"usage":{"prompt_tokens":3,"completion_tokens":4,"total_tokens":7}}""";

    [Fact]
    public async Task Stored_turns_restart_from_successful_entry_and_branches_keep_independent_positions()
    {
        var calls = 0;
        using var handler = new Handler(_ => Handler.Response(_envelope.Replace("\"entry\",\"model\"", "\"entry" + ++calls + "\",\"model\"")));
        using var http = new HttpClient(handler);
        var task = StructuredTask.Create<Answer>("Extract");
        var conversation = new MistralClient(http, Options()).CreateConversation(task);
        var first = await conversation.ExecuteAsync(new() { Input = "first" }, TestContext.Current.CancellationToken);
        Assert.Equal("answer", first.EnsureSuccess().Value);
        Assert.Equal("conversation", first.Metadata.ConversationId);
        Assert.Equal("entry1", first.Metadata.ResponseId);
        Assert.Equal(7, first.Metadata.Usage!.TotalTokens);
        var branch = conversation.ChangeOutput(task.BindOutput());
        Assert.True((await conversation.ExecuteAsync(new() { Input = "second" }, TestContext.Current.CancellationToken)).IsSuccess);
        Assert.True((await branch.ExecuteAsync(new() { Input = "branch" }, TestContext.Current.CancellationToken)).IsSuccess);
        Assert.Equal("/v1/conversations", handler.Uris[0].AbsolutePath);
        using var firstPayload = JsonDocument.Parse(handler.Payloads[0]);
        Assert.True(firstPayload.RootElement.GetProperty("store").GetBoolean());
        Assert.Equal("/v1/conversations/conversation/restart", handler.Uris[1].AbsolutePath);
        using var payload = JsonDocument.Parse(handler.Payloads[2]);
        Assert.Equal("entry1", payload.RootElement.GetProperty("from_entry_id").GetString());
        Assert.True(payload.RootElement.GetProperty("store").GetBoolean());
        Assert.Equal("json_schema", payload.RootElement.GetProperty("completion_args").GetProperty("response_format").GetProperty("type").GetString());
    }

    [Fact]
    public async Task Configuration_changes_replay_history_into_a_new_stored_conversation()
    {
        using var handler = new Handler(_envelope);
        using var http = new HttpClient(handler);
        var conversation = new MistralClient(http, Options()).CreateConversation(StructuredTask.Create<Answer>("Extract"));
        Assert.True((await conversation.ExecuteAsync(new() { Input = "first" }, TestContext.Current.CancellationToken)).IsSuccess);
        var branch = conversation.ChangeConfiguration(new() { Instructions = "Correct", ModelSelection = new() { ModelId = "other" } });
        Assert.True((await branch.ExecuteAsync(new() { Input = "second" }, TestContext.Current.CancellationToken)).IsSuccess);
        using var payload = JsonDocument.Parse(handler.Payloads[1]);
        Assert.Equal("/v1/conversations", handler.Uris[1].AbsolutePath);
        Assert.Equal("Correct", payload.RootElement.GetProperty("instructions").GetString());
        Assert.Equal("other", payload.RootElement.GetProperty("model").GetString());
        Assert.Equal(3, payload.RootElement.GetProperty("inputs").GetArrayLength());
        Assert.Equal("message.output", payload.RootElement.GetProperty("inputs")[1].GetProperty("type").GetString());
    }

    [Fact]
    public async Task Streaming_stored_turns_use_native_events_and_report_final_usage()
    {
        const string stream = """
            event: conversation.response.started
            data: {"type":"conversation.response.started","conversation_id":"conversation"}

            event: message.output.delta
            data: {"type":"message.output.delta","id":"entry","model":"resolved","output_index":0,"content":{"type":"text","text":"{\"value\":\"answer\"}"}}

            event: conversation.response.done
            data: {"type":"conversation.response.done","usage":{"prompt_tokens":3,"completion_tokens":4,"total_tokens":7}}


            """;
        using var handler = new Handler(stream);
        using var http = new HttpClient(handler);
        var conversation = new MistralClient(http, Options()).CreateConversation(StructuredTask.Create<Answer>("Extract"));
        var progress = new List<StructuredProgress>();
        var result = await conversation.ExecuteAsync(new()
        {
            Input = "first",
            Stream = true,
            Progress = (item, _) =>
            {
                progress.Add(item);
                return ValueTask.CompletedTask;
            },
        }, TestContext.Current.CancellationToken);
        Assert.Equal("answer", result.EnsureSuccess().Value);
        Assert.Equal("conversation", result.Metadata.ConversationId);
        Assert.Equal("entry", result.Metadata.ResponseId);
        Assert.Equal(7, result.Metadata.Usage!.TotalTokens);
        Assert.Equal(new[] { StructuredProgressKind.Started, StructuredProgressKind.OutputTextDelta, StructuredProgressKind.Completed }, progress.Select(x => x.Kind));
    }

    [Fact]
    public async Task Invalid_stored_output_keeps_usage_and_does_not_advance()
    {
        var calls = 0;
        using var handler = new Handler(_ => Handler.Response(++calls == 2 ? _envelope.Replace("{\\\"value\\\":\\\"answer\\\"}", "{}") : _envelope));
        using var http = new HttpClient(handler);
        var conversation = new MistralClient(http, Options()).CreateConversation(StructuredTask.Create<Answer>("Extract"));
        Assert.True((await conversation.ExecuteAsync(new() { Input = "first" }, TestContext.Current.CancellationToken)).IsSuccess);
        var failed = await conversation.ExecuteAsync(new() { Input = "failed" }, TestContext.Current.CancellationToken);
        Assert.Equal(StructuredErrorKind.InvalidOutput, failed.Error!.Kind);
        Assert.Equal(7, failed.Metadata.Usage!.TotalTokens);
        Assert.True((await conversation.ExecuteAsync(new() { Input = "third" }, TestContext.Current.CancellationToken)).IsSuccess);
        using var payload = JsonDocument.Parse(handler.Payloads[2]);
        Assert.Equal("entry", payload.RootElement.GetProperty("from_entry_id").GetString());
    }

    [Fact]
    public async Task Streamed_thinking_preserves_its_signature_when_configuration_changes_replay_history()
    {
        const string stream = """
            data: {"type":"conversation.response.started","conversation_id":"conversation"}

            data: {"type":"message.output.delta","id":"entry","model":"resolved","content":{"type":"thinking","thinking":[{"type":"text","text":"first"}],"closed":false}}

            data: {"type":"message.output.delta","id":"entry","model":"resolved","content":[{"type":"thinking","thinking":[{"type":"text","text":"second"}],"signature":"signed","closed":true},{"type":"text","text":"{\"value\":\"answer\"}"}]}

            data: {"type":"conversation.response.done"}


            """;
        var calls = 0;
        using var handler = new Handler(_ => Handler.Response(++calls == 1 ? stream : _envelope));
        using var http = new HttpClient(handler);
        var conversation = new MistralClient(http, Options()).CreateConversation(StructuredTask.Create<Answer>("Extract").BindOutput());
        Assert.True((await conversation.ExecuteAsync(new() { Input = "first", Stream = true }, TestContext.Current.CancellationToken)).IsSuccess);
        var branch = conversation.ChangeConfiguration(new() { Instructions = "Correct", ModelSelection = new() { ModelId = "other" } });
        Assert.True((await branch.ExecuteAsync(new() { Input = "second" }, TestContext.Current.CancellationToken)).IsSuccess);
        using var payload = JsonDocument.Parse(handler.Payloads[1]);
        var thinking = payload.RootElement.GetProperty("inputs")[1].GetProperty("content")[0];
        Assert.Equal(2, thinking.GetProperty("thinking").GetArrayLength());
        Assert.Equal("signed", thinking.GetProperty("signature").GetString());
        Assert.True(thinking.GetProperty("closed").GetBoolean());
        Assert.Equal("/v1/conversations", handler.Uris[1].AbsolutePath);
    }

    [Fact]
    public async Task Non_user_turns_are_rejected_before_credentials_or_http()
    {
        using var handler = new Handler(_ => throw new InvalidOperationException("Should not send"));
        using var http = new HttpClient(handler);
        var options = Options();
        options.CredentialResolver = _ => throw new InvalidOperationException("Should not resolve");
        var conversation = new MistralClient(http, options).CreateConversation(StructuredTask.Create<Answer>("Extract"));
        foreach(var role in new[] { MessageRole.System, MessageRole.Developer, MessageRole.Assistant })
        {
            var result = await conversation.ExecuteAsync(new() { Messages = [new(role, [new TextPart("input")])] }, TestContext.Current.CancellationToken);
            Assert.Equal(StructuredErrorKind.InvalidRequest, result.Error!.Kind);
        }

        Assert.Empty(handler.Uris);
    }
}
