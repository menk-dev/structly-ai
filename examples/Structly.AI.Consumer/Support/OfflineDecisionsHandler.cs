using System.Net;

sealed class OfflineDecisionsHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if(request.RequestUri?.AbsolutePath != "/v1/decisions")
            throw new InvalidOperationException("Expected a decision request.");

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                {"model":"offline-decision","answers":[
                  {"name":"Department","type":"choice","choice":"billing","confidence":0.9,
                   "probabilities":[{"value":"billing","probability":0.95},{"value":"Technical","probability":0.05}]},
                  {"name":"Blocking","type":"predicate","probability":0.1},
                  {"name":"Team","type":"choice","choice":"payments","confidence":0.8,
                   "probabilities":[{"value":"payments","probability":0.9},{"value":"support","probability":0.1}]}
                ],"usage":{"input_tokens":42,"output_tokens":0,"total_tokens":42}}
                """),
        });
    }
}
