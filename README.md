# Structly.AI

Structly.AI turns OpenAI output into .NET objects and checks it against your schema.
Define an output type, create a reusable task, and execute it. Each result includes
either a value or an error, along with any response IDs and token counts reported by
the provider. Token counts can still be available when the output is invalid.

Supports **.NET 10** and OpenAI. The runtime has no NuGet dependencies. The package also
includes a C# Roslyn analyzer that checks supported output types during compilation.
Version `0.2.0` adds the optional `Structly.AI.Hosting` package for ASP.NET Core and
Generic Host. Install packages from nuget.org.

## Quick start

See the [installation guide](https://github.com/menk-dev/structly-ai/blob/main/docs/INSTALLATION.md#consuming-from-your-projects)
and install with `dotnet add package Structly.AI --version 0.2.0`. Configure a model ID
supported by your account and supply credentials explicitly:

```csharp
using Structly.AI;
using Structly.AI.OpenAI;

var task = StructuredTask.Create<Ticket>(new()
{
    Instructions = "Extract the reported support issue.",
    SchemaName = "ticket"
});
using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
var client = new OpenAiClient(http, new()
{
    DefaultModel = new()
    {
        ModelId = Environment.GetEnvironmentVariable("STRUCTLY_MODEL")
            ?? throw new InvalidOperationException("Set STRUCTLY_MODEL.")
    },
    CredentialResolver = Credentials.FromEnvironment("OPENAI_API_KEY")
});
var result = await client.ExecuteAsync(task, new()
{
    Input = "Invoice INV-42 was charged twice.",
    TotalTimeout = TimeSpan.FromSeconds(60)
});
if (result.IsSuccess)
    Console.WriteLine(result.Value!.Summary);
else
    Console.WriteLine($"{result.Error!.Kind}: {result.Error.Message}");

public sealed record Ticket
{
    public required string Summary { get; init; }
    public string? Reference { get; init; }
}
```

Every included property must appear in the JSON. Nullable properties may contain `null`;
they cannot be omitted. Creating a task checks its schema. You can reuse the task for
concurrent requests. Invalid requests fail before the library reads credentials or sends
HTTP requests. Use `EnsureSuccess()` if you prefer exceptions to checking the result.
Caller cancellation throws `StructuredOperationCanceledException`, which includes any
token counts already received.

## ASP.NET Core and .NET Hosting

Use the optional `Structly.AI.Hosting` package for dependency injection, settings from
`appsettings.json`, settings validation at startup, and `IHttpClientFactory`:

```csharp
using Structly.AI.Hosting;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddStructlyOpenAi(builder.Configuration);
```

Inject `OpenAiClient` into endpoints or application services. Configure the
`Structly:OpenAI` section with a `DefaultModel`, timeouts and model profiles; supply
`Structly__OpenAI__ApiKey` through the host's environment configuration or use user secrets.
Install it with `dotnet add package Structly.AI.Hosting --version 0.2.0`.
See the [hosting guide](https://github.com/menk-dev/structly-ai/blob/main/docs/HOSTING.md)
for configuration and ASP.NET Core and worker examples.

## Guides

- [ASP.NET Core and Generic Host integration](https://github.com/menk-dev/structly-ai/blob/main/docs/HOSTING.md)
- [Client and HTTP configuration](https://github.com/menk-dev/structly-ai/blob/main/docs/CONFIGURATION.md)
- [Schema support, constraints and analyzer diagnostics](https://github.com/menk-dev/structly-ai/blob/main/docs/SCHEMAS.md)
- [Failure handling](https://github.com/menk-dev/structly-ai/blob/main/docs/FAILURES.md)
- [Cancellation, deadlines, streaming and retries](https://github.com/menk-dev/structly-ai/blob/main/docs/EXECUTION.md)
- [Token counts and usage callbacks](https://github.com/menk-dev/structly-ai/blob/main/docs/USAGE.md)
- [Guidance, messages, vision, continuation, caching, embeddings and images](https://github.com/menk-dev/structly-ai/blob/main/docs/ADVANCED.md)

Each operation sends one request. Your application chooses the model and controls HTTP
settings and retries. Check that the selected model supports the features you request.
The library does not switch models automatically or guarantee cache reuse, stored response
availability, or protection from duplicate charges.

Arbitrary JSON converters, recursive output types, dictionaries and polymorphic schemas
are unsupported. This release does not support other providers, automatic retries,
Native AOT, tool calling, image editing, or audio, video and file input.

## Run without credentials

The [consumer example](https://github.com/menk-dev/structly-ai/tree/main/examples/Structly.AI.Consumer)
uses a simulated HTTP response to demonstrate typed output, dynamic vocabularies, output
instructions, token counts and cancellation. It does not call a provider:

```sh
dotnet run --project examples/Structly.AI.Consumer -c Release
```

Build both packages and check them with a separate test application. The test application
uses a fresh package cache and installs the core package from the local feed:

```sh
dotnet restore Structly.AI.slnx --locked-mode
dotnet build Structly.AI.slnx -c Release --no-restore
dotnet pack src/Structly.AI/Structly.AI.csproj -c Release --no-build -o artifacts/packages
dotnet pack src/Structly.AI.Hosting/Structly.AI.Hosting.csproj -c Release --no-build -o artifacts/packages
dotnet run --project tools/Structly.AI.PackageValidation -c Release --no-build -- artifacts/packages
```

The validator checks both packages' contents and versions, documentation and debug symbols.
It also checks that the packaged analyzer rejects an unsupported output type.

## Development

See the [development guide](https://github.com/menk-dev/structly-ai/blob/main/dev/README.md)
for local checks, architecture and release maintenance.

Licensed under the [MIT License](https://github.com/menk-dev/structly-ai/blob/main/LICENSE).
