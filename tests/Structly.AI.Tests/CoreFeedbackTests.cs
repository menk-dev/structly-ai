using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Structly.AI.Hosting;
using Structly.AI.OpenAI;
using Structly.AI.Testing;
using System.Net;
using System.Text.Json.Serialization;

namespace Structly.AI.Tests;

public sealed class CoreFeedbackTests
{
    [Fact]
    public async Task MissingInstructionsPrecedeCredentialsForExecutionAndPrewarm()
    {
        var calls = 0;
        using var http = new HttpClient(new Handler(_ => throw new InvalidOperationException("No HTTP")));
        var client = new OpenAiClient(http, new()
        {
            DefaultModel = new() { ModelId = "gpt-6-sol" },
            CredentialResolver = _ =>
        {
            calls++;
            return ValueTask.FromResult<string?>("key");
        }
        });
        var task = StructuredTask.Create<Answer>(new());
        Assert.Equal(StructuredErrorKind.InvalidRequest, (await client.ExecuteAsync(task, new() { Input = "input" }, TestContext.Current.CancellationToken)).Error!.Kind);
        Assert.Equal(StructuredErrorKind.InvalidRequest, (await client.PrewarmAsync(task, new() { Request = new() { Input = "input" } }, TestContext.Current.CancellationToken)).Error!.Kind);
        Assert.Equal(0, calls);
        Assert.True(task.ReadOutput(task.CreateExample().GetRawText()).IsSuccess);
    }

    [Fact]
    public async Task BlankSelectedCredentialsDoNotFallBackAndUsageHasRequestedModel()
    {
        using var handler = new Handler(_ => ResponseEnvelopes.ToHttpResponse(ResponseEnvelopes.CompletedText("{}", usage: new() { InputTokens = Int64.MaxValue })));
        using var http = new HttpClient(handler);
        StructuredUsageEvent? observed = null;
        var client = new OpenAiClient(http, new()
        {
            DefaultModel = new() { ModelId = "requested" },
            CredentialResolver = Credentials.FromStatic("client"),
            UsageObserver = (item, _) =>
        {
            observed = item;
            return ValueTask.CompletedTask;
        }
        });
        var task = StructuredTask.Create<Answer>(new() { Instructions = "Extract" });
        var absent = await client.ExecuteAsync(task, new() { Input = "input", CredentialResolver = _ => ValueTask.FromResult<string?>(" ") }, TestContext.Current.CancellationToken);
        Assert.Equal(StructuredErrorKind.CredentialsMissing, absent.Error!.Kind);
        Assert.Equal(0, handler.Calls);
        var invalid = await client.ExecuteAsync(task, new() { Input = "input" }, TestContext.Current.CancellationToken);
        Assert.Equal(StructuredErrorKind.InvalidOutput, invalid.Error!.Kind);
        Assert.Equal("requested", observed!.RequestedModel);
        Assert.Equal("openai", observed.Provider);
        Assert.Equal(Int64.MaxValue, observed.Usage.InputTokens);
        Assert.Null(observed.Usage.OutputTokens);
    }

    [Fact]
    public void PreserveInputOrderingAppliesToExamplesSchemasAndSpecifications()
    {
        var task = StructuredTask.Create<Choice>(new() { VocabularyOrder = VocabularyOrder.PreserveInput });
        var entries = new List<string> { "z", "a" };
        var values = new Dictionary<string, IReadOnlyList<string>> { ["choices"] = entries };
        var schema = task.CreateSchema(values);
        var example = task.CreateExample(values);
        var guidance = task.CreateOutputSpecification(new(), values);
        entries[0] = "changed";
        Assert.Equal("z", schema.GetProperty("properties").GetProperty("value").GetProperty("enum")[0].GetString());
        Assert.Equal("z", example.GetProperty("value").GetString());
        Assert.Contains("z", guidance);
    }

    [Fact]
    public void InvalidOutputSummariesAreEscapedBoundedAndOmitValues()
    {
        var task = StructuredTask.Create<ControlNames>(new());
        var result = task.ReadOutput("{}");
        Assert.DoesNotContain("\n", result.Error!.Message);
        Assert.Contains("\\n", result.Error.Message);
        Assert.InRange(result.Error.Message.Length, 1, 512);
        Assert.NotEmpty(result.Error.Issues);
        var extra = task.ReadOutput("{\"secret-value\":123}");
        Assert.DoesNotContain("123", extra.Error!.Message);
    }

    [Theory]
    [InlineData("gpt-6-luna", true)]
    [InlineData("gpt-6-sol", true)]
    [InlineData("gpt-6-astra", true)]
    [InlineData("gpt-6-sol-new", false)]
    public async Task CacheDefaultsUseExactIds(string model, bool supported)
    {
        using var handler = new Handler(_ => new(HttpStatusCode.OK) { Content = new StringContent("{\"id\":\"warm\",\"model\":\"resolved\",\"status\":\"completed\",\"output\":[]}") });
        using var http = new HttpClient(handler);
        var client = new OpenAiClient(http, new() { DefaultModel = new() { ModelId = model }, CredentialResolver = Credentials.FromStatic("key") });
        var result = await client.PrewarmAsync(new() { Request = new() { Input = "warm" } }, TestContext.Current.CancellationToken);
        Assert.Equal(supported, result.IsSuccess);
        Assert.Equal(supported ? 1 : 0, handler.Calls);
        var overridden = new OpenAiClient(http, new() { DefaultModel = new() { ModelId = model }, CredentialResolver = Credentials.FromStatic("key"), CacheCompatibility = new Dictionary<string, OpenAiCacheCompatibility> { [model] = OpenAiCacheCompatibility.Legacy } });
        Assert.Equal(StructuredErrorKind.InvalidRequest, (await overridden.PrewarmAsync(new() { Request = new() { Input = "warm" } }, TestContext.Current.CancellationToken)).Error!.Kind);
    }

    public sealed class Answer { public required string Value { get; init; } }
    public sealed class Choice { [DynamicVocabulary("choices")] public required string Value { get; init; } }
    public sealed class ControlNames { [JsonPropertyName("line\nbreak")] public required string Value { get; init; } }
    sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(send(request));
        }
    }
}

[CollectionDefinition("Environment credentials", DisableParallelization = true)]
public sealed class EnvironmentCredentialsCollection;

[Collection("Environment credentials")]
public sealed class EnvironmentHostingTests
{
    [Theory]
    [InlineData(false, null, false)]
    [InlineData(true, null, false)]
    [InlineData(true, "configured", false)]
    [InlineData(true, "configured", true)]
    public async Task CallbackHostingUsesOptInEnvironmentPerExecutionWithPrecedence(bool enabled, string? configured, bool custom)
    {
        var original = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", "first");
            var builder = Host.CreateApplicationBuilder();
            var handler = new RecordingHandler();
            builder.Services.AddStructlyOpenAi(options =>
            {
                options.DefaultModel = new() { ModelId = "gpt-6-sol" };
                options.UseEnvironmentApiKey = enabled;
                options.ApiKey = configured;
                if(custom)
                    options.CredentialResolver = Credentials.FromStatic("custom");
            }).ConfigurePrimaryHttpMessageHandler(() => handler);
            using var host = builder.Build();
            await host.StartAsync(TestContext.Current.CancellationToken);
            var client = host.Services.GetRequiredService<OpenAiClient>();
            for(var i = 0; i < 2; i++)
            {
                var result = await client.GenerateTextAsync(new() { Request = new() { Input = "input" } }, TestContext.Current.CancellationToken);
                if(!enabled && configured is null)
                {
                    Assert.Equal(StructuredErrorKind.CredentialsMissing, result.Error!.Kind);
                    Assert.Null(handler.Key);
                }
                else
                    Assert.Equal(custom ? "custom" : configured ?? (i == 0 ? "first" : "second"), handler.Key);
                Environment.SetEnvironmentVariable("OPENAI_API_KEY", "second");
            }
            await host.StopAsync(TestContext.Current.CancellationToken);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", original);
        }
    }
    sealed class RecordingHandler : HttpMessageHandler
    {
        public string? Key { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Key = request.Headers.Authorization?.Parameter;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }
    }
}
