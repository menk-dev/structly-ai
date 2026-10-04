# Structly.AI

Typed, validated LLM output for unattended .NET workflows. Define a DTO, create an
immutable task, and receive a categorized result with response identity and usage—even
when a billed response fails output validation.

Supports **.NET 10** and OpenAI. The runtime has no NuGet dependencies. The package also
includes a C# Roslyn analyzer for early schema feedback. Available as `Structly.AI`
version `0.1.0` on GitHub Packages; configure the authenticated feed below or use the local workflow.

## Quick start

For GitHub Packages, [configure the authenticated package feed](https://github.com/menk-dev/structly-ai/blob/main/docs/INSTALLATION.md#consuming-from-your-projects)
and install with `dotnet add package Structly.AI --version 0.1.0`. Configure a model ID
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

Every included property must appear in the JSON; nullable properties permit explicit
`null`. Tasks validate schemas at creation and can be reused concurrently. Invalid
requests fail before credential resolution or HTTP. `EnsureSuccess()` is available
when exceptions fit your host; caller cancellation throws
`StructuredOperationCanceledException`, retaining any captured usage.

## ASP.NET Core and .NET Hosting

Use the optional `Structly.AI.Hosting` package for dependency injection, settings from
`appsettings.json`, startup validation and factory-managed HTTP handlers:

```csharp
using Structly.AI.Hosting;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddStructlyOpenAi(builder.Configuration);
```

Inject `OpenAiClient` into endpoints or application services. Configure the
`Structly:OpenAI` section with a `DefaultModel`, timeouts and model profiles; supply
`Structly__OpenAI__ApiKey` through the host's environment configuration or use user secrets.
See the [hosting guide](https://github.com/menk-dev/structly-ai/blob/main/docs/HOSTING.md) for complete JSON and ASP.NET Core/worker examples.
This package is built alongside the core library; install it from the next release
or use a project reference locally.

## Guides

- [ASP.NET Core and Generic Host integration](https://github.com/menk-dev/structly-ai/blob/main/docs/HOSTING.md)
- [Configuration and transport ownership](https://github.com/menk-dev/structly-ai/blob/main/docs/CONFIGURATION.md)
- [Schema support, constraints and analyzer diagnostics](https://github.com/menk-dev/structly-ai/blob/main/docs/SCHEMAS.md)
- [Failure handling](https://github.com/menk-dev/structly-ai/blob/main/docs/FAILURES.md)
- [Cancellation, deadlines, streaming and retries](https://github.com/menk-dev/structly-ai/blob/main/docs/EXECUTION.md)
- [Usage accounting](https://github.com/menk-dev/structly-ai/blob/main/docs/USAGE.md)
- [Guidance, messages, vision, continuation, caching, embeddings and images](https://github.com/menk-dev/structly-ai/blob/main/docs/ADVANCED.md)

The library sends one attempt. The host owns retries, model selection and transport
policy. Provider features depend on the selected model; no model fallback, cache-hit,
storage-availability or duplicate-billing guarantee is implied. Arbitrary JSON converters,
recursive DTOs, dictionaries and polymorphic schemas are unsupported. Additional providers,
frameworks, automatic retries, Native AOT, tool calling, image editing and audio/video/file
input are outside the current release scope.

## Run without credentials

The [consumer example](https://github.com/menk-dev/structly-ai/tree/main/examples/Structly.AI.Consumer)
uses a simulated HTTP response, exercises typed output, dynamic vocabularies, guidance,
accounting and cancellation, and never calls a provider:

```sh
dotnet run --project examples/Structly.AI.Consumer -c Release
```

Verify the actual NuGet artifact in an isolated consumer with a fresh package cache
and a local feed only:

```sh
dotnet restore Structly.AI.slnx --locked-mode
dotnet build Structly.AI.slnx -c Release --no-restore
dotnet pack src/Structly.AI/Structly.AI.csproj -c Release --no-build -o artifacts/packages
dotnet run --project tools/Structly.AI.PackageValidation -c Release --no-build -- artifacts/packages
```

The validator also checks metadata, XML documentation, symbols, source information,
version consistency and a failing DTO diagnosed by the packed analyzer.

## Development

See the [development guide](https://github.com/menk-dev/structly-ai/blob/main/dev/README.md)
for local checks, architecture and release maintenance.

Licensed under the [MIT License](https://github.com/menk-dev/structly-ai/blob/main/LICENSE).
