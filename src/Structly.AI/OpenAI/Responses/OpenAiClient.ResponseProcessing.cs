using System.Text;
using System.Text.Json;

namespace Structly.AI.OpenAI;

public sealed partial class OpenAiClient
{
    static async ValueTask<StructuredResult<T>> ProcessResponse<T>(JsonElement root, StructuredRequest request,
        OpenAiExecution execution, Func<string, StructuredMetadata, StructuredResult<T>> readOutput,
        bool prewarm = false, T? prewarmValue = default)
    {
        StructuredResult<T> Fail(StructuredErrorKind kind, bool transient = false) => LocalFailure<T>(execution, kind, transient);
        if(root.ValueKind != JsonValueKind.Object || String.IsNullOrWhiteSpace(Text(root, "id")) || String.IsNullOrWhiteSpace(Text(root, "model")))
            return Fail(StructuredErrorKind.InvalidResponse);

        var status = Text(root, "status");
        if(prewarm)
        {
            if(ProviderStatusFailure() is { } failure)
                return failure;

            var warmOutput = Property(root, "output");
            if(warmOutput.ValueKind != JsonValueKind.Array || warmOutput.GetArrayLength() != 0)
                return Fail(StructuredErrorKind.InvalidResponse);

            return StructuredResult<T>.Success(prewarmValue!, execution.Metadata, execution.Warnings);
        }

        var output = Property(root, "output");
        // Terminal provider status takes precedence even when no answer is present.
        if(output.ValueKind != JsonValueKind.Array)
            return ProviderStatusFailure() ?? Fail(StructuredErrorKind.InvalidResponse);

        var text = new StringBuilder();
        var answers = 0;
        var refused = false;
        var invalid = false;
        foreach(var item in output.EnumerateArray())
        {
            if(Text(item, "type") == "reasoning")
                continue;

            if(Text(item, "type") != "message" || Text(item, "role") != "assistant" || Text(item, "status") != "completed")
            {
                invalid = true;
                continue;
            }

            answers++;
            var content = Property(item, "content");
            if(content.ValueKind != JsonValueKind.Array)
            {
                invalid = true;
                continue;
            }

            foreach(var part in content.EnumerateArray())
            {
                if(Text(part, "type") == "refusal" && Text(part, "refusal") is not null)
                    refused = true;
                else if(Text(part, "type") == "output_text" && Text(part, "text") is { } value)
                    text.Append(value);
                else
                    invalid = true;
            }
        }

        execution.Metadata = execution.Metadata with { OutputText = request.OpenAi.CaptureOutputText ? text.ToString() : null };
        if(ProviderStatusFailure() is { } statusFailure)
            return statusFailure;

        if(refused)
            return Fail(StructuredErrorKind.Refused);

        if(invalid || answers != 1 || text.Length == 0)
            return Fail(StructuredErrorKind.InvalidResponse);

        execution.CheckCancellation();
        var result = readOutput(text.ToString(), execution.Metadata);
        execution.CheckCancellation();
        if(result.IsSuccess || result.Error?.Kind == StructuredErrorKind.InvalidOutput)
            await execution.Progress(new() { Kind = StructuredProgressKind.Completed, ResponseId = execution.Metadata.ResponseId, ModelId = execution.Metadata.ResolvedModel }).ConfigureAwait(false);

        return result.IsSuccess ? StructuredResult<T>.Success(result.Value!, execution.Metadata, execution.Warnings) : StructuredResult<T>.Failure(result.Error!, execution.Metadata, execution.Warnings);

        StructuredResult<T>? ProviderStatusFailure()
        {
            if(status == "completed")
                return null;

            if(status == "incomplete")
                return Fail(StructuredErrorKind.IncompleteOutput);

            if(status != "failed")
                return Fail(StructuredErrorKind.InvalidResponse);

            var transient = Text(Property(root, "error"), "code") is "server_error" or "rate_limit_exceeded";
            return Fail(transient ? StructuredErrorKind.ProviderUnavailable : StructuredErrorKind.ProviderRejected, transient);
        }
    }

}
