# .NET Hosting

The optional `Structly.AI.Hosting` package targets .NET 10 and integrates with ASP.NET
Core, worker services and Generic Host. The core `Structly.AI` package remains dependency-free.
Install the hosting package from the next release or reference its project locally.

## Settings

Put this section in `appsettings.json`, replacing model IDs with your own selections:

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
`CacheCompatibility` also binds as a dictionary of model IDs to enum names; see
[advanced configuration](ADVANCED.md). InactivityTimeout applies only to streaming.

The host loads configuration in its normal order: appsettings files, user secrets in
Development, environment variables and command-line arguments. Store credentials using
`dotnet user-secrets set "Structly:OpenAI:ApiKey" "YOUR_KEY"` in development or set
`Structly__OpenAI__ApiKey` in your deployment environment. `ApiKey` can also bind from
JSON when the file is supplied securely. Registration does not implicitly read `OPENAI_API_KEY`.

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

Clients are transient typed HTTP clients. Resolve `OpenAiClient` within a DI scope for
each unit of work in a singleton `BackgroundService`; inject `IServiceScopeFactory`
and use `CreateScope()` rather than capturing a client for the worker's entire lifetime.
Tasks can be registered as singletons and reused concurrently.

## Customization and lifetime

`AddStructlyOpenAi` returns `IHttpClientBuilder` for handlers, proxy settings and connection
pool policy. It sets `HttpClient.Timeout` to infinite so library execution budgets apply.
Do not add automatic HTTP retries unless your application explicitly accepts retry and
billing semantics. The library still sends one attempt per operation.

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

Runtime callbacks and `TimeProvider` are configured in code. A custom credential resolver
replaces configured `ApiKey`; request and task resolvers retain their higher precedence.
Missing credentials are allowed at startup for request-specific credentials and produce
`Authentication` at execution when no resolver supplies a key.

To change the section use `sectionName: "MyProvider"`, or pass
`builder.Configuration.GetSection("MyProvider")` directly. Register one default client
per service collection. `IOptionsMonitor<OpenAiHostingOptions>` exposes validated settings.
Invalid model/profile selection, deadlines, endpoint URIs or size limits fail host startup
without contacting a provider. Invalid settings also fail client resolution without a host.
Validation messages omit configuration values and credentials.

When a configuration provider signals reload, newly resolved clients use the new settings.
Existing clients retain their original snapshot, including the configured API key. Custom
credential resolvers may resolve fresh credentials per operation. Do not mutate options
objects retrieved from DI. Invalid reloads are rejected by options validation.

The factory pools and disposes HTTP handlers; the core client keeps its existing caller-owned
transport contract. Follow Microsoft's [typed client lifetime guidance](https://learn.microsoft.com/en-us/dotnet/core/extensions/httpclient-factory#avoid-typed-clients-in-singleton-services).
