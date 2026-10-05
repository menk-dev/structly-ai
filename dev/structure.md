# Source structure

Keep the existing solution boundaries: libraries in `src/`, tests in `tests/`, runnable
examples in `examples/`, and repository tools in `tools/`. Folders inside a project group
code by responsibility without adding assembly dependencies or changing public namespaces.

## Core library

| Folder in `src/Structly.AI/` | Responsibility |
| --- | --- |
| `Tasks/` | Reusable typed contracts, task settings and serialization profiles |
| `Schema/` | Schema resolution, schema representation, emission and schema exceptions |
| `Schema/Attributes/` | Public attributes for output contracts and constraints |
| `Output/` | Output validation, contract-derived instructions and example generation |
| `Requests/` | Response requests and model selection |
| `Requests/Messages/` | Conversation roles and text/image input parts |
| `Authentication/` | Explicit credential factories |
| `Results/` | Results, errors, warnings, diagnostics and retained metadata |
| `Execution/` | Public progress, usage and cancellation contracts |
| `Embeddings/` | Embedding request contracts |
| `Images/` | Image generation requests, results and option types |
| `OpenAI/` | Provider client construction and client-wide configuration |
| `OpenAI/Execution/` | Shared transport, execution budgets, request validation and envelope accounting |
| `OpenAI/Responses/` | Typed/text responses, request payloads, response processing and streaming |
| `OpenAI/Caching/` | Cache options, diagnostics and prewarming |
| `OpenAI/Embeddings/`, `OpenAI/Images/` | Provider behavior for those operations |
| `OpenAI/Files/` | File upload, inspection, download and deletion |
| `OpenAI/Batches/` | Batch preparation, lifecycle, transport and result import |

`OpenAiClient` remains one public client. Its partial files separate existing responsibilities
and share its existing execution and transport behavior. A partial filename identifies the
type and responsibility, for example `OpenAiClient.BatchPreparation.cs`.

Public types have separate files, including enums and attributes. `StructuredTask.cs`
contains the factory and `StructuredTaskOfT.cs` contains the generic contract. Substantial
internal types also have their own files. Small nested implementation details stay with
their containing type.

The public namespaces remain `Structly.AI`, `Structly.AI.OpenAI`, `Structly.AI.Embeddings`
and `Structly.AI.Imaging`. Source moves do not require consumer import changes.

## Companion projects and examples

`Structly.AI.Hosting` keeps its small set of registration, options and validation files at
the project root. `Structly.AI.Analyzers/Schema/` contains symbol-based contract validation;
the diagnostic analyzer handles registration and reporting. `Structly.AI.Testing` keeps
its small envelope-builder API together.

Example `Program.cs` files contain startup and example calls. Output models live in
`Models/`, and offline HTTP handlers live in `Support/`. Package validation copies example
sources recursively and preserves their relative paths when building a fresh consumer.

The package validation tool separates package inspection in `Packages/` from fresh-consumer
validation in `Consumers/`; its `Program.cs` coordinates the checks.

## Tests

`Structly.AI.Tests` mirrors the feature folders, including `Schema/`, `Output/`, `OpenAI/`,
`Hosting/` and `Testing/`. Test classes describe the behavior under test rather than broad
categories such as advanced or auxiliary operations. Cross-feature regression tests live
in `Regression/`.

Shared test data and fixtures live in `Support/`. Keep test-specific fixtures nested when
they help explain the test and are not reused. A shared fixture may contain small nested
models and handlers to keep their names scoped to its tests. Tests remain offline and use
controlled time and fake HTTP transports.
