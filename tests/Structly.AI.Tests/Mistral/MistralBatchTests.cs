using Structly.AI.Embeddings;
using Structly.AI.Mistral;
using System.Net;
using System.Text;
using System.Text.Json;
using static Structly.AI.Tests.MistralFixture;

namespace Structly.AI.Tests;

public sealed class MistralBatchTests
{
    [Fact]
    public async Task Preparation_submission_and_import_use_mistral_wire_formats()
    {
        using var handler = new Handler(request => request.RequestUri!.AbsolutePath switch
        {
            "/v1/files" => Handler.Response("""{"id":"input","filename":"batch.jsonl","bytes":null,"purpose":"batch"}"""),
            "/v1/files/output/content" => Handler.Response("{\"custom_id\":\"second\",\"response\":{\"status_code\":200,\"body\":" + Chat + "},\"error\":null}\n" +
                "{\"custom_id\":\"first\",\"response\":{\"status_code\":200,\"body\":" + Chat + "},\"error\":null}\n"),
            _ => Handler.Response(Job),
        });
        using var http = new HttpClient(handler);
        var client = new MistralClient(http, Options());
        var task = StructuredTask.Create<Answer>("Extract");
        var prepared = client.PrepareResponseBatch(task, [new("first", new MistralRequest { Input = "one", CaptureOutputText = true }), new("second", new() { Input = "two" })]);
        var manifest = BatchManifest.FromJson(prepared.Manifest.ToJson());
        foreach(var line in prepared.Jsonl.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            Assert.False(root.TryGetProperty("method", out _));
            Assert.False(root.TryGetProperty("url", out _));
            Assert.False(root.GetProperty("body").TryGetProperty("model", out _));
            Assert.Equal("json_schema", root.GetProperty("body").GetProperty("response_format").GetProperty("type").GetString());
        }

        var submitted = await client.SubmitBatchAsync(prepared, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("job", submitted.EnsureSuccess().Id);
        Assert.Contains("name=purpose", handler.Payloads[0]);
        using var creation = JsonDocument.Parse(handler.Payloads[1]);
        Assert.Equal("input", creation.RootElement.GetProperty("input_files")[0].GetString());
        Assert.Equal("offline", creation.RootElement.GetProperty("model").GetString());
        Assert.EndsWith("/batch/jobs", handler.Uris[1].AbsolutePath);

        var observed = new List<StructuredUsageEvent>();
        var imported = await client.ImportResponseBatchResultsAsync("job", manifest, task, new()
        {
            UsageObserver = (usage, _) =>
            {
                observed.Add(usage);
                return ValueTask.CompletedTask;
            },
        }, TestContext.Current.CancellationToken);
        var result = imported.EnsureSuccess();
        Assert.Equal(new[] { "first", "second" }, result.Items.Select(x => x.CustomId));
        Assert.All(result.Items, x => Assert.Equal("answer", x.Result.EnsureSuccess().Value));
        Assert.Equal(manifest.Items[0].ExecutionId, result.Items[0].Result.Metadata.ExecutionId);
        Assert.Equal("{\"value\":\"answer\"}", result.Items[0].Result.Metadata.OutputText);
        Assert.True(result.Items[0].Result.Metadata.IsBatch);
        Assert.Equal(2, observed.Count);
        Assert.Equal(14, result.UsageByModel[0].ReportedUsage.TotalTokens);
        Assert.Equal(2, result.UsageByModel[0].Coverage.TotalTokens);
    }

    [Fact]
    public async Task Partial_terminal_results_keep_http_failures_and_missing_items_in_order()
    {
        using var handler = new Handler(request => Handler.Response(request.RequestUri!.AbsolutePath.EndsWith("/content", StringComparison.Ordinal)
            ? """
                {"custom_id":"first","response":{"status_code":429,"body":{"error":{"message":"secret"}}},"error":null}
                {"custom_id":"second","response":null,"error":{"message":"secret"}}
                """
            : Job.Replace("SUCCESS", "CANCELLED")));
        using var http = new HttpClient(handler);
        var client = new MistralClient(http, Options());
        var task = StructuredTask.Create<Answer>("Extract");
        var batch = client.PrepareResponseBatch(task, [new("first", new() { Input = "one" }), new("second", new() { Input = "two" }), new("third", new() { Input = "three" })]);
        var result = (await client.ImportResponseBatchResultsAsync("job", batch.Manifest, task, cancellationToken: TestContext.Current.CancellationToken)).EnsureSuccess();
        Assert.Equal(new[] { StructuredErrorKind.RateLimited, StructuredErrorKind.BatchItemFailed, StructuredErrorKind.IncompleteOutput }, result.Items.Select(x => x.Result.Error!.Kind));
        Assert.All(result.Items, x => Assert.DoesNotContain("secret", x.Result.Error!.Message));
        Assert.Equal(0, result.UsageByModel[0].ItemsWithUsage);
        Assert.Null(result.UsageByModel[0].ReportedUsage.TotalTokens);
    }

    [Fact]
    public async Task Duplicate_results_and_download_limits_are_rejected()
    {
        var line = "{\"custom_id\":\"first\",\"response\":{\"status_code\":200,\"body\":" + Chat + "},\"error\":null}\n";
        using var handler = new Handler(request => Handler.Response(request.RequestUri!.AbsolutePath.EndsWith("/content", StringComparison.Ordinal) ? line + line : Job));
        using var http = new HttpClient(handler);
        var client = new MistralClient(http, Options());
        var task = StructuredTask.Create<Answer>("Extract");
        var batch = client.PrepareResponseBatch(task, [new("first", new() { Input = "one" })]);
        Assert.Equal(StructuredErrorKind.InvalidResponse, (await client.ImportResponseBatchResultsAsync("job", batch.Manifest, task, cancellationToken: TestContext.Current.CancellationToken)).Error!.Kind);
        Assert.Equal(StructuredErrorKind.InvalidResponse, (await client.ImportResponseBatchResultsAsync("job", batch.Manifest, task, new() { MaxDownloadBytes = 1 }, TestContext.Current.CancellationToken)).Error!.Kind);
    }

    [Fact]
    public async Task Schema_mismatch_is_rejected_before_credentials()
    {
        using var http = new HttpClient();
        var options = Options();
        options.CredentialResolver = _ => throw new Exception("Must not resolve");
        var client = new MistralClient(http, options);
        var task = StructuredTask.Create<Answer>("Extract");
        var manifest = client.PrepareResponseBatch(task, [new("first", new() { Input = "one" })]).Manifest;
        manifest = manifest with { Items = [manifest.Items[0] with { SchemaFingerprint = "changed" }] };
        Assert.Equal(StructuredErrorKind.InvalidRequest, (await client.ImportResponseBatchResultsAsync("job", manifest, task, cancellationToken: TestContext.Current.CancellationToken)).Error!.Kind);
    }

    [Fact]
    public async Task Embedding_batches_use_output_dimension_and_import_vectors()
    {
        using var handler = new Handler(request => Handler.Response(request.RequestUri!.AbsolutePath.EndsWith("/content", StringComparison.Ordinal)
            ? "{\"custom_id\":\"first\",\"response\":{\"status_code\":200,\"body\":" + Vectors + "},\"error\":null}\n"
            : Job.Replace("/v1/chat/completions", "/v1/embeddings")));
        using var http = new HttpClient(handler);
        var client = new MistralClient(http, Options());
        var batch = client.PrepareEmbeddingBatch([new("first", new EmbeddingRequest { Inputs = ["one", "two"], ModelSelection = new() { ModelId = "offline" }, Dimensions = 2 })]);
        using var payload = JsonDocument.Parse(batch.Jsonl.Trim());
        Assert.Equal(2, payload.RootElement.GetProperty("body").GetProperty("output_dimension").GetInt32());
        var imported = (await client.ImportEmbeddingBatchResultsAsync("job", batch.Manifest, cancellationToken: TestContext.Current.CancellationToken)).EnsureSuccess();
        Assert.Equal(new float[] { 1, 2 }, imported.Items[0].Result.EnsureSuccess()[0]);
    }

    [Fact]
    public async Task Submit_failure_retains_uploaded_file_without_cleanup()
    {
        using var handler = new Handler(request => request.RequestUri!.AbsolutePath == "/v1/files"
            ? Handler.Response("""{"id":"input","filename":"batch.jsonl","bytes":1}""")
            : Handler.Response("secret", HttpStatusCode.BadRequest));
        using var http = new HttpClient(handler);
        var client = new MistralClient(http, Options());
        var batch = client.PrepareResponseBatch(StructuredTask.Create<Answer>("Extract"), [new("first", new() { Input = "one" })]);
        var result = await client.SubmitBatchAsync(batch, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(StructuredErrorKind.ProviderRejected, result.Error!.Kind);
        Assert.Equal("input", result.Metadata.UploadedFileId);
        Assert.Equal(2, handler.Uris.Count);
    }

    [Fact]
    public async Task Lifecycle_uses_numbered_pages_and_cancel_route()
    {
        using var handler = new Handler(request => Handler.Response(request.RequestUri!.Query.Length > 0 ? "{\"total\":1,\"data\":[" + Job + "]}" : Job));
        using var http = new HttpClient(handler);
        var client = new MistralClient(http, Options());
        var page = await client.ListBatchesAsync(2, 20, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(1, page.EnsureSuccess().Total);
        Assert.Equal("?page=2&page_size=20", handler.Uris[0].Query);
        Assert.True((await client.CancelBatchAsync("job", cancellationToken: TestContext.Current.CancellationToken)).IsSuccess);
        Assert.EndsWith("/batch/jobs/job/cancel", handler.Uris[1].AbsolutePath);
        Assert.Equal(HttpMethod.Post, handler.Methods[1]);
    }

    [Fact]
    public void Preparation_rejects_mixed_models_duplicate_ids_and_transport_controls()
    {
        using var http = new HttpClient();
        var client = new MistralClient(http, Options());
        var task = StructuredTask.Create<Answer>("Extract");
        var first = new ResponseBatchItem("first", new() { Input = "one" });
        Assert.Throws<ArgumentException>(() => client.PrepareResponseBatch(task, [first, first]));
        Assert.Throws<ArgumentException>(() => client.PrepareResponseBatch(task, [first, new("second", new() { Input = "two", ModelSelection = new() { ModelId = "other" } })]));
        Assert.Throws<ArgumentException>(() => client.PrepareResponseBatch(task, [new("first", new() { Input = "one", Stream = true })]));
    }
}
