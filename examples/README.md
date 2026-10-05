# Runnable examples

Both examples require the .NET 10 SDK from [global.json](../global.json). They use local
HTTP handlers and fake credentials; they make no provider calls.

## Core consumer

```sh
dotnet run --project examples/Structly.AI.Consumer -c Release
```

Read [Program.cs](Structly.AI.Consumer/Program.cs) for task creation, dynamic vocabularies,
schema inspection, generated fixtures, typed execution, usage and cancellation.
[BatchExamples.cs](Structly.AI.Consumer/BatchExamples.cs) demonstrates embedding and typed
batch import, manifest reconstruction and usage deduplication across repeated imports.

Output contracts are in `Models/`. The handlers in `Support/` supply strict Responses
envelopes, completed batch status and JSONL output files. The example checks the resulting
values and metadata and ends with `Offline consumer passed.`.

## Hosted consumer

```sh
dotnet run --project examples/Structly.AI.HostingConsumer -c Release
```

Read [Program.cs](Structly.AI.HostingConsumer/Program.cs) for provider registration,
named tasks, DI resolution and per-call correlation metadata. The handler in `Support/`
supplies the response locally. The example ends with `Offline hosted consumer passed.`.

For a real provider, configure a supported model and explicit credentials as shown in the
[quick start](../README.md#quick-start) or [hosting guide](../docs/hosting.md). Replace the
fake handler with your application's HTTP configuration.

The [development guide](../dev/README.md#local-verification) describes package validation,
which also builds and runs the core consumer against freshly packed packages.
