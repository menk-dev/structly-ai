using System.Text;
using System.Text.Json;

namespace Structly.AI.Mistral;

public sealed partial class MistralClient
{
    async Task<StructuredResult<T>> ReadChatStream<T>(HttpResponseMessage response, StructuredRequest request,
        BoundOutput<T>? output, MistralExecution execution)
    {
        var text = new StringBuilder();
        string? finish = null;
        var terminal = false;
        await Progress(StructuredProgressKind.Started).ConfigureAwait(false);
        var done = await ReadSse(response, request, execution, async frame =>
        {
            if(frame == "[DONE]")
                return true;

            using var document = JsonDocument.Parse(frame);
            var root = document.RootElement;
            execution.Metadata = execution.Metadata with
            {
                ResponseId = StringValue(root, "id") ?? execution.Metadata.ResponseId,
                ResolvedModel = StringValue(root, "model") ?? execution.Metadata.ResolvedModel,
                Usage = ReadUsage(root, execution.Warnings) ?? execution.Metadata.Usage,
            };
            var choices = root.GetProperty("choices");
            if(choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() > 1)
                throw new JsonException();

            if(choices.GetArrayLength() == 0)
                return false;

            var choice = choices[0];
            if(choice.GetProperty("index").GetInt32() != 0 || terminal)
                throw new JsonException();

            var delta = choice.GetProperty("delta");
            var fragment = delta.TryGetProperty("content", out var content) ? ReadContent(content) : null;
            if(fragment is { Length: > 0 })
            {
                text.Append(fragment);
                execution.StopInactivity();
                await Progress(StructuredProgressKind.OutputTextDelta, fragment).ConfigureAwait(false);
                execution.ResetInactivity(request.InactivityTimeout ?? _inactivityTimeout);
            }

            finish = StringValue(choice, "finish_reason");
            terminal = finish is not null;
            return false;
        }).ConfigureAwait(false);

        if(!done || !terminal)
            return execution.Failure<T>(StructuredErrorKind.InvalidResponse);

        var result = FinishChat(text.ToString(), finish, request, output, execution);
        await Progress(StructuredProgressKind.Completed).ConfigureAwait(false);
        return result;

        ValueTask Progress(StructuredProgressKind kind, string? fragment = null)
            => execution.Progress(new()
            {
                Kind = kind,
                TextDelta = fragment,
                OutputIndex = kind == StructuredProgressKind.OutputTextDelta ? 0 : null,
                ResponseId = execution.Metadata.ResponseId,
                ModelId = execution.Metadata.ResolvedModel,
            });
    }
}
