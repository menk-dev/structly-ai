# Structly.AI

Structly.AI gives you **structured output from an LLM as typed .NET objects**.
Define the C# type you need, supply instructions and input, and receive a validated
result. Use it to extract fields from text, classify messages, or generate data that
your application can consume directly.

The library generates a strict JSON schema from your output type, sends it to the
provider, and validates the returned JSON before deserializing it. A successful result
contains your typed value; a failed result contains an error. Schema validation checks
the output's structure and constraints, not the factual accuracy of the model's answer.

Supports **.NET 10**, with separate provider packages for OpenAI and
[Mistral](https://github.com/menk-dev/structly-ai/blob/main/docs/mistral.md).
The core runtime has no NuGet dependencies and includes a C# Roslyn analyzer that
checks supported output types during compilation.

## Install

Install the core package and the OpenAI provider from nuget.org for the example below:

```sh
dotnet add package Structly.AI --version 0.8.0
dotnet add package Structly.AI.OpenAI --version 0.8.0
```

Optional packages add [ASP.NET Core and Generic Host integration](https://github.com/menk-dev/structly-ai/blob/main/docs/hosting.md)
and [offline testing utilities](https://github.com/menk-dev/structly-ai/blob/main/docs/testing.md):

```sh
dotnet add package Structly.AI.Hosting --version 0.8.0
dotnet add package Structly.AI.Testing --version 0.8.0
```

See [installation and debug symbols](https://github.com/menk-dev/structly-ai/blob/main/docs/installation.md) for details.

## Quick start

This example extracts a support ticket from a customer's message.

### 1. Define the output you need

The output contract describes the fields the LLM must return:

```csharp
public sealed record Ticket
{
    public required string Summary { get; init; }

    public string? Reference { get; init; }
}
```

Every included property must appear in the JSON. `Reference` may contain `null` when
there is no reference in the input; it cannot be omitted. You can also use enums,
nested objects, collections and [field constraints](https://github.com/menk-dev/structly-ai/blob/main/docs/schemas.md).

### 2. Extract a typed result

Create a .NET 10 console application, install the packages above, and use the following
complete `Program.cs`. It includes the output type at the end because C# top-level
statements must precede type declarations. Set `STRUCTLY_MODEL` to a model ID that
supports structured output and supply your API key through `OPENAI_API_KEY`.

```csharp
using Structly.AI;
using Structly.AI.OpenAI;

var task = StructuredTask.Create<Ticket>(
    "Extract the reported support issue. Use null when no reference is mentioned.");

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

var result = await client.ExecuteAsync(task, "Invoice INV-42 was charged twice.");

if (result.IsSuccess)
{
    Console.WriteLine(result.Value.Summary);
    Console.WriteLine(result.Value.Reference);
}
else
    Console.WriteLine($"{result.Error.Kind}: {result.Error.Message}");

public sealed record Ticket
{
    public required string Summary { get; init; }

    public string? Reference { get; init; }
}
```

For this input, a successful output might have `Summary = "Invoice charged twice"`
and `Reference = "INV-42"`. Your code receives a `Ticket` instance after schema
validation and deserialization.

### 3. Reuse the task with new input

Keep the client and task for subsequent calls:

```csharp
var next = await client.ExecuteAsync(task, "I cannot sign in to my account.");
var ticket = next.EnsureSuccess();
Console.WriteLine(ticket.Summary);
```

Creating a task validates its schema locally. Clients and tasks support concurrent
requests. `EnsureSuccess()` returns the typed value or throws on failure; use the
`IsSuccess` check above when you want to handle errors explicitly.

Use a `StructuredRequest` for per-call settings such as timeouts and runtime vocabularies.
Results also retain response IDs and token counts reported by the provider, including
available usage when output validation fails. See [failure handling](https://github.com/menk-dev/structly-ai/blob/main/docs/failures.md)
for errors and cancellation, and [client configuration](https://github.com/menk-dev/structly-ai/blob/main/docs/configuration.md)
for models, credentials and defaults.

## Use with ASP.NET Core or Generic Host

The hosting package adds dependency injection, settings from `appsettings.json`, startup
validation, and `IHttpClientFactory`. Register the provider and a named task:

```csharp
using Structly.AI.Hosting;
using Structly.AI.OpenAI;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddStructlyAi(builder.Configuration, ai =>
{
    ai.ConfigureOpenAiProvider();
    ai.AddTask<Ticket>("extract-ticket", "Extract the reported support issue.");
});
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
| Define output types and constraints | [Schemas and analyzer diagnostics](https://github.com/menk-dev/structly-ai/blob/main/docs/schemas.md) |
| Configure HTTP, models and credentials | [Client configuration](https://github.com/menk-dev/structly-ai/blob/main/docs/configuration.md) |
| Use Mistral structured output | [Mistral provider](https://github.com/menk-dev/structly-ai/blob/main/docs/mistral.md) |
| Bind output settings and continue typed conversations | [Bound output and conversations](https://github.com/menk-dev/structly-ai/blob/main/docs/conversations.md) |
| Handle errors and cancellation | [Failure handling](https://github.com/menk-dev/structly-ai/blob/main/docs/failures.md) and [execution](https://github.com/menk-dev/structly-ai/blob/main/docs/execution.md) |
| Record token counts | [Usage callbacks](https://github.com/menk-dev/structly-ai/blob/main/docs/usage.md) |
| Use messages, vision, caching, embeddings or images | [Advanced operations](https://github.com/menk-dev/structly-ai/blob/main/docs/advanced.md) |
| Prepare and import batch jobs | [Batch operations](https://github.com/menk-dev/structly-ai/blob/main/docs/batches.md) |
| Test without a provider | [Offline testing](https://github.com/menk-dev/structly-ai/blob/main/docs/testing.md) |
| Update existing applications | [Provider package migration](https://github.com/menk-dev/structly-ai/blob/main/docs/migration-provider-split.md) and [0.4.0 migration](https://github.com/menk-dev/structly-ai/blob/main/docs/migration-0.4.md) |

## Scope and limitations

Each operation sends one request. Your application chooses the model and controls HTTP
settings and retries. Check that the selected model supports the features you request.
The library does not switch models automatically or guarantee cache reuse, stored response
availability, or protection from duplicate charges.

Arbitrary JSON converters, recursive output types, dictionaries and polymorphic schemas
are unsupported. The OpenAI package implements one provider. This release does not include automatic retries,
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
