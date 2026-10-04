# .NET Hosting

`Structly.AI.Hosting` registers `OpenAiClient` with dependency injection in ASP.NET Core,
worker services and Generic Host applications. It requires .NET 10. The core `Structly.AI`
package has no NuGet dependencies and can be used without hosting integration.

Install with `dotnet add package Structly.AI.Hosting --version 0.2.0` after configuring
the [GitHub Packages feed](INSTALLATION.md). You can also reference the project locally.

## Settings

Add this section to `appsettings.json` and replace the model IDs with models you can use:

```json
{
  "Structly": {
    "OpenAI": {
      "DefaultModel": { "ProfileName": "extract" },
      "Profiles": {
        "extract": { "ModelId": "your-response-model", "ReasoningEffort": "Low" }
      },
      "EmbeddingProfiles": {
        "search": { "ModelId": "your-embedding-model" }
      },
      "ImageProfiles": {
        "draw": { "ModelId": "your-image-model" }
      },
      "BaseAddress": "https://api.openai.com/v1/",
      "TotalTimeout": "00:02:00",
      "InactivityTimeout": "00:00:30",
      "MaxResponseBytes": 16777216,
      "MaxImageResponseBytes": 134217728
    }
  }
}
```

Only `DefaultModel` is required. It can instead be `{ "ModelId": "your-response-model" }`.
`CacheCompatibility` accepts a dictionary of model IDs to enum names; see
[advanced configuration](ADVANCED.md). `InactivityTimeout` applies only to streaming.

The default host configuration loads appsettings files, development user secrets,
environment variables and command-line arguments in that order. Store credentials using
`dotnet user-secrets set "Structly:OpenAI:ApiKey" "YOUR_KEY"` in development or set
`Structly__OpenAI__ApiKey` in your deployment environment. `ApiKey` can also bind from
JSON if the file is stored securely. Registration does not read `OPENAI_API_KEY` automatically.

## ASP.NET Core

```csharp
using Structly.AI;
using Structly.AI.Hosting;
using Structly.AI.OpenAI;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddStructlyOpenAi(builder.Configuration);
builder.Services.AddSingleton(StructuredTask.Create<Ticket>(new()
{
    Instructions = "Extract the reported support issue.",
    SchemaName = "ticket"
}));

var app = builder.Build();
app.MapPost("/extract", async (TicketInput input, OpenAiClient client,
    StructuredTask<Ticket> task, CancellationToken cancellationToken) =>
{
    var result = await client.ExecuteAsync(task, new() { Input = input.Text }, cancellationToken);
    return result.IsSuccess ? Results.Ok(result.Value) : Results.Problem(statusCode: 502,
        title: result.Error!.Kind.ToString(), detail: result.Error.Message);
});
app.Run();

public sealed record TicketInput(string Text);
public sealed record Ticket(string Summary);
```

## Generic Host and workers

```csharp
using Microsoft.Extensions.Hosting;
using Structly.AI.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddStructlyOpenAi(builder.Configuration);
builder.Services.AddHostedService<Worker>();
await builder.Build().RunAsync();
```

`OpenAiClient` is registered as a transient typed HTTP client. In a singleton
`BackgroundService`, inject `IServiceScopeFactory`, call `CreateScope()` for each job,
and resolve the client from that scope. Do not keep one client for the worker's entire
lifetime. Tasks can be singletons and reused across concurrent calls.

## Customization and lifetime

`AddStructlyOpenAi` returns `IHttpClientBuilder`, which you can use to configure handlers,
proxies and connection pooling. It sets `HttpClient.Timeout` to infinite so Structly.AI's
timeouts control execution. The library sends one attempt per operation. Adding an HTTP
retry handler can send more attempts and may cause additional provider charges.

```csharp
builder.Services.AddStructlyOpenAi(builder.Configuration, options =>
{
    options.CredentialResolver = Credentials.FromEnvironment("OPENAI_API_KEY");
    options.UsageObserver = async (usage, token) => await RecordUsageAsync(usage, token);
}).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    PooledConnectionLifetime = TimeSpan.FromMinutes(5)
});
```

Configure callbacks and `TimeProvider` in code. A custom credential resolver replaces
the configured `ApiKey`. Request resolvers take precedence over task resolvers, which
take precedence over the client resolver. Startup allows missing credentials so that
requests can supply their own. Execution returns `Authentication` if no selected resolver
provides a key.

To change the section use `sectionName: "MyProvider"`, or pass
`builder.Configuration.GetSection("MyProvider")` directly. Register one default client
per service collection. Read validated settings through `IOptionsMonitor<OpenAiHostingOptions>`.
Invalid model or profile selection, timeouts, endpoint URIs or size limits cause startup
to fail without contacting a provider. Resolving a client with invalid settings also fails
when there is no running host. Validation messages omit configuration values and credentials.

After a configuration reload, newly resolved clients use the updated settings. Existing
clients keep the settings they were created with, including the configured API key.
Custom resolvers can read credentials on each operation. Do not modify options objects
retrieved from DI. Options validation rejects invalid reloaded settings.

`IHttpClientFactory` pools and disposes HTTP handlers. `OpenAiClient` does not dispose
the `HttpClient` passed to it. Follow Microsoft's
[typed client lifetime guidance](https://learn.microsoft.com/en-us/dotnet/core/extensions/httpclient-factory#avoid-typed-clients-in-singleton-services).
