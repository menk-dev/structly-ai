# .NET Hosting

`Structly.AI.Hosting` registers a selected `IStructuredClient` provider
with dependency injection in ASP.NET Core,
worker services and Generic Host applications. It requires .NET 10. The core `Structly.AI`
package has no NuGet dependencies. Reference `Structly.AI.OpenAI` to use OpenAI;
it depends only on core and can be used without hosting integration.

Install `Structly.AI.OpenAI` and `Structly.AI.Hosting` with matching versions.
Use `dotnet add package Structly.AI.Hosting --version 0.6.0` from
[nuget.org](installation.md). You can also reference the project locally.

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
[advanced configuration](advanced.md). `InactivityTimeout` applies only to streaming.

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

builder.Services.AddStructlyAi(builder.Configuration, ai =>
{
    ai.ConfigureOpenAiProvider();
    ai.AddTask<Ticket>(
        "extract-ticket", "Extract the reported support issue.");
});

var app = builder.Build();
app.MapPost("/extract", async (TicketInput input, StructlyAi ai,
    CancellationToken cancellationToken) =>
{
    var result = await ai.ExecuteTaskAsync<Ticket>("extract-ticket", input.Text, cancellationToken);

    if (result.IsSuccess)
        return Results.Ok(result.Value);

    return Results.Problem(
        statusCode: 502,
        title: result.Error!.Kind.ToString(),
        detail: result.Error.Message);
});
app.Run();

public sealed record TicketInput(string Text);
public sealed record Ticket(string Summary);
```

## Named AI tasks

`AddStructlyAi` constructs and validates task definitions during registration, then
registers one singleton `StructlyAi` service. Register it once per service collection.
Names must be nonblank, unique across output types, and case-sensitive. Multiple names
can return the same output type. The builder cannot be modified after its callback returns,
and definitions remain fixed for the service lifetime. Select exactly one provider inside
the registration callback with `ConfigureOpenAiProvider`. Missing or duplicate provider
selection throws. Provider option callbacks run after configuration binding.

Use an options object to configure a task, or register an existing task or bound output.
For example, replace the earlier `AddStructlyAi` registration with:

```csharp
builder.Services.AddStructlyAi(builder.Configuration, ai =>
{
    ai.ConfigureOpenAiProvider();
    ai.AddTask<Ticket>("extract-ticket", new()
    {
        Instructions = "Extract the reported support issue.",
        ModelSelection = new() { ProfileName = "extract" }
    });
    ai.AddTask("review-ticket", reviewTask);
    ai.AddTask("classify-ticket", classifierTask.BindOutput(new()
    {
        Vocabularies = vocabularies
    }));
});
```

Here `reviewTask`, `classifierTask`, and `vocabularies` are application-defined contracts
and vocabulary values. Unbound tasks accept per-call vocabularies and output specifications.
Bound outputs use their captured values and reject those per-call overrides.

`ExecuteTaskAsync<T>` returns the existing `StructuredResult<T>`, retaining usage,
warnings, and cancellation metadata. The requested `T` must match the registered type
exactly. Unknown names throw `KeyNotFoundException`; mismatched types throw
`InvalidOperationException`, before resolving a client or accessing credentials or HTTP.
Registration and lookup errors are programming errors, rather than provider error results.

The string overload supplies only input. Use a `StructuredRequest` for correlation IDs,
streaming, callbacks, deadlines, instructions, model selection, and other per-call settings.
Existing override precedence and failure behavior apply. Options/request overloads have
higher overload-resolution priority so target-typed `new()` calls remain unambiguous;
this behavior requires C# 13 or later (the default compiler for .NET 10 supports it).

For locally constructed tasks and operations other than named structured calls, inject
`OpenAiClient` directly and use the provider APIs. Named tasks do not cache provider responses
or provide automatic task-definition caching.

## Optional typed task references

Use `AiTaskReference<T>` to carry the name and output type together. The string APIs
remain available, and both forms use the same registered definitions.

```csharp
public static class TicketTasks
{
    public static readonly AiTaskReference<Ticket> Extract = new("extract-ticket");
}
```

Register the definition with the reference, including reusable execution settings:

```csharp
builder.Services.AddStructlyAi(builder.Configuration, ai =>
{
    ai.ConfigureOpenAiProvider();
    ai.AddTask(TicketTasks.Extract, new()
    {
        Instructions = "Extract the reported support issue.",
        ModelSelection = new() { ProfileName = "extract" },
        ExecutionDefaults = new()
        {
            MaxOutputTokens = 800,
            TotalTimeout = TimeSpan.FromSeconds(30)
        }
    });
});

var result = await ai.ExecuteAsync(TicketTasks.Extract, text, cancellationToken);
var detailed = await ai.ExecuteAsync(TicketTasks.Extract, new()
{
    Input = text,
    MaxOutputTokens = 1600,
    CorrelationId = ticketId
}, cancellationToken);
```

`T` is inferred from the reference. References can also register instruction strings,
existing tasks, or bound output. Names remain case-sensitive and unique across output
types, including when mixing typed and string registration. A reference is an identity,
not a definition or a client: a new reference with the same name and type resolves the
same task. Unknown names and conflicting output types still throw before client resolution.
The constructor rejects blank names. The immutable name cannot be changed after construction.

See [task execution defaults](execution.md#task-execution-defaults) for precedence and limits.

## Using configured profiles

The `/extract` endpoint above uses the `extract` profile because neither the task nor
the request sets `ModelSelection`. The configured `DefaultModel.ProfileName` selects
that entry from `Profiles`, including its model ID and reasoning effort.

To select a profile for a reusable task, set `ModelSelection` during registration.
Use this in place of the earlier task registration:

```csharp
builder.Services.AddStructlyAi(builder.Configuration, ai =>
{
    ai.ConfigureOpenAiProvider();
    ai.AddTask<Ticket>("extract-ticket", new()
    {
        Instructions = "Extract the reported support issue.",
        ModelSelection = new() { ProfileName = "extract" }
    });
});
```

To select a profile for one call, set it on the request passed to `StructlyAi`:

```csharp
var result = await ai.ExecuteTaskAsync<Ticket>("extract-ticket", new()
{
    Input = "The application crashes when I open settings.",
    ModelSelection = new() { ProfileName = "extract" }
}, cancellationToken);
```

Request selection overrides task selection, which overrides `DefaultModel`. Select
exactly one `ProfileName` or `ModelId`. Profile names are case-sensitive and must match
a configured entry. Structured output, free text and prewarming use `Profiles`.

Embeddings and images require their own request selection; they do not use
`DefaultModel`. The `search` and `draw` entries in the settings above are used as follows:

```csharp
using Structly.AI.Embeddings;
using Structly.AI.Imaging;
using Structly.AI.OpenAI;

// Inject OpenAiClient as client for embedding and image operations.
var embedded = await client.EmbedAsync(new EmbeddingRequest
{
    Inputs = ["The application crashes when I open settings."],
    ModelSelection = new() { ProfileName = "search" }
}, cancellationToken);

var images = await client.GenerateImagesAsync(new ImageGenerationRequest
{
    Prompt = "A simple illustration of a support ticket.",
    ModelSelection = new() { ProfileName = "draw" }
}, cancellationToken);
```

`search` is resolved from `EmbeddingProfiles`, and `draw` from `ImageProfiles`.
These profiles and requests do not support reasoning effort settings.

## Generic Host and workers

```csharp
using Microsoft.Extensions.Hosting;
using Structly.AI.Hosting;
using Structly.AI.OpenAI;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddStructlyAi(builder.Configuration, ai =>
{
    ai.ConfigureOpenAiProvider();
    ai.AddTask<Ticket>(
        "extract-ticket", "Extract the reported support issue.");
});
builder.Services.AddHostedService<Worker>();
await builder.Build().RunAsync();
```

Inject `StructlyAi` into a singleton `BackgroundService` and call
`ExecuteTaskAsync<Ticket>("extract-ticket", input, cancellationToken)` for each job.
The service creates an async DI scope, resolves a fresh client, awaits execution, and
disposes the scope for each call. Concurrent calls have independent scopes and execution
state. Subsequent calls observe reloaded provider settings without replacing `StructlyAi`.

For direct provider API usage, `OpenAiClient` is registered as a transient typed HTTP client.
In a singleton `BackgroundService`, inject `IServiceScopeFactory`, call `CreateScope()` for each job,
and resolve the client from that scope. Do not keep one client for the worker's entire
lifetime. Tasks can be singletons and reused across concurrent calls.

## Customization and lifetime

`AddStructlyAi` returns `IHttpClientBuilder`, which you can use to configure handlers,
proxies and connection pooling. It sets `HttpClient.Timeout` to infinite so Structly.AI's
timeouts control execution. The library sends one attempt per operation. Adding an HTTP
retry handler can send more attempts and may cause additional provider charges.

```csharp
builder.Services.AddStructlyAi(builder.Configuration, ai => ai.ConfigureOpenAiProvider(options =>
{
    options.CredentialResolver = Credentials.FromEnvironment("OPENAI_API_KEY");
    options.UsageObserver = async (usage, token) => await RecordUsageAsync(usage, token);
})).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    PooledConnectionLifetime = TimeSpan.FromMinutes(5)
});
```

Configure callbacks and `TimeProvider` in code. A custom credential resolver replaces
the configured `ApiKey`. Request resolvers take precedence over task resolvers, which
take precedence over the client resolver. Startup allows missing credentials so that
requests can supply their own. Execution returns `CredentialsMissing` if no selected resolver
provides a key.

To change the root section use `sectionName: "MyProvider"`; OpenAI binds
`MyProvider:OpenAI`. Register one provider
per service collection. Read validated settings through `IOptionsMonitor<OpenAiOptions>`.
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

## Callback-only registration and environment fallback

```csharp
services.AddStructlyAi(ai => ai.ConfigureOpenAiProvider(options =>
{
    options.DefaultModel = new() { ModelId = "gpt-6-sol" };
    options.UseEnvironmentApiKey = true;
}));
```

This overload uses the same startup validation and HTTP registration as section binding.
`UseEnvironmentApiKey` defaults to false. The client chooses a custom resolver first,
then a nonblank configured `ApiKey`, then an environment resolver when enabled.
The environment resolver reads `OPENAI_API_KEY` on each execution, so existing clients
observe changes. A selected resolver returning blank does not fall back.

For an explicit root section, use `services.AddStructlyAi(configuration, ai => ai.ConfigureOpenAiProvider(), sectionName: "MyProvider")`.

Use `OpenAiRequest` for provider response controls; plain `StructuredRequest` uses defaults.
Custom providers implement `IStructuredClient` and `IStructuredClientFactory<TClient, TOptions>`
and select a typed `AiProviderDescriptor<TClient, TOptions>` through `ConfigureProvider`.
Validation must not send HTTP or resolve credentials. See [migration](migration-provider-split.md).

The runnable [offline hosted consumer](../examples/Structly.AI.HostingConsumer/Program.cs)
uses callback registration with a fake HTTP handler and no provider connection.

## DI usage observers

Implement `IStructuredUsageObserver` to record usage using DI dependencies:

```csharp
builder.Services.AddStructlyAi(builder.Configuration, ai =>
    ai.ConfigureOpenAiProvider(options =>
        options.DefaultModel = new() { ModelId = "your-model" }))
    .AddUsageObserver<LlmUsageRecorder>();

sealed class LlmUsageRecorder(UsageLedger ledger) : IStructuredUsageObserver
{
    public ValueTask ObserveAsync(StructuredUsageEvent usage, CancellationToken token)
        => ledger.RecordAsync(usage, token);
}
```

`UsageLedger` and its recording method are application code. Register the ledger and its
dependencies in DI. `AddUsageObserver<T>()` registers the concrete observer as scoped unless
it is already registered. Only one DI observer can be registered for the Structly OpenAI
client; a second registration throws.

Every notification creates a separate async scope, resolves the observer, awaits its method
and disposes the scope. It does not share the caller's request scope. Existing singleton
observer registrations must support concurrent callbacks. Request and batch-import observers
override the DI observer, which overrides `OpenAiOptions.UsageObserver`; only the
selected observer runs. The existing best-effort callback timeout and warning behavior applies.
