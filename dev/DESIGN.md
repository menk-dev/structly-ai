# Design and tests

## Package structure

The core library targets .NET 10. It uses framework HTTP, JSON, reflection and time APIs,
with no NuGet runtime dependencies. Its package includes a `netstandard2.0` Roslyn analyzer
under `analyzers/dotnet/cs`. Compiler dependencies are private to the analyzer and its tests.
The analyzer cannot reference the .NET 10 runtime assembly because it must load in the compiler.

Tasks are immutable, and clients take explicit settings. The optional `Structly.AI.Hosting`
package registers the client with dependency injection, binds configuration and validates
settings. The core package can be used without it.

Other providers, automatic retries, Native AOT, tool calling, image editing, and audio,
video and file inputs are not currently supported. This list describes current limits;
it is not a plan to implement those features.

## Schema handling

`SchemaResolver` builds an immutable description of the task's serialization rules.
`SchemaWriter`, `OutputValidator` and the output specification partial of `StructuredTask<T>` use it to generate the JSON
schema, validate output and generate output instructions. Keep supported types consistent
with [SCHEMAS.md](../docs/SCHEMAS.md). Finding a type through reflection does not prove
that `System.Text.Json` can deserialize it.

Each request copies its vocabularies. There is no global schema cache for vocabulary data.
This avoids keeping unlimited caller data in memory or reusing another request's values.
When changing schema rules, preserve nullable enums, serialized enum names, nullable
collection items, ignored properties and constructor-bound properties. Generated examples
must pass the same validation as real output, including constraints and vocabularies.

The analyzer checks Roslyn symbols separately. Tests compile the same examples and compare
analyzer and runtime issue codes and JSON paths. Both report the first schema issue.
Vocabularies, dynamic settings, serialized schema size and provider support are checked
at runtime. See the schema guide for analyzer limits.

## Execution

`OpenAiClient` shares HTTP handling, credential resolution, error handling, timeouts and
result completion across responses, embeddings, images and prewarming. Request construction,
response parsing, streaming and operation-specific processing stay in their feature files.
Your application configures model profiles, chooses models and handles retries. When
changing provider requests, check official documentation rather than old comments or fixtures.

Preserve the behavior described in [EXECUTION.md](../docs/EXECUTION.md),
[FAILURES.md](../docs/FAILURES.md) and [USAGE.md](../docs/USAGE.md):

- Validate requests before resolving credentials or sending HTTP. Copy mutable request
  data and keep credentials, options, callbacks and vocabularies separate across calls.
- Send one attempt. Before completion, caller cancellation takes precedence over the
  total timeout, then streaming inactivity, then provider and output results.
- Read response IDs and usage before processing output. Failed output can be billed.
  Usage callbacks can fail, and missing counts must stay unknown.
- Limit callback waits and asynchronous work that ignores cancellation. Observe later
  faults and dispose late responses and streams. Synchronous application code must return promptly.
- Keep provider and application exception text out of errors and warnings. Do not log
  sensitive bodies by default. Raw response and output capture require explicit settings.

Streaming needs a final response event. Deltas or end-of-stream alone cannot produce
success. Raw capture stores the final response, not the event history. Delta callback
time pauses the inactivity timer but still counts toward total and callback timeouts.

Cancellation exceptions from output constructors or setters become nontransient
`InvalidOutput`, unless actual caller cancellation or a library timeout takes precedence.
Keep the regression tests for these rules when refactoring.

Cache settings are provider-specific and configured per model. Unsupported combinations
fail before sending, and unknown diagnostic strings are retained. Cache settings do not
guarantee reuse, storage or protection from duplicate requests.
See [ADVANCED.md](../docs/ADVANCED.md).

## Test coverage

| Area | Tests and behavior checked |
| --- | --- |
| Schema and output | `SchemaTests`, `SchemaLimitTests`, `OutputTests`, `VocabularyTests`: serialization rules, supported types, limits, constraints and separate request vocabularies |
| Responses | `OpenAI/Responses/`: request JSON, validation before credentials and HTTP, errors, metadata, text and image messages and follow-up schemas; `Output/OutputSpecificationTests` checks output instructions |
| Execution | `OpenAI/Execution/`: cancellation and timeout precedence, callback limits, slow progress, stalled reads, cleanup, usage on invalid output and concurrent calls |
| Caching | `OpenAI/Caching/`: cache controls, compatibility, diagnostics and prewarming |
| Embeddings and images | `OpenAI/Embeddings/`, `OpenAI/Images/`: complete embedding indexes, ordering and finite vectors; image count, base64, format, transparent JPEG rejection and response limits |
| Batches and test envelopes | `OpenAI/Batches/`, `Testing/`: preparation, lifecycle, ordered import, retained accounting and envelope builders |
| Hosting | `HostingTests`: configuration binding, dependency injection, settings validation, credentials, HTTP client settings and configuration reloads |
| Analyzer | `AnalyzerTests`: agreement with runtime checks, valid output types, unsupported types and migration diagnostics |
| Packages | `Structly.AI.PackageValidation`: package contents and versions, symbols, a separate test application and rejection of an unsupported output type by the packaged analyzer |

Test combinations and failure cases, such as nullable enum collections, concurrent
vocabularies, changed schemas, callback failures and usage on invalid output. Offline tests
check library behavior. They do not prove that a provider or model supports a feature today.
