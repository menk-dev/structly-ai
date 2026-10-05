using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Structly.AI.Hosting;
using Structly.AI.OpenAI;
using System.Net;

namespace Structly.AI.Tests;

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
