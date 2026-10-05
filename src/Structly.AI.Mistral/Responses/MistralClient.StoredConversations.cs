using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Structly.AI.Mistral;

public sealed partial class MistralClient
{
    sealed record StoredPosition(string ConversationId, string EntryId, string Configuration);

    Task<StructuredResult<T>> RunStored<T>(BoundOutput<T> output, StructuredRequest request,
        Func<CancellationToken, ValueTask<string?>>? credentials, IReadOnlyList<JsonElement> history, StoredPosition? position,
        Action<StoredPosition, JsonElement> retain, CancellationToken caller)
        => RunOperation(request, "Structured", async execution =>
        {
            if(request.Messages?.Any(x => x is null || x.Role != MessageRole.User) == true)
                throw new ArgumentException("Conversation turns require user messages.");

            var model = SelectModel(request.ModelSelection!);
            execution.Metadata = execution.Metadata with { RequestedModel = model.ModelId };
            var chat = ChatPayload(request, model, request.Instructions, output);
            var instructions = String.Join("\n\n", new[] { request.Instructions, output.Guidance }.Where(x => x is not null));
            var configuration = JsonSerializer.Serialize(new { model = model.ModelId, instructions });
            var inputs = Messages(request).Select(x => ConversationEntry(JsonSerializer.SerializeToElement(x))).ToList();
            var arguments = new Dictionary<string, object?> { ["response_format"] = chat["response_format"] };
            if(chat.TryGetValue("max_tokens", out var maximum))
                arguments["max_tokens"] = maximum;

            if(chat.TryGetValue("reasoning_effort", out var effort))
                arguments["reasoning_effort"] = effort;

            var payload = new Dictionary<string, object?> { ["inputs"] = inputs, ["completion_args"] = arguments, ["store"] = true, ["stream"] = request.Stream };
            var path = "conversations";
            if(position is not null && position.Configuration == configuration)
            {
                path += "/" + RemoteId(position.ConversationId) + "/restart";
                payload["from_entry_id"] = RemoteId(position.EntryId);
            }
            else
            {
                inputs.InsertRange(0, history.Select(ConversationEntry));
                payload["model"] = model.ModelId;
                payload["instructions"] = instructions;
            }

            return await Send(execution, request, credentials, HttpMethod.Post, path, JsonContent.Create(payload), async response =>
            {
                JsonElement entry;
                if(request.Stream)
                    entry = await ReadStoredStream(response, request, execution).ConfigureAwait(false);
                else
                {
                    using var document = await ReadEnvelope(response, execution).ConfigureAwait(false);
                    var root = document.RootElement;
                    execution.Metadata = execution.Metadata with
                    {
                        ConversationId = StringValue(root, "conversation_id"),
                        Usage = ReadUsage(root, execution.Warnings),
                    };
                    var outputs = root.GetProperty("outputs");
                    if(outputs.ValueKind != JsonValueKind.Array || outputs.GetArrayLength() != 1)
                        throw new JsonException();

                    entry = outputs[0].Clone();
                }

                if(StringValue(entry, "type") != "message.output" || StringValue(entry, "role") != "assistant" ||
                    String.IsNullOrWhiteSpace(execution.Metadata.ConversationId) || String.IsNullOrWhiteSpace(StringValue(entry, "id")))
                    throw new JsonException();

                execution.Metadata = execution.Metadata with { ResponseId = StringValue(entry, "id"), ResolvedModel = StringValue(entry, "model") };
                var text = ReadContent(entry.GetProperty("content"));
                var result = String.IsNullOrWhiteSpace(text) ? execution.Failure<T>(StructuredErrorKind.InvalidResponse) : output.ReadOutput(text, execution.Metadata);
                if(request.Stream)
                    await execution.Progress(new() { Kind = StructuredProgressKind.Completed, ResponseId = execution.Metadata.ResponseId, ModelId = execution.Metadata.ResolvedModel }).ConfigureAwait(false);

                if(result.IsSuccess)
                    retain(new(execution.Metadata.ConversationId!, execution.Metadata.ResponseId!, configuration), entry);

                return result;
            }).ConfigureAwait(false);
        }, caller, output.SchemaName);

    static JsonElement ConversationEntry(JsonElement message)
    {
        if(StringValue(message, "object") == "entry")
            return message;

        var role = StringValue(message, "role");
        return JsonSerializer.SerializeToElement(new
        {
            @object = "entry",
            type = role == "assistant" ? "message.output" : "message.input",
            role,
            content = message.GetProperty("content"),
        });
    }

    async Task<JsonElement> ReadStoredStream(HttpResponseMessage response, StructuredRequest request, MistralExecution execution)
    {
        var chunks = new MistralContentAccumulator();
        string? entryId = null;
        string? model = null;
        var started = false;
        await execution.Progress(new() { Kind = StructuredProgressKind.Started }).ConfigureAwait(false);
        var done = await ReadSse(response, request, execution, async frame =>
        {
            using var document = JsonDocument.Parse(frame);
            var root = document.RootElement;
            switch(StringValue(root, "type"))
            {
                case "conversation.response.started":
                    if(started)
                        throw new JsonException();

                    started = true;
                    execution.Metadata = execution.Metadata with { ConversationId = StringValue(root, "conversation_id") };
                    break;
                case "message.output.delta":
                    var id = StringValue(root, "id");
                    if(!started || String.IsNullOrWhiteSpace(id) || entryId is not null && entryId != id ||
                        Property(root, "output_index") is { ValueKind: JsonValueKind.Number } index && index.GetInt32() != 0)
                        throw new JsonException();

                    entryId = id;
                    model = StringValue(root, "model") ?? model;
                    execution.Metadata = execution.Metadata with { ResponseId = id, ResolvedModel = model };
                    var content = root.GetProperty("content");
                    if(content.ValueKind == JsonValueKind.Object)
                        content = JsonSerializer.SerializeToElement(new[] { content });

                    var text = ReadContent(content);
                    chunks.Add(content);
                    if(text is { Length: > 0 })
                    {
                        execution.StopInactivity();
                        await execution.Progress(new() { Kind = StructuredProgressKind.OutputTextDelta, TextDelta = text, OutputIndex = 0, ResponseId = id, ModelId = model }).ConfigureAwait(false);
                        execution.ResetInactivity(request.InactivityTimeout ?? _inactivityTimeout);
                    }

                    break;
                case "conversation.response.done":
                    execution.Metadata = execution.Metadata with { Usage = ReadUsage(root, execution.Warnings) };
                    return true;
                case "conversation.response.error":
                    throw new JsonException();
                case "tool.execution.started" or "tool.execution.delta" or "tool.execution.done" or "function.call.delta" or "agent.handoff.started" or "agent.handoff.done":
                    throw new JsonException();
            }

            return false;
        }).ConfigureAwait(false);
        if(!done || !started || entryId is null)
            throw new JsonException();

        var entry = JsonNode.Parse(chunks.CreateAssistantMessage().GetRawText())!.AsObject();
        entry["object"] = "entry";
        entry["type"] = "message.output";
        entry["id"] = entryId;
        entry["model"] = model;
        return JsonSerializer.SerializeToElement(entry);
    }
}
