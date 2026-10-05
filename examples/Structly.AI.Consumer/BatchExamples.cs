using Structly.AI;
using Structly.AI.OpenAI;

static class BatchExamples
{
    public static async Task Run()
    {
        // This in-memory set illustrates usage deduplication. Production applications use a
        // durable unique execution-ID key and commit usage in the same transaction.
        var ledger = new HashSet<Guid>();
        ValueTask Record(StructuredUsageEvent item, CancellationToken token)
        {
            if(ledger.Add(item.Metadata.ExecutionId))
                Console.WriteLine($"Batch usage {item.RequestedModel}: {item.Usage.TotalTokens}");

            return ValueTask.CompletedTask;
        }

        // Configure a client whose handler supplies batch status and output files locally.
        var task = StructuredTask.Create<BatchAnswer>(new() { Instructions = "Extract" });
        using var http = new HttpClient(new BatchResponsesHandler())
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        var client = new OpenAiClient(http, new()
        {
            DefaultModel = new() { ModelId = "offline" },
            CredentialResolver = Credentials.FromStatic("offline"),
            UsageObserver = Record,
        });

        // Prepare one embedding item and one typed response item in separate batches.
        var embedding = client.PrepareEmbeddingBatch([
            new("embedding-item", new()
            {
                Inputs = ["document"],
                ModelSelection = new() { ModelId = "offline-embedding" },
                Dimensions = 2,
            }),
        ]);
        var response = client.PrepareResponseBatch(task, [
            new("response-item", new() { Input = "document" }),
        ]);

        // Persist the manifest JSON and remote batch ID in production. Here the handler
        // provides completed jobs, so reconstruct the manifest and import directly.
        var manifest = BatchManifest.FromJson(embedding.Manifest.ToJson());
        for(var i = 0; i < 2; i++)
        {
            var imported = await client.ImportEmbeddingBatchResultsAsync("embedding-job", manifest);
            if(!imported.EnsureSuccess().Items[0].Result.IsSuccess)
                throw new InvalidOperationException("Embedding batch failed.");
        }

        // Re-importing the embedding batch above must not add another ledger entry.
        var responseManifest = BatchManifest.FromJson(response.Manifest.ToJson());
        var typed = await client.ImportResponseBatchResultsAsync("response-job", responseManifest, task);
        if(typed.EnsureSuccess().Items[0].Result.EnsureSuccess().Value != "answer" || ledger.Count != 2)
            throw new InvalidOperationException("Batch output or ledger mismatch.");
    }
}
