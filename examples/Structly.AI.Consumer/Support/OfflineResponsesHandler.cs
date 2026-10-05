using Structly.AI.Testing;
using System.Text.Json;

sealed class OfflineResponsesHandler : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if(request.RequestUri?.AbsolutePath != "/v1/responses")
            throw new InvalidOperationException("Unexpected endpoint.");

        using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
        if(body.RootElement.GetProperty("text").GetProperty("format").GetProperty("strict").GetBoolean() != true)
            throw new InvalidOperationException("The consumer must send a strict schema.");

        var output = JsonSerializer.Serialize(new { summary = "Duplicate charge", queue = "billing", reference = "INV-42" });
        return ResponseEnvelopes.ToHttpResponse(ResponseEnvelopes.CompletedText(output, "offline-fixture-model", "resp_offline", new() { InputTokens = 12, OutputTokens = 8, TotalTokens = 20 }));
    }
}
