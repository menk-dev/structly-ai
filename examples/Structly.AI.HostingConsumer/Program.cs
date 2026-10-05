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
var extract = new AiTaskReference<Answer>("extract");
services.AddStructlyAi(ai => ai.AddTask(extract, new()
{
    Instructions = "Extract",
    ExecutionDefaults = new() { MaxOutputTokens = 800, TotalTimeout = TimeSpan.FromSeconds(30) },
}));

using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
var ai = provider.GetRequiredService<StructlyAi>();

// Execute a typed task reference with the simple string overload.
var result = await ai.ExecuteAsync(extract, "input");
if(result.EnsureSuccess().Value != "hosted")
    throw new InvalidOperationException("Hosted consumer failed.");

// Override execution defaults when a call needs a larger output budget.
var detailed = await ai.ExecuteAsync(extract, new()
{
    Input = "input",
    CorrelationId = "offline-job",
    MaxOutputTokens = 1600,
});
if(detailed.EnsureSuccess().Value != "hosted" || detailed.Metadata.CorrelationId != "offline-job")
    throw new InvalidOperationException("Hosted request settings were not retained.");

Console.WriteLine("Offline hosted consumer passed.");
