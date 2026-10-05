# Documentation

Structly.AI requires .NET 10. Start with the [README quick start](../README.md#quick-start)
for a typed request, then choose a guide for your application.

## Getting started

- [Installation and debug symbols](installation.md): core, hosting and testing packages.
- [Client configuration](configuration.md): HTTP lifetime, models, credentials and defaults.
- [.NET Hosting](hosting.md): ASP.NET Core endpoints, named tasks and singleton workers.
- [Schemas and analyzer diagnostics](schemas.md): output types, constraints and vocabularies.

## Execution and results

- [Bound output and conversations](conversations.md): capture output settings, continue turns and branch.
- [Failure handling](failures.md): result checks, error kinds and exceptions.
- [Execution](execution.md): cancellation, deadlines, streaming, callbacks and retries.
- [Token counts and usage callbacks](usage.md): accounting metadata and captured data.

## Additional operations

- [Messages and output instructions](advanced.md#messages-and-output-instructions)
- [Manual follow-up requests](advanced.md#follow-up-requests)
- [Free text](advanced.md#free-text)
- [Cache settings and prewarming](advanced.md#cache-settings)
- [Embeddings and images](advanced.md#embeddings-and-images)
- [Batch files and results](batches.md): preparation, submission, restart and import.

## Testing and upgrades

- [Runnable examples](../examples/README.md): core and hosted consumers with simulated HTTP.
- [Offline testing](testing.md): provider envelopes and explicit fixture completion.
- [Migrating to 0.4.0](migration-0.4.md): changes for existing applications.

For repository builds, package validation and releases, see the [development guide](../dev/README.md).

- [Provider package migration](migration-provider-split.md)

- [Mistral provider](mistral.md)
