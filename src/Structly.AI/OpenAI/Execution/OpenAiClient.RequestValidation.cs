namespace Structly.AI.OpenAI;

public sealed partial class OpenAiClient
{
    static void ValidateCommonRequest(StructuredRequest request)
    {
        if(request.OpenAi is null || request.TotalTimeout is { } total && !OpenAiExecution.ValidTimeout(total))
            throw new ArgumentException("Invalid deadlines/provider options.");

        if(request.OpenAi.IdempotencyKey is { } key && (String.IsNullOrWhiteSpace(key) || key.Any(c => c < 32 || c > 126)))
            throw new ArgumentException("Invalid idempotency header.");

        if(request.OpenAi.Metadata is { } metadata && (metadata.Count > 16 || metadata.Any(x =>
            String.IsNullOrWhiteSpace(x.Key) || x.Key.Length > 64 || x.Value is null || x.Value.Length > 512)))
            throw new ArgumentException("Invalid provider metadata.");
    }

}
