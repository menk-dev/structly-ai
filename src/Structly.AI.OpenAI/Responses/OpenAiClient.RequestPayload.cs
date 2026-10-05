namespace Structly.AI.OpenAI;

public sealed partial class OpenAiClient
{
    static void ValidateResponseRequest(StructuredRequest request, bool prewarm = false)
    {
        ValidateCommonRequest(request);
        if(request.MaxOutputTokens is <= 0 || request.InactivityTimeout is { } idle && idle <= TimeSpan.Zero ||
            !request.Stream && (request.InactivityTimeout is not null || request.Progress is not null || request.IncludeReasoningSummary))
            throw new ArgumentException("Invalid response controls.");

        if(prewarm && (request.Stream || request.Progress is not null || request.IncludeReasoningSummary || request.MaxOutputTokens is not null ||
            ResponseOptions(request).PreviousResponseId is not null || request.OutputSpecification is not null || ResponseOptions(request).CaptureOutputText || ResponseOptions(request).Store))
            throw new ArgumentException("Prewarm does not generate output or continue/store a conversation.");

        if(ResponseOptions(request).PreviousResponseId is { } previous && String.IsNullOrWhiteSpace(previous) ||
            ResponseOptions(request).PromptCacheKey is { } cacheKey && String.IsNullOrWhiteSpace(cacheKey))
            throw new ArgumentException("Response/cache IDs must be nonblank.");
    }

    Dictionary<string, object?> ResponsePayload(StructuredRequest request, ModelSelection model, string? instructions,
        string? guidance = null, bool prewarm = false)
    {
        if(instructions is not null && String.IsNullOrWhiteSpace(instructions))
            throw new ArgumentException("Instructions must be nonblank.");

        var (input, breakpoints) = CreateInput(request, guidance);
        var payload = new Dictionary<string, object?>
        {
            ["model"] = model.ModelId,
            ["input"] = input,
            ["store"] = ResponseOptions(request).Store,
            ["stream"] = request.Stream,
        };
        if(instructions is not null)
            payload["instructions"] = instructions;

        if(request.MaxOutputTokens is { } cap)
            payload["max_output_tokens"] = cap;

        if(model.ReasoningEffort is not null || request.IncludeReasoningSummary)
        {
            var reasoning = new Dictionary<string, object?>();
            if(model.ReasoningEffort is { } effort)
                reasoning["effort"] = effort.ToString().ToLowerInvariant();

            if(request.IncludeReasoningSummary)
                reasoning["summary"] = "auto";

            payload["reasoning"] = reasoning;
        }

        if(ResponseOptions(request).Metadata is { } metadata)
            payload["metadata"] = metadata.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);

        if(ResponseOptions(request).PreviousResponseId is { } previous)
            payload["previous_response_id"] = previous;

        if(ResponseOptions(request).PromptCacheKey is { } key)
            payload["prompt_cache_key"] = key;

        ApplyCache(payload, ResponseOptions(request), model.ModelId!, breakpoints, prewarm);
        return payload;
    }

    static (object Input, int Breakpoints) CreateInput(StructuredRequest request, string? guidance)
    {
        if((request.Input is null) == (request.Messages is null) || request.Input is { } input && String.IsNullOrWhiteSpace(input))
            throw new ArgumentException("Supply exactly one of nonblank Input or nonempty Messages.");

        // String shorthand is the provider's equivalent of one user text message.
        if(request.Input is { } shorthand && String.IsNullOrEmpty(guidance))
            return (shorthand, 0);

        var source = request.Messages?.ToArray() ?? [new(MessageRole.User, [new TextPart(request.Input!)])];
        if(source.Length == 0)
            throw new ArgumentException("Messages must be nonempty.");

        var messages = new List<Dictionary<string, object?>>();
        var breakpoints = 0;
        List<Dictionary<string, object?>>? lastUser = null;
        foreach(var message in source)
        {
            if(message is null || !Enum.IsDefined(message.Role) || message.Content is null)
                throw new ArgumentException("Invalid message.");

            var content = new List<Dictionary<string, object?>>();
            foreach(var part in message.Content.ToArray())
            {
                Dictionary<string, object?> block;
                if(part is TextPart text && !String.IsNullOrWhiteSpace(text.Text))
                    block = new() { ["type"] = message.Role == MessageRole.Assistant ? "output_text" : "input_text", ["text"] = text.Text };
                else if(part is ImagePart image && message.Role == MessageRole.User && Enum.IsDefined(image.Detail) && ValidImageUrl(image.Url))
                    block = new() { ["type"] = "input_image", ["image_url"] = image.Url, ["detail"] = image.Detail.ToString().ToLowerInvariant() };
                else
                    throw new ArgumentException("Invalid content: use nonblank text or user HTTPS/base64 images.");

                if(part.CacheBreakpoint)
                {
                    if(message.Role == MessageRole.Assistant)
                        throw new ArgumentException("Assistant output cannot mark a cache breakpoint.");

                    breakpoints++;
                    block["prompt_cache_breakpoint"] = new { mode = "explicit" };
                }

                content.Add(block);
            }

            if(content.Count == 0)
                throw new ArgumentException("Message content must be nonempty.");

            if(message.Role == MessageRole.User)
                lastUser = content;

            messages.Add(new() { ["role"] = message.Role.ToString().ToLowerInvariant(), ["content"] = content });
        }

        if(!String.IsNullOrEmpty(guidance))
        {
            if(lastUser is null)
                throw new ArgumentException("Output guidance requires a user message.");

            lastUser.Add(new() { ["type"] = "input_text", ["text"] = guidance });
        }

        return (messages, breakpoints);
    }

    static bool ValidImageUrl(string? url)
    {
        if(url is null)
            return false;

        if(url.StartsWith("data:", StringComparison.Ordinal))
        {
            var comma = url.IndexOf(',');
            if(comma < 0 || url[..comma] is not ("data:image/png;base64" or "data:image/jpeg;base64" or "data:image/webp;base64" or "data:image/gif;base64"))
                return false;

            try
            {
                return Convert.FromBase64String(url[(comma + 1)..]).Length > 0;
            }
            catch(FormatException)
            {
                return false;
            }
        }

        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.Host.Length > 0 &&
            uri.UserInfo.Length == 0 && uri.Fragment.Length == 0;
    }

}
