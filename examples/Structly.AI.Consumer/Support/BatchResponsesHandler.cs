using Structly.AI.Testing;
using System.Net;
using System.Text.Json;

sealed class BatchResponsesHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        var embedding = path.Contains("embedding", StringComparison.Ordinal);
        string json;
        if(path.Contains("batches", StringComparison.Ordinal))
            json = JsonSerializer.Serialize(new { id = embedding ? "embedding-job" : "response-job", status = "completed", input_file_id = "input", endpoint = embedding ? "/v1/embeddings" : "/v1/responses", output_file_id = embedding ? "embedding-output" : "response-output" });
        else
        {
            var body = embedding ? ResponseEnvelopes.Embeddings([new float[] { 1, 2 }], "offline-embedding", new() { TotalTokens = 3 }) : ResponseEnvelopes.CompletedText("{\"value\":\"answer\"}", "offline", usage: new() { TotalTokens = 5 });
            json = JsonSerializer.Serialize(new { custom_id = embedding ? "embedding-item" : "response-item", response = new { status_code = 200, body } }) + "\n";
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
    }
}
