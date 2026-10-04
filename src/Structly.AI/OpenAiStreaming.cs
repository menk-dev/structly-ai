using System.Text;
using System.Text.Json;

namespace Structly.AI.OpenAI;

public sealed partial class OpenAiClient
{
    async Task<JsonDocument> ReadEvents(Stream stream, StructuredRequest request, OpenAiExecution execution)
    {
        var idle = request.InactivityTimeout ?? _options.InactivityTimeout;
        execution.StartInactivity(idle);
        using var line = new MemoryStream();
        var data = new StringBuilder();
        string? eventName = null;
        var utf8 = new UTF8Encoding(false, true);
        var chunk = new byte[4096];
        long bytes = 0;
        var afterCr = false;
        var firstLine = true;
        try
        {
            while (true)
            {
                var count = await execution.Await(stream.ReadAsync(chunk, execution.Token).AsTask()).ConfigureAwait(false);
                if (count == 0) throw new JsonException("Missing terminal event.");
                bytes += count;
                if (bytes > _options.MaxResponseBytes) throw new JsonException("Stream exceeds size ceiling.");
                for (var index = 0; index < count; index++)
                {
                    var value = chunk[index];
                    if (afterCr && value == 10) { afterCr = false; continue; }
                    afterCr = value == 13;
                    if (value is not (10 or 13)) { line.WriteByte(value); continue; }
                    execution.ResetInactivity(idle);
                    var text = utf8.GetString(line.GetBuffer(), 0, (int)line.Length);
                    line.SetLength(0);
                    if (firstLine && text.StartsWith('\uFEFF')) text = text[1..];
                    firstLine = false;
                    if (text.Length != 0)
                    {
                        if (text.StartsWith(':')) continue;
                        var colon = text.IndexOf(':');
                        var field = colon < 0 ? text : text[..colon];
                        var fieldValue = colon < 0 ? "" : text[(colon + 1)..];
                        if (fieldValue.StartsWith(' ')) fieldValue = fieldValue[1..];
                        if (field == "data") data.Append(fieldValue).Append('\n');
                        else if (field == "event") eventName = fieldValue;
                        continue;
                    }
                    if (data.Length == 0) { eventName = null; continue; }
                    using var document = JsonDocument.Parse(data.ToString());
                    data.Clear();
                    var root = document.RootElement;
                    var type = Text(root, "type");
                    if (String.IsNullOrWhiteSpace(type) || eventName is not null && eventName != type)
                        throw new JsonException("Invalid event type.");
                    eventName = null;
                    if (type is "response.created" or "response.in_progress")
                    {
                        var envelope = Property(root, "response");
                        if (envelope.ValueKind != JsonValueKind.Object) throw new JsonException("Invalid response event.");
                        execution.Metadata = execution.Metadata with
                        {
                            ResponseId = Text(envelope, "id") ?? execution.Metadata.ResponseId,
                            ResolvedModel = Text(envelope, "model") ?? execution.Metadata.ResolvedModel
                        };
                    }
                    else if (type is "response.completed" or "response.incomplete" or "response.failed")
                    {
                        var envelope = Property(root, "response");
                        if (Text(envelope, "status") != type[9..]) throw new JsonException("Invalid terminal status.");
                        execution.StopInactivity();
                        return JsonDocument.Parse(envelope.GetRawText());
                    }
                    else if (type is "response.output_text.delta" or "response.reasoning_summary_text.delta")
                    {
                        var delta = Text(root, "delta");
                        var outputIndex = Property(root, "output_index");
                        if (delta is null || outputIndex.ValueKind != JsonValueKind.Number || !outputIndex.TryGetInt32(out var output) || output < 0)
                            throw new JsonException("Invalid delta.");
                        int? summary = null;
                        if (type == "response.reasoning_summary_text.delta")
                        {
                            var summaryIndex = Property(root, "summary_index");
                            if (summaryIndex.ValueKind != JsonValueKind.Number || !summaryIndex.TryGetInt32(out var part) || part < 0) throw new JsonException("Invalid summary index.");
                            summary = part;
                            if (!request.IncludeReasoningSummary) continue;
                        }
                        await execution.Progress(new()
                        {
                            Kind = summary is null ? StructuredProgressKind.OutputTextDelta : StructuredProgressKind.ReasoningSummaryDelta,
                            TextDelta = delta,
                            OutputIndex = output,
                            SummaryIndex = summary,
                            ResponseId = execution.Metadata.ResponseId,
                            ModelId = execution.Metadata.ResolvedModel
                        }).ConfigureAwait(false);
                        execution.Token.ThrowIfCancellationRequested();
                    }
                    else if (type == "error") throw new JsonException("Stream error without terminal response.");
                    // Ancillary and unknown events, including raw reasoning, are never exposed.
                }
            }
        }
        catch (DecoderFallbackException) { throw new JsonException("Invalid UTF-8 event."); }
        finally { execution.StopInactivity(); }
    }
}
