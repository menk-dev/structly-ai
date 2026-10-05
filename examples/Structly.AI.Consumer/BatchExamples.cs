using Structly.AI;
using Structly.AI.OpenAI;
using Structly.AI.Testing;
using System.Net;
using System.Text.Json;

static class BatchExamples
{
    // This in-memory set illustrates the ledger contract. Production applications use a
    // durable unique execution-ID key and commit the usage record in the same transaction.
    public static async Task Run()
    {
        var ledger = new HashSet<Guid>();
        ValueTask Record(StructuredUsageEvent item, CancellationToken token)
        {
            if(ledger.Add(item.Metadata.ExecutionId))
                Console.WriteLine($"Batch usage {item.RequestedModel}: {item.Usage.TotalTokens}");
            return ValueTask.CompletedTask;
        }
        var task = StructuredTask.Create<BatchAnswer>(new() { Instructions = "Extract" });
        using var http = new HttpClient(new Handler());
        var client = new OpenAiClient(http, new() { DefaultModel = new() { ModelId = "offline" }, CredentialResolver = Credentials.FromStatic("offline"), UsageObserver = Record });
        var embedding = client.PrepareEmbeddingBatch([new("embedding-item", new() { Inputs = ["document"], ModelSelection = new() { ModelId = "offline-embedding" }, Dimensions = 2 })]);
        var response = client.PrepareResponseBatch(task, [new("response-item", new() { Input = "document" })]);
        // Persist ToJson() and the remote batch ID in your application; reconstruct on restart.
        var manifest = BatchManifest.FromJson(embedding.Manifest.ToJson());
        for(var i = 0; i < 2; i++)
            if(!(await client.ImportEmbeddingBatchResultsAsync("embedding-job", manifest)).EnsureSuccess()[0].IsSuccess)
                throw new InvalidOperationException("Embedding batch failed.");
        var typed = (await client.ImportResponseBatchResultsAsync("response-job", BatchManifest.FromJson(response.Manifest.ToJson()), task)).EnsureSuccess();
        if(typed[0].EnsureSuccess().Value != "answer" || ledger.Count != 2)
            throw new InvalidOperationException("Batch output or ledger mismatch.");
    }

    public sealed record BatchAnswer(string Value);
    sealed class Handler : HttpMessageHandler
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
}
