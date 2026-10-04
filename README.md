# Structly.AI

Typed, validated LLM output for unattended .NET workflows. Define a DTO, create an
immutable task, and receive a categorized result with response identity and usage—even
when a billed response fails output validation.

Supports **.NET 10** and OpenAI. The runtime has no NuGet dependencies. The package also
includes a C# Roslyn analyzer for early schema feedback. First publication is planned
as `Structly.AI` version `0.1.0`; until publication, use the local workflow below.

## Quick start

After publication, install with `dotnet add package Structly.AI`. Configure a model ID
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

## Guides

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

Use the SDK in `global.json`. CI runs locked restore, formatting, Release build, the
full offline suite, packing and isolated package validation on Linux and Windows:

```sh
dotnet format Structly.AI.slnx --verify-no-changes --no-restore
dotnet test --solution Structly.AI.slnx -c Release --no-build
git diff --check
```

In Codex sessions prefix shell commands with `rtk`; use `rtk proxy dotnet test` for
Microsoft.Testing.Platform output. Dependency changes require restoring with
`--force-evaluate` and committing lockfiles. Ignored `ref1/` and `ref2/` are assessment
material, excluded from the solution and packages. [Release setup and recovery](https://github.com/menk-dev/structly-ai/blob/main/docs/RELEASES.md)
remain separate from local validation; publishing is disabled until explicitly activated.

Licensed under the [MIT License](https://github.com/menk-dev/structly-ai/blob/main/LICENSE).
