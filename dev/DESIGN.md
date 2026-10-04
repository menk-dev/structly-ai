# Design and regression boundaries

## Architecture and scope

The runtime targets .NET 10 and uses framework HTTP, JSON, reflection and time APIs without
NuGet runtime dependencies. The same `Structly.AI` package ships a `netstandard2.0` Roslyn
analyzer under `analyzers/dotnet/cs`. Compiler dependencies remain private to the analyzer
and its tests; compiler-host compatibility prevents sharing the runtime assembly directly.

The library replaced the functional capabilities of two reference implementations without
preserving their public API. Immutable tasks and a directly configured client replace
mutable sessions and type registration. Caller-owned DI can register the concrete client.
Additional providers/frameworks, automatic retries, Native AOT, tool calling, image editing
and audio/video/file inputs remain outside the current scope. These are scope boundaries,
not an approved implementation backlog.

## Shared schema contract

`SchemaResolver` resolves serialization into a task-owned immutable contract.
`SchemaWriter`, `OutputValidator` and `OutputSpecification` consume that contract so wire
schemas, local validation and generated guidance agree. Keep supported shapes aligned with
[SCHEMAS.md](../docs/SCHEMAS.md); broad reflection discovery alone does not prove a shape
can deserialize correctly.

Vocabularies are snapshotted per request; there is no global or vocabulary-specific schema
cache. This avoids retaining unbounded caller data and mixing distinct serialization or
vocabulary inputs. Preserve nullable enum branches, enum wire names, nested element
nullability, ignored-member semantics and constructor-bound DTO support when changing rules.
Generated examples must pass local validation, including constraints and vocabularies.

The analyzer independently inspects Roslyn symbols. Parity tests compile the same snippets
and compare analyzer/runtime issue codes and serialized paths. Both fail on the first
contract issue. Runtime vocabularies, dynamic settings, serialized schema byte size and
provider compatibility remain runtime concerns; see the consumer guide for diagnostic limits.

## Execution and provider boundaries

`OpenAiClient` shares bounded transport, credentials, safe failure classification and
finalization across responses, embeddings, images and prewarming. Response construction,
terminal parsing, streaming and auxiliary operations stay in their feature files.
Model profiles are explicit caller configuration; model selection and retries belong to
the host. Provider feature support must be checked against official documentation when
changing wire behavior, rather than inherited from reference comments or old fixtures.

Preserve the policies documented in [EXECUTION.md](../docs/EXECUTION.md),
[FAILURES.md](../docs/FAILURES.md) and [USAGE.md](../docs/USAGE.md):

- Validate requests before credentials or transport; snapshot mutable request inputs and
  isolate credentials, options, callbacks and vocabulary state across concurrent calls.
- Send one attempt. Caller cancellation takes precedence over total expiration, then
  streaming inactivity, then provider/output outcomes before finalization.
- Capture available identity and usage before output processing. Refusal, incomplete or
  invalid output can be billed; observation is best effort and missing counts stay unknown.
- Bound callbacks and noncooperative async waits. Observe late faults and dispose late
  responses/streams. Synchronous host code cannot be preempted and must return promptly.
- Never expose provider/host exception text in safe errors or log sensitive bodies by
  default. Raw envelope and output retention are explicit opt-ins.

Streaming requires a terminal envelope; deltas or EOF alone cannot produce success. Raw
capture retains the terminal envelope rather than the event history. Delta observer time
pauses inactivity measurement while total and callback budgets remain active.
DTO constructor/setter cancellation exceptions become nontransient `InvalidOutput` unless
actual execution cancellation or expiration takes precedence. Both rules have regressions
from the completed audit and must survive future refactoring.

Advanced response/cache controls remain explicitly provider-specific. Compatibility is
configured per model, unsupported combinations fail locally, and unknown diagnostic strings
are retained. No cache-hit, storage, replay or deduplication guarantee is implied.
See [ADVANCED.md](../docs/ADVANCED.md) for supported wire behavior and limitations.

## Regression coverage

| Area | Tests and acceptance boundaries |
| --- | --- |
| Schema and output | `SchemaTests`, `SchemaLimitTests`, `OutputTests`, `VocabularyTests`: serialization fidelity, supported/unsupported shapes, strict limits, constraints and vocabulary isolation |
| Responses | `OpenAiClientTests`, `AdvancedResponseTests`: exact wire JSON, validation suppresses auth/send, failure taxonomy, metadata, text/messages/vision, changed-schema continuation, guidance/cache/prewarm interactions |
| Execution | `ReliabilityTests`: caller/total/inactivity precedence, observer bounds, buffered completion after slow progress, genuine stalls, cleanup, usage on failed output and concurrent isolation |
| Auxiliary operations | `AuxiliaryOperationTests`: embedding index reordering/completeness and finite vectors; image count/base64/format, transparent JPEG rejection and independent response limits |
| Analyzer | `AnalyzerTests`: runtime parity, valid DTO acceptance, unsupported shape diagnostics and legacy attribute guidance |
| Package | `Structly.AI.PackageValidation`: isolated packed consumer, bundled analyzer rejection and artifact/version integrity |

Tests should exercise interactions and failure boundaries, including nullable enum collections,
concurrent vocabularies, changed schemas, observer failure and captured usage. Offline fixtures
establish library behavior; they do not certify current provider or model support.
