using Microsoft.Extensions.DependencyInjection;
using Structly.AI;
using Structly.AI.Hosting;
using Structly.AI.OpenAI;

// Offline hosting configuration: the handler supplies the provider envelope locally.
var services = new ServiceCollection();
services.AddStructlyOpenAi(options =>
{
    options.DefaultModel = new() { ModelId = "offline-model" };
    options.CredentialResolver = Credentials.FromStatic("offline-fixture");
}).ConfigurePrimaryHttpMessageHandler(() => new OfflineHandler());
using var provider = services.BuildServiceProvider();
var client = provider.GetRequiredService<OpenAiClient>();
var task = StructuredTask.Create<Answer>(new());
var result = await client.ExecuteAsync(task, new() { Input = "input", Instructions = "Extract" });
if(result.EnsureSuccess().Value != "hosted")
    throw new InvalidOperationException("Hosted consumer failed.");
Console.WriteLine("Offline hosted consumer passed.");
