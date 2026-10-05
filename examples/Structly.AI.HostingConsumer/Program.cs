using Microsoft.Extensions.DependencyInjection;
using Structly.AI;
using Structly.AI.Hosting;

// Offline hosting configuration: the handler supplies the provider envelope locally.
var services = new ServiceCollection();
services.AddStructlyOpenAi(options =>
{
    options.DefaultModel = new() { ModelId = "offline-model" };
    options.CredentialResolver = Credentials.FromStatic("offline-fixture");
}).ConfigurePrimaryHttpMessageHandler(() => new OfflineHandler());

// Register the reusable task separately from provider configuration.
services.AddStructlyAi(ai => ai.AddTask<Answer>("extract", new() { Instructions = "Extract" }));

using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
var ai = provider.GetRequiredService<StructlyAi>();

// Execute a named task with the simple string overload.
var result = await ai.ExecuteTaskAsync<Answer>("extract", "input");
if(result.EnsureSuccess().Value != "hosted")
    throw new InvalidOperationException("Hosted consumer failed.");

// Use request settings when a call needs correlation metadata.
var detailed = await ai.ExecuteTaskAsync<Answer>("extract", new()
{
    Input = "input",
    CorrelationId = "offline-job",
});
if(detailed.EnsureSuccess().Value != "hosted" || detailed.Metadata.CorrelationId != "offline-job")
    throw new InvalidOperationException("Hosted request settings were not retained.");

Console.WriteLine("Offline hosted consumer passed.");
