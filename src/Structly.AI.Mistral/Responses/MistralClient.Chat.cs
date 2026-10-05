using System.Text;
using System.Text.Json;

namespace Structly.AI.Mistral;

public sealed partial class MistralClient
{
    static Dictionary<string, object?> ChatPayload<T>(StructuredRequest request, ModelSelection model, string? instructions, BoundOutput<T>? output)
    {
        if(request.GetType() != typeof(StructuredRequest) && request is not MistralRequest || request.IncludeReasoningSummary ||
            request.MaxOutputTokens is <= 0 || request.InactivityTimeout is { } inactivity && (!request.Stream || inactivity <= TimeSpan.Zero) ||
            request.Progress is not null && !request.Stream || request is MistralRequest { CaptureRawResponse: true, Stream: true } ||
            output is null && (request.Vocabularies is not null || request.OutputSpecification is not null) ||
            instructions is not null && String.IsNullOrWhiteSpace(instructions) || output is not null && String.IsNullOrWhiteSpace(instructions))
            throw new ArgumentException("Invalid chat settings.");

        var messages = Messages(request);
        if(instructions is not null || output?.Guidance is not null)
            messages.Insert(0, new { role = "system", content = String.Join("\n\n", new[] { instructions, output?.Guidance }.Where(x => x is not null)) });

        var payload = new Dictionary<string, object?> { ["model"] = model.ModelId, ["messages"] = messages, ["stream"] = request.Stream };
        if(request.MaxOutputTokens is { } maximum)
            payload["max_tokens"] = maximum;

        if(model.ReasoningEffort is { } effort)
            payload["reasoning_effort"] = effort.ToString().ToLowerInvariant();

        if(output is not null)
            payload["response_format"] = new { type = "json_schema", json_schema = new { name = output.SchemaName, description = output.Task.Description, schema = output.CreateSchema(), strict = true } };

        if(request is MistralRequest advanced)
        {
            if(advanced.PromptCacheKey is { } key)
            {
                if(String.IsNullOrWhiteSpace(key))
                    throw new ArgumentException("Cache key must be nonblank.");

                payload["prompt_cache_key"] = key;
            }

            if(advanced.Metadata is { } metadata)
            {
                if(metadata.Any(x => String.IsNullOrWhiteSpace(x.Key) || x.Value is null))
                    throw new ArgumentException("Invalid metadata.");

                payload["metadata"] = new Dictionary<string, string>(metadata);
            }
        }

        return payload;
    }

    static List<object> Messages(StructuredRequest request)
    {
        if((request.Input is null) == (request.Messages is null))
            throw new ArgumentException("Select input or messages.");

        if(request.Input is { } input)
            return !String.IsNullOrWhiteSpace(input) ? [new { role = "user", content = input }] : throw new ArgumentException("Input must be nonblank.");

        var messages = new List<object>();
        foreach(var message in request.Messages!)
        {
            if(message is null || message.Content is null || message.Content.Count == 0 ||
                message.Role is not (MessageRole.System or MessageRole.User or MessageRole.Assistant))
                throw new ArgumentException("Unsupported message.");

            var parts = new List<object>();
            foreach(var part in message.Content)
            {
                if(part is null || part.CacheBreakpoint)
                    throw new ArgumentException("Cache breakpoints are unsupported.");

                if(part is TextPart text && !String.IsNullOrWhiteSpace(text.Text))
                    parts.Add(new { type = "text", text = text.Text });
                else if(part is ImagePart image && message.Role == MessageRole.User)
                {
                    ValidateImage(image);
                    parts.Add(new { type = "image_url", image_url = new { url = image.Url, detail = image.Detail.ToString().ToLowerInvariant() } });
                }
                else
                    throw new ArgumentException("Unsupported content part.");
            }

            messages.Add(new { role = message.Role.ToString().ToLowerInvariant(), content = parts });
        }

        return messages.Count > 0 ? messages : throw new ArgumentException("Messages must not be empty.");
    }

    static void ValidateImage(ImagePart image)
    {
        if(!Enum.IsDefined(image.Detail) || String.IsNullOrWhiteSpace(image.Url))
            throw new ArgumentException("Invalid image.");

        if(image.Url.StartsWith("data:", StringComparison.Ordinal))
        {
            var comma = image.Url.IndexOf(',');
            if(comma < 0 || image.Url[..comma] is not ("data:image/png;base64" or "data:image/jpeg;base64" or "data:image/webp;base64" or "data:image/gif;base64"))
                throw new ArgumentException("Invalid image data URL.");

            try
            {
                if(Convert.FromBase64String(image.Url[(comma + 1)..]).Length == 0)
                    throw new ArgumentException("Empty image data.");
            }
            catch(FormatException exception)
            {
                throw new ArgumentException("Invalid image data.", exception);
            }
        }
        else if(!Uri.TryCreate(image.Url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length > 0 || uri.Fragment.Length > 0)
            throw new ArgumentException("Expected an HTTPS image URL.");
    }

    static string? ReadContent(JsonElement content)
    {
        if(content.ValueKind == JsonValueKind.String)
            return content.GetString();

        if(content.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;

        if(content.ValueKind != JsonValueKind.Array)
            throw new JsonException();

        var text = new StringBuilder();
        foreach(var chunk in content.EnumerateArray())
        {
            switch(StringValue(chunk, "type"))
            {
                case "text":
                    text.Append(StringValue(chunk, "text") ?? throw new JsonException());
                    break;
                case "thinking":
                    if(Property(chunk, "thinking").ValueKind != JsonValueKind.Array)
                        throw new JsonException();

                    break;
                default:
                    throw new JsonException();
            }
        }

        return text.ToString();
    }

    static StructuredResult<T> FinishChat<T>(string? text, string? finish, StructuredRequest request, BoundOutput<T>? output, MistralExecution execution)
    {
        if(request is MistralRequest { CaptureOutputText: true })
            execution.Metadata = execution.Metadata with { OutputText = text };

        return finish is "length" or "model_length" ? execution.Failure<T>(StructuredErrorKind.IncompleteOutput)
            : finish == "error" ? execution.Failure<T>(StructuredErrorKind.ProviderRejected)
            : finish != "stop" || String.IsNullOrWhiteSpace(text) ? execution.Failure<T>(StructuredErrorKind.InvalidResponse)
            : output is not null ? output.ReadOutput(text, execution.Metadata)
            : StructuredResult<T>.Success((T)(object)text, execution.Metadata);
    }

    static StructuredResult<T> ReadChat<T>(JsonElement root, StructuredRequest request, BoundOutput<T>? output, MistralExecution execution)
    {
        var choices = root.GetProperty("choices");
        if(choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() != 1)
            return execution.Failure<T>(StructuredErrorKind.InvalidResponse);

        var choice = choices[0];
        execution.AssistantMessage = choice.GetProperty("message").Clone();
        return FinishChat(ReadContent(choice.GetProperty("message").GetProperty("content")), StringValue(choice, "finish_reason"), request, output, execution);
    }
}
