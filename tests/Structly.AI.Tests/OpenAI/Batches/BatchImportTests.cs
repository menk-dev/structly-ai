using Structly.AI.OpenAI;
using Structly.AI.Testing;
using System.Net;
using System.Text.Json;
using static Structly.AI.Tests.BatchTestSupport;

namespace Structly.AI.Tests;

public sealed class BatchImportTests
{
    [Fact]
    public async Task SummariesGroupModelsAndRetainPartialCoverageAndOverflow()
    {
        var bodies = new[]
        {
            ResponseEnvelopes.Embeddings([new float[] { 1, 2 }], "resolved", new() { InputTokens = Int64.MaxValue, TotalTokens = 0 }),
            ResponseEnvelopes.Embeddings([new float[] { 1, 2 }], "other", new() { InputTokens = 2 }),
            ResponseEnvelopes.Embeddings([new float[] { 1, 2 }], "resolved", new() { InputTokens = 1 }),
        };
        using var handler = new Handler(request => request.RequestUri!.AbsolutePath.EndsWith("batch", StringComparison.Ordinal) ? Response(Job()) :
            new(HttpStatusCode.OK) { Content = new StringContent(request.RequestUri.AbsolutePath.Contains("errors", StringComparison.Ordinal) ? "" : Line("c", bodies[2]) + Line("b", bodies[1]) + Line("a", bodies[0])) });
        using var http = new HttpClient(handler);
        var client = Client(http);
        var manifest = client.PrepareEmbeddingBatch([Item("a"), Item("b"), Item("c"), Item("missing")]).Manifest;
        for(var attempt = 0; attempt < 2; attempt++)
        {
            var imported = await client.ImportEmbeddingBatchResultsAsync("batch", manifest, cancellationToken: TestContext.Current.CancellationToken);
            var result = imported.EnsureSuccess();
            Assert.Equal(new[] { "a", "b", "c", "missing" }, result.Items.Select(item => item.CustomId));
            Assert.Equal(new[] { "resolved", "other", "embedding" }, result.UsageByModel.Select(summary => summary.Model));
            var summary = result.UsageByModel[0];
            Assert.Equal(2, summary.ItemCount);
            Assert.Equal(2, summary.ItemsWithUsage);
            Assert.Null(summary.ReportedUsage.InputTokens);
            Assert.Equal(2, summary.Coverage.InputTokens);
            Assert.Equal(0, summary.ReportedUsage.TotalTokens);
            Assert.Equal(1, summary.Coverage.TotalTokens);
            Assert.Null(summary.ReportedUsage.OutputTokens);
            Assert.Equal(0, summary.Coverage.OutputTokens);
            Assert.Equal(2, result.UsageByModel[1].ReportedUsage.InputTokens);
            Assert.Equal(1, result.UsageByModel[2].ItemCount);
            Assert.Equal(0, result.UsageByModel[2].ItemsWithUsage);
            var warning = Assert.Single(imported.Warnings);
            Assert.Equal("BatchUsageOverflow", warning.Code);
            Assert.Contains("resolved", warning.Message);
            Assert.Contains("InputTokens", warning.Message);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void BatchItemRejectsMissingIdentity(string? customId)
        => Assert.ThrowsAny<ArgumentException>(() => new BatchItemResult<int>(customId!, StructuredResult<int>.Success(1, new())));

    [Fact]
    public async Task RestartImportOrdersResultsAndReusesAccountingIds()
    {
        using var handler = new Handler(request => request.RequestUri!.AbsolutePath switch
        {
            "/v1/batches/batch" => Response(Job("cancelled")),
            "/v1/files/output/content" => new(HttpStatusCode.OK) { Content = new StringContent(Line("b", Vectors()) + Line("a", Vectors())) },
            _ => new(HttpStatusCode.OK) { Content = new StringContent("") },
        });
        using var http = new HttpClient(handler);
        var observed = new List<Guid>();
        var client = Client(http, (item, _) =>
        {
            Assert.Equal("embedding", item.RequestedModel);
            Assert.Equal(7, item.Usage.InputTokens);
            observed.Add(item.Metadata.ExecutionId);
            return ValueTask.CompletedTask;
        });
        var prepared = client.PrepareEmbeddingBatch([Item("a"), Item("b"), Item("missing")]);
        var persisted = BatchManifest.FromJson(prepared.Manifest.ToJson());
        for(var i = 0; i < 2; i++)
        {
            var results = (await client.ImportEmbeddingBatchResultsAsync("batch", persisted, cancellationToken: TestContext.Current.CancellationToken)).EnsureSuccess();
            Assert.Equal("a", results.Items[0].CustomId);
            Assert.True(results.Items[0].Result.IsSuccess);
            Assert.Equal(new float[] { 1, 2 }, results.Items[1].Result.Value![0]);
            Assert.Equal("BatchResultMissing", Assert.Single(results.Items[2].Result.Error!.Issues).Code);
            Assert.Equal(persisted.Items[0].ExecutionId, results.Items[0].Result.Metadata.ExecutionId);
        }

        Assert.Equal(4, observed.Count);
        Assert.Equal(observed[0], observed[2]);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("duplicate")]
    [InlineData("limit")]
    public async Task MalformedImportFailsOuterResultWithAlreadyObservedUsage(string scenario)
    {
        using var handler = new Handler(request => request.RequestUri!.AbsolutePath.EndsWith("batch", StringComparison.Ordinal) ? Response(Job()) :
            new(HttpStatusCode.OK) { Content = new StringContent(Line("a", Vectors()) + Line(scenario == "unknown" ? "other" : "a", Vectors())) });
        using var http = new HttpClient(handler);
        var events = 0;
        var client = Client(http, (_, _) =>
        {
            events++;
            return ValueTask.CompletedTask;
        });
        var prepared = client.PrepareEmbeddingBatch([Item("a")]);
        var result = await client.ImportEmbeddingBatchResultsAsync("batch", prepared.Manifest, new() { MaxDownloadBytes = scenario == "limit" ? 1 : 10000 }, TestContext.Current.CancellationToken);
        Assert.Equal(StructuredErrorKind.InvalidResponse, result.Error!.Kind);
        Assert.Equal(scenario == "limit" ? 0 : 1, events);
    }

    [Fact]
    public async Task TypedImportValidatesFingerprintBeforeHttpAndRetainsInvalidOutputUsage()
    {
        var task = StructuredTask.Create<Answer>(new() { Instructions = "Extract" });
        var envelope = ResponseEnvelopes.CompletedText("{\"value\":42}", "resolved", usage: new() { TotalTokens = 8 });
        using var handler = new Handler(request => request.RequestUri!.AbsolutePath.EndsWith("batch", StringComparison.Ordinal) ? Response(Job(endpoint: "/v1/responses")) : new(HttpStatusCode.OK) { Content = new StringContent(request.RequestUri.AbsolutePath.Contains("errors", StringComparison.Ordinal) ? "" : Line("a", envelope)) });
        using var http = new HttpClient(handler);
        StructuredUsageEvent? observed = null;
        var client = Client(http, (item, _) =>
        {
            observed = item;
            return ValueTask.CompletedTask;
        });
        var prepared = client.PrepareResponseBatch(task, [new("a", new() { Input = "input" })]);
        var wrong = StructuredTask.Create<OtherAnswer>(new() { Instructions = "Extract" });
        Assert.Equal(StructuredErrorKind.InvalidRequest, (await client.ImportResponseBatchResultsAsync("batch", prepared.Manifest, wrong, cancellationToken: TestContext.Current.CancellationToken)).Error!.Kind);
        Assert.Equal(0, handler.Calls);
        var results = (await client.ImportResponseBatchResultsAsync("batch", prepared.Manifest, task, cancellationToken: TestContext.Current.CancellationToken)).EnsureSuccess();
        Assert.Equal(StructuredErrorKind.InvalidOutput, results.Items[0].Result.Error!.Kind);
        Assert.Equal(8, observed!.Usage.TotalTokens);
        Assert.Equal(8, Assert.Single(results.UsageByModel).ReportedUsage.TotalTokens);
    }

    [Fact]
    public async Task ErrorFilesAreImportedWithoutInventingMissingUsageAndObserversAreBestEffort()
    {
        var errorLine = JsonSerializer.Serialize(new { custom_id = "b", error = new { code = "batch_expired", message = "private-provider-message" } }) + "\n";
        using var handler = new Handler(request => request.RequestUri!.AbsolutePath.EndsWith("batch", StringComparison.Ordinal) ? Response(Job("expired")) : new(HttpStatusCode.OK) { Content = new StringContent(request.RequestUri.AbsolutePath.Contains("errors", StringComparison.Ordinal) ? errorLine : Line("a", Vectors())) });
        using var http = new HttpClient(handler);
        var client = Client(http);
        var prepared = client.PrepareEmbeddingBatch([Item("a"), Item("b")]);
        var events = 0;
        var result = await client.ImportEmbeddingBatchResultsAsync("batch", prepared.Manifest, new()
        {
            UsageObserver = (_, _) =>
        {
            events++;
            return ValueTask.FromException(new InvalidOperationException("private-observer-message"));
        },
        }, TestContext.Current.CancellationToken);
        var items = result.EnsureSuccess();
        Assert.True(items.Items[0].Result.IsSuccess);
        Assert.Equal(StructuredErrorKind.BatchItemFailed, items.Items[1].Result.Error!.Kind);
        Assert.Null(items.Items[1].Result.Metadata.Usage);
        Assert.Equal(1, events);
        Assert.Equal(7, Assert.Single(items.UsageByModel).ReportedUsage.InputTokens);
        Assert.Contains(result.Warnings, warning => warning.Code == "UsageObserverFailed");
        Assert.DoesNotContain("private", items.Items[1].Result.Error!.Message);
    }

    [Fact]
    public async Task NonterminalImportsAndPerEnvelopeLimitsFailBeforeProcessingItems()
    {
        using var handler = new Handler(request => request.RequestUri!.AbsolutePath.EndsWith("batch", StringComparison.Ordinal) ? Response(Job("in_progress")) : throw new InvalidOperationException("Do not download"));
        using var http = new HttpClient(handler);
        var client = Client(http);
        var manifest = client.PrepareEmbeddingBatch([Item("a")]).Manifest;
        Assert.Equal(StructuredErrorKind.InvalidRequest, (await client.ImportEmbeddingBatchResultsAsync("batch", manifest, cancellationToken: TestContext.Current.CancellationToken)).Error!.Kind);
        Assert.Equal(1, handler.Calls);
        using var limitedHandler = new Handler(request => request.RequestUri!.AbsolutePath.EndsWith("batch", StringComparison.Ordinal) ? Response(Job()) : new(HttpStatusCode.OK) { Content = new StringContent(new string('x', 2048)) });
        using var limitedHttp = new HttpClient(limitedHandler);
        var limitedClient = new OpenAiClient(limitedHttp, new() { DefaultModel = new() { ModelId = "model" }, CredentialResolver = Credentials.FromStatic("key"), MaxResponseBytes = 1024 });
        Assert.Equal(StructuredErrorKind.InvalidResponse, (await limitedClient.ImportEmbeddingBatchResultsAsync("batch", manifest, cancellationToken: TestContext.Current.CancellationToken)).Error!.Kind);
    }

}
