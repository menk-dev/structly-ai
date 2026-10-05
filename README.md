# Structly.AI

Structly.AI turns OpenAI output into .NET objects and checks it against your schema.
Define an output type, create a reusable task, and execute it. Each result includes
either a value or an error, along with any response IDs and token counts reported by
the provider. Token counts can still be available when the output is invalid.

Supports **.NET 10** and OpenAI. The core runtime has no NuGet dependencies and includes
a C# Roslyn analyzer that checks supported output types during compilation.

## Install

Install the core package from nuget.org:

```sh
dotnet add package Structly.AI --version 0.5.0
```

Optional packages add [ASP.NET Core and Generic Host integration](https://github.com/menk-dev/structly-ai/blob/main/docs/hosting.md)
and [offline testing utilities](https://github.com/menk-dev/structly-ai/blob/main/docs/testing.md):

```sh
dotnet add package Structly.AI.Hosting --version 0.5.0
dotnet add package Structly.AI.Testing --version 0.5.0
```

See [installation and debug symbols](https://github.com/menk-dev/structly-ai/blob/main/docs/installation.md) for details.

## Quick start

The following snippets belong in the same console application's `Program.cs`, in the
order shown. Configure a model ID supported by your account in `STRUCTLY_MODEL` and
supply credentials through `OPENAI_API_KEY`.

### 1. Define a reusable task

```csharp
using Structly.AI;
using Structly.AI.OpenAI;

var task = StructuredTask.Create<Ticket>("Extract the reported support issue.");
```

### 2. Configure the client

```csharp
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
```

### 3. Execute and inspect the result

```csharp
var result = await client.ExecuteAsync(task, new()
{
    Input = "Invoice INV-42 was charged twice.",
    TotalTimeout = TimeSpan.FromSeconds(60)
});

if (result.IsSuccess)
    Console.WriteLine(result.Value.Summary);
else
    Console.WriteLine($"{result.Error.Kind}: {result.Error.Message}");
```

### 4. Declare the output type

```csharp
public sealed record Ticket
{
    public required string Summary { get; init; }

    public string? Reference { get; init; }
}
```

Every included property must appear in the JSON. Nullable properties may contain `null`;
they cannot be omitted. Creating a task checks its schema, and the task can be reused for
concurrent requests. Invalid requests fail before credentials are read or HTTP requests
are sent. Use `EnsureSuccess()` if you prefer exceptions to checking the result.
Caller cancellation throws `StructuredOperationCanceledException`, which includes any
available token counts. See [failure handling](https://github.com/menk-dev/structly-ai/blob/main/docs/failures.md).

For text input with default execution settings, use
`await client.ExecuteAsync(task, text, cancellationToken)`. The same overload accepts
bound output. Use a `StructuredRequest` when supplying per-call settings.

## Use with ASP.NET Core or Generic Host

The hosting package adds dependency injection, settings from `appsettings.json`, startup
validation, and `IHttpClientFactory`. Register the provider and a named task:

```csharp
using Structly.AI.Hosting;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddStructlyOpenAi(builder.Configuration);

builder.Services.AddStructlyAi(ai => ai.AddTask<Ticket>(
    "extract-ticket", "Extract the reported support issue."));
```

In an endpoint or service with an injected `StructlyAi ai`, execute the task:

```csharp
var result = await ai.ExecuteTaskAsync<Ticket>(
    "extract-ticket", text, cancellationToken);
```

Optionally use `AiTaskReference<T>` to keep task names and output types together and let
execution infer the result type. Tasks can also carry timeout and output-token defaults;
per-call settings override them. See the [typed reference example](https://github.com/menk-dev/structly-ai/blob/main/docs/hosting.md#optional-typed-task-references).

Configure `Structly:OpenAI:DefaultModel` and supply `Structly__OpenAI__ApiKey` through
environment configuration or user secrets. Definitions are validated during registration;
each execution resolves a fresh client in its own DI scope, including calls from singleton
workers. The [hosting guide](https://github.com/menk-dev/structly-ai/blob/main/docs/hosting.md) includes complete startup and endpoint
examples, model profiles, credentials, and worker lifetime rules.

## Documentation

Start with the [documentation index](https://github.com/menk-dev/structly-ai/blob/main/docs/README.md) or choose a guide:

| Goal | Guide |
| --- | --- |
| Configure HTTP, models and credentials | [Client configuration](https://github.com/menk-dev/structly-ai/blob/main/docs/configuration.md) |
| Define output types and constraints | [Schemas and analyzer diagnostics](https://github.com/menk-dev/structly-ai/blob/main/docs/schemas.md) |
| Bind output settings and continue typed conversations | [Bound output and conversations](https://github.com/menk-dev/structly-ai/blob/main/docs/conversations.md) |
| Handle errors and cancellation | [Failure handling](https://github.com/menk-dev/structly-ai/blob/main/docs/failures.md) and [execution](https://github.com/menk-dev/structly-ai/blob/main/docs/execution.md) |
| Record token counts | [Usage callbacks](https://github.com/menk-dev/structly-ai/blob/main/docs/usage.md) |
| Use messages, vision, caching, embeddings or images | [Advanced operations](https://github.com/menk-dev/structly-ai/blob/main/docs/advanced.md) |
| Prepare and import batch jobs | [Batch operations](https://github.com/menk-dev/structly-ai/blob/main/docs/batches.md) |
| Test without a provider | [Offline testing](https://github.com/menk-dev/structly-ai/blob/main/docs/testing.md) |
| Update existing applications | [0.4.0 migration](https://github.com/menk-dev/structly-ai/blob/main/docs/migration-0.4.md) |

## Scope and limitations

Each operation sends one request. Your application chooses the model and controls HTTP
settings and retries. Check that the selected model supports the features you request.
The library does not switch models automatically or guarantee cache reuse, stored response
availability, or protection from duplicate charges.

Arbitrary JSON converters, recursive output types, dictionaries and polymorphic schemas
are unsupported. This release does not support other providers, automatic retries,
Native AOT, tool calling, image editing, or audio, video and file input.

## Run the offline examples

The [consumer example](https://github.com/menk-dev/structly-ai/tree/main/examples/Structly.AI.Consumer)
uses simulated HTTP responses to demonstrate typed output, dynamic vocabularies, output
instructions, token counts and cancellation. It does not call a provider:

```sh
dotnet run --project examples/Structly.AI.Consumer -c Release
```

The [examples guide](https://github.com/menk-dev/structly-ai/blob/main/examples/README.md)
also covers the hosted consumer and explains the source layout.

## Development

See the [development guide](https://github.com/menk-dev/structly-ai/blob/main/dev/README.md) for local checks, package validation,
architecture and release maintenance.

Licensed under the [MIT License](https://github.com/menk-dev/structly-ai/blob/main/LICENSE).
