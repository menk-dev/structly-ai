using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Structly.AI.Hosting;
using Structly.AI.OpenAI;
using System.Net;
using System.Text;

namespace Structly.AI.Tests;

public sealed class HostingTests
{
    [Fact]
    public async Task Json_settings_bind_and_client_uses_factory_transport()
    {
        using var json = new MemoryStream(Encoding.UTF8.GetBytes("""
            { "Structly": { "OpenAI": {
              "ApiKey": "test-key",
              "Store": false,
              "DefaultModel": { "ProfileName": "extract" },
              "Profiles": { "extract": { "ModelId": "test-model", "ReasoningEffort": "Low" } },
              "EmbeddingProfiles": { "search": { "ModelId": "test-embedding" } },
              "ImageProfiles": { "draw": { "ModelId": "test-image" } },
              "BaseAddress": "https://example.test/api/",
              "TotalTimeout": "00:03:00",
              "InactivityTimeout": "00:00:30",
              "MaxResponseBytes": 4096,
              "MaxImageResponseBytes": 8192
            } } }
            """));
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddJsonStream(json);
        var handler = new RecordingHandler();
        var registration = builder.Services.AddStructlyAi(builder.Configuration, ai => ai.ConfigureOpenAiProvider())
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        using var host = builder.Build();
        await host.StartAsync(TestContext.Current.CancellationToken);
        var settings = host.Services.GetRequiredService<IOptions<OpenAiOptions>>().Value;
        Assert.Equal(TimeSpan.FromMinutes(3), settings.TotalTimeout);
        Assert.Equal(TimeSpan.FromSeconds(30), settings.InactivityTimeout);
        Assert.Equal(4096, settings.MaxResponseBytes);
        Assert.Equal(8192, settings.MaxImageResponseBytes);
        Assert.Equal("test-embedding", settings.EmbeddingProfiles["search"].ModelId);
        Assert.Equal("test-image", settings.ImageProfiles["draw"].ModelId);
        using var http = host.Services.GetRequiredService<IHttpClientFactory>().CreateClient(registration.Name);
        Assert.Equal(Timeout.InfiniteTimeSpan, http.Timeout);
        var client = host.Services.GetRequiredService<OpenAiClient>();
        Assert.NotSame(client, host.Services.GetRequiredService<OpenAiClient>());
        var result = await client.GenerateTextAsync(new() { Request = new() { Input = "hello" } }, TestContext.Current.CancellationToken);
        Assert.Equal(StructuredErrorKind.ProviderUnavailable, result.Error!.Kind);
        Assert.Equal("https://example.test/api/responses", handler.Address);
        Assert.Equal("test-key", handler.Key);
        using var payload = System.Text.Json.JsonDocument.Parse(handler.Body);
        Assert.False(payload.RootElement.GetProperty("store").GetBoolean());
        Assert.Contains("test-model", handler.Body);
        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("DefaultModel:ModelId", "")]
    [InlineData("TotalTimeout", "00:00:00")]
    [InlineData("BaseAddress", "https://example.test/no-trailing-slash")]
    [InlineData("MaxResponseBytes", "0")]
    [InlineData("DefaultModel:ProfileName", "missing")]
    [InlineData("EmbeddingProfiles:search:ProfileName", "nested")]
    public async Task Invalid_settings_fail_at_startup_without_leaking_credentials(string key, string value)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Structly:OpenAI:DefaultModel:ModelId"] = "test-model",
            ["Structly:OpenAI:ApiKey"] = "secret-test-key",
            [$"Structly:OpenAI:{key}"] = value,
        });
        builder.Services.AddStructlyAi(builder.Configuration, ai => ai.ConfigureOpenAiProvider());
        using var host = builder.Build();
        var error = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync(TestContext.Current.CancellationToken));
        Assert.DoesNotContain("secret-test-key", error.Message);
    }

    [Fact]
    public async Task Explicit_section_and_runtime_resolver_override_configured_key()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Custom:OpenAI:DefaultModel:ModelId"] = "test-model",
            ["Custom:OpenAI:ApiKey"] = "configured-key",
        });
        var handler = new RecordingHandler();
        builder.Services.AddStructlyAi(builder.Configuration, ai => ai.ConfigureOpenAiProvider(options => options.CredentialResolver = Credentials.FromStatic("runtime-key")), sectionName: "Custom")
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        using var host = builder.Build();
        await host.StartAsync(TestContext.Current.CancellationToken);
        await host.Services.GetRequiredService<OpenAiClient>().GenerateTextAsync(new() { Request = new() { Input = "hello" } }, TestContext.Current.CancellationToken);
        Assert.Equal("runtime-key", handler.Key);
        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Reload_applies_to_new_clients_and_preserves_existing_snapshots()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Structly:OpenAI:DefaultModel:ModelId"] = "original-model",
            ["Structly:OpenAI:ApiKey"] = "original-key",
        });
        var handler = new RecordingHandler();
        builder.Services.AddStructlyAi(builder.Configuration, ai => ai.ConfigureOpenAiProvider())
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        using var host = builder.Build();
        var original = host.Services.GetRequiredService<OpenAiClient>();
        builder.Configuration["Structly:OpenAI:DefaultModel:ModelId"] = "updated-model";
        builder.Configuration["Structly:OpenAI:ApiKey"] = "updated-key";
        builder.Configuration["Structly:OpenAI:Store"] = "false";
        ((IConfigurationRoot)builder.Configuration).Reload();
        await original.GenerateTextAsync(new() { Request = new() { Input = "hello" } }, TestContext.Current.CancellationToken);
        Assert.Contains("original-model", handler.Body);
        Assert.Equal("original-key", handler.Key);
        using var originalPayload = System.Text.Json.JsonDocument.Parse(handler.Body);
        Assert.True(originalPayload.RootElement.GetProperty("store").GetBoolean());
        await host.Services.GetRequiredService<OpenAiClient>().GenerateTextAsync(new() { Request = new() { Input = "hello" } }, TestContext.Current.CancellationToken);
        Assert.Contains("updated-model", handler.Body);
        Assert.Equal("updated-key", handler.Key);
        using var updatedPayload = System.Text.Json.JsonDocument.Parse(handler.Body);
        Assert.False(updatedPayload.RootElement.GetProperty("store").GetBoolean());
    }

    sealed class RecordingHandler : HttpMessageHandler
    {
        public string? Address { get; set; }
        public string? Key { get; set; }
        public string Body { get; set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Address = request.RequestUri!.AbsoluteUri;
            Key = request.Headers.Authorization?.Parameter;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{}") };
        }
    }
}
