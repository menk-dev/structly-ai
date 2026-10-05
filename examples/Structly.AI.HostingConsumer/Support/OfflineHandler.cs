using Structly.AI.Testing;

sealed class OfflineHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(ResponseEnvelopes.ToHttpResponse(ResponseEnvelopes.CompletedText("{\"value\":\"hosted\"}")));
}
