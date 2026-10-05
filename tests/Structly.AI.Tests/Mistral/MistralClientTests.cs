using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Structly.AI.Hosting;
using Structly.AI.Mistral;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Structly.AI.Tests;

public sealed class MistralClientTests
{
    const string _envelope = """{"id":"response","model":"resolved","usage":{"prompt_tokens":3,"completion_tokens":4,"total_tokens":7},"choices":[{"finish_reason":"stop","message":{"content":"{\"value\":\"answer\"}"}}]}""";

    [Fact]
    public async Task Typed_output_sends_schema_and_retains_accounting()
    {
        using var handler = new Handler(_envelope);
        using var http = new HttpClient(handler);
        StructuredUsageEvent? observed = null;
        var options = Options();
        options.UsageObserver = (usage, _) =>
        {
            observed = usage;
            return ValueTask.CompletedTask;
        };
        var client = new MistralClient(http, options);
        options.DefaultModel = new() { ModelId = "changed" };
        var result = await client.ExecuteAsync(StructuredTask.Create<Answer>("Extract"), "input", TestContext.Current.CancellationToken);
        Assert.Equal("answer", result.EnsureSuccess().Value);
        Assert.Equal("Mistral", result.Metadata.Provider);
        Assert.Equal("resolved", result.Metadata.ResolvedModel);
        Assert.Equal(7, result.Metadata.Usage!.TotalTokens);
        Assert.True(observed!.Succeeded);
        Assert.Equal("https://api.mistral.ai/v1/chat/completions", handler.Uri!.ToString());
        using var payload = JsonDocument.Parse(handler.Payload!);
        Assert.Equal("offline", payload.RootElement.GetProperty("model").GetString());
        Assert.Equal("json_schema", payload.RootElement.GetProperty("response_format").GetProperty("type").GetString());
        Assert.Equal("Bearer test", handler.Authorization);
    }

    [Fact]
    public async Task Text_generation_omits_schema()
    {
        using var handler = new Handler(_envelope);
        using var http = new HttpClient(handler);
        var result = await new MistralClient(http, Options()).GenerateTextAsync(new() { Request = new() { Input = "input" } }, TestContext.Current.CancellationToken);
        Assert.Equal("{\"value\":\"answer\"}", result.EnsureSuccess());
        using var payload = JsonDocument.Parse(handler.Payload!);
        Assert.False(payload.RootElement.TryGetProperty("response_format", out _));
    }

    [Theory]
    [InlineData(401, StructuredErrorKind.Authentication)]
    [InlineData(403, StructuredErrorKind.PermissionDenied)]
    [InlineData(429, StructuredErrorKind.RateLimited)]
    [InlineData(503, StructuredErrorKind.ProviderUnavailable)]
    [InlineData(400, StructuredErrorKind.ProviderRejected)]
    public async Task Http_failures_are_sanitized(int status, StructuredErrorKind kind)
    {
        using var handler = new Handler("secret", (HttpStatusCode)status);
        using var http = new HttpClient(handler);
        var result = await new MistralClient(http, Options()).ExecuteAsync(StructuredTask.Create<Answer>("Extract"), "input", TestContext.Current.CancellationToken);
        Assert.Equal(kind, result.Error!.Kind);
        Assert.DoesNotContain("secret", result.Error.Message);
    }

    [Theory]
    [InlineData("not json", StructuredErrorKind.InvalidResponse)]
    [InlineData("{\"choices\":[]}", StructuredErrorKind.InvalidResponse)]
    [InlineData("{\"choices\":[{\"finish_reason\":\"length\",\"message\":{\"content\":\"partial\"}}]}", StructuredErrorKind.IncompleteOutput)]
    [InlineData("{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"content\":\"{}\"}}]}", StructuredErrorKind.InvalidOutput)]
    public async Task Invalid_output_is_categorized(string envelope, StructuredErrorKind kind)
    {
        using var handler = new Handler(envelope);
        using var http = new HttpClient(handler);
        var result = await new MistralClient(http, Options()).ExecuteAsync(StructuredTask.Create<Answer>("Extract"), "input", TestContext.Current.CancellationToken);
        Assert.Equal(kind, result.Error!.Kind);
    }

    [Fact]
    public async Task Unsupported_requests_and_bound_overrides_do_not_resolve_credentials()
    {
        using var http = new HttpClient();
        var options = Options();
        options.CredentialResolver = _ => throw new Exception("Must not resolve");
        var client = new MistralClient(http, options);
        var task = StructuredTask.Create<Answer>("Extract");
        foreach(var request in new[] { new StructuredRequest { Input = "input", Stream = true }, new StructuredRequest { Input = "input", ModelSelection = new() { ProfileName = "missing" } } })
            Assert.Equal(StructuredErrorKind.InvalidRequest, (await client.ExecuteAsync(task, request, TestContext.Current.CancellationToken)).Error!.Kind);

        Assert.Equal(StructuredErrorKind.InvalidRequest, (await client.ExecuteAsync(task.BindOutput(), new() { Input = "input", Vocabularies = new Dictionary<string, IReadOnlyList<string>>() }, TestContext.Current.CancellationToken)).Error!.Kind);
        new MistralClientFactory().Validate(options);
    }

    [Fact]
    public async Task Credential_resolution_obeys_deadline_and_caller_cancellation()
    {
        using var http = new HttpClient();
        var options = Options();
        options.TotalTimeout = TimeSpan.FromMilliseconds(30);
        options.CredentialResolver = async token =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return "key";
        };
        var client = new MistralClient(http, options);
        var task = StructuredTask.Create<Answer>("Extract");
        Assert.Equal(StructuredErrorKind.DeadlineExceeded, (await client.ExecuteAsync(task, "input", TestContext.Current.CancellationToken)).Error!.Kind);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var exception = await Assert.ThrowsAsync<StructuredOperationCanceledException>(() => client.ExecuteAsync(task, "input", cancelled.Token));
        Assert.Equal(cancelled.Token, exception.CancellationToken);
    }

    [Fact]
    public async Task Hosting_binds_mistral_and_executes_named_task()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Structly:Mistral:DefaultModel:ModelId"] = "offline",
            ["Structly:Mistral:ApiKey"] = "test",
        }).Build();
        var services = new ServiceCollection();
        services.AddStructlyAi(configuration, builder =>
        {
            builder.ConfigureMistralProvider();
            builder.AddTask<Answer>("answer", "Extract");
        }).ConfigurePrimaryHttpMessageHandler(() => new Handler(_envelope));
        using var provider = services.BuildServiceProvider();
        var result = await provider.GetRequiredService<StructlyAi>().ExecuteTaskAsync<Answer>("answer", "input", TestContext.Current.CancellationToken);
        Assert.Equal("answer", result.EnsureSuccess().Value);
    }

    static MistralOptions Options() => new() { DefaultModel = new() { ModelId = "offline" }, ApiKey = "test" };
    public sealed record Answer(string Value);
    sealed class Handler(string envelope, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public string? Payload { get; set; }
        public Uri? Uri { get; set; }
        public string? Authorization { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Payload = await request.Content!.ReadAsStringAsync(cancellationToken);
            Uri = request.RequestUri;
            Authorization = request.Headers.Authorization?.ToString();
            return new(status) { Content = new StringContent(envelope, Encoding.UTF8, "application/json") };
        }
    }
}
