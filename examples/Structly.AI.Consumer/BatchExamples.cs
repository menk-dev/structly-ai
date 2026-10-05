using Structly.AI;
using Structly.AI.OpenAI;

static class BatchExamples
{    // This in-memory set illustrates the ledger contract. Production applications use a
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
        using var http = new HttpClient(new BatchResponsesHandler());
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

}
