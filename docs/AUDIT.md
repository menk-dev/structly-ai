# Current project audit against ref1 and ref2

Audited 2026-10-04. Scope: static comparison of the runtime and analyzer implementation,
reference contracts and implementation, documented F01–F17 requirements, plus the current
offline suite and targeted runtime reproductions. This is not a live provider certification
or an exhaustive proof of parity. No provider calls or credentials were used.

## Findings

### P1 — Progress observers can cause false streaming inactivity failures

Evidence: `src/Structly.AI/OpenAiStreaming.cs:11`, `:34`, `:89` and
`src/Structly.AI/OpenAiExecution.cs` (`Callback`).

The stream inactivity timer remains active while the reader awaits a progress observer.
An observer taking longer than InactivityTimeout cancels the operation even when the
terminal event is already buffered in the same chunk. The timer measures observer latency
as provider inactivity. Because the terminal event is never processed, its usage is lost.
This undermines F12 (stream activity) and F13 (billed-response accounting); ref1's
IProgress reporting did not await an async observer in its stream reader.

Offline reproduction: a memory-backed SSE response containing a text delta followed by a
completed response with input_tokens=10; InactivityTimeout=50 ms; delta observer awaits
150 ms, below the one-second observer cap. Actual result: InactivityExceeded and null
usage, despite the complete response already being available.

Correction: distinguish waiting for stream activity from processing observer callbacks.
Keep total and observer deadlines active, and prevent buffered terminal events from being
discarded because of callback latency. Add a regression covering buffered completion and
a slow observer, as well as an actual stalled stream.

### P2 — DTO cancellation exceptions become retryable transport failures

Evidence: `src/Structly.AI/StructuredTask.cs:156`,
`src/Structly.AI/OpenAiClient.cs:245` and `:70`.

ReadOutput explicitly excludes OperationCanceledException from its deserialization error
handling. A DTO constructor or setter throwing that exception escapes to the transport
catch, which returns TransportFailure with IsTransient=true even though neither the caller
nor the execution deadline cancelled. Direct ReadOutput instead throws the exception.
Hosts following IsTransient may repeat a billed request for a deterministic DTO problem.

Offline reproduction: valid completed provider response containing {"value":1}; a
constructor-bound DTO whose constructor throws OperationCanceledException; no cancellation
requested. Actual result: TransportFailure, IsTransient=true, input usage retained as 10.

Correction: classify host materialization exceptions as InvalidOutput when the execution
has not cancelled, while preserving actual caller/deadline precedence. Verify both direct
ReadOutput and ExecuteAsync behavior.

## Legacy coverage assessment

The principal feature groups in both references have corresponding current implementation.
No missing whole feature group was established by this audit. Presence does not establish
correctness for every interaction; the findings above qualify the existing completion claims.

| Required capabilities | Current implementation | Assessment |
| --- | --- | --- |
| F01 reusable task/startup validation | StructuredTask | Immutable replacement for registered sessions |
| F02–F04 strict schemas, constraints, vocabularies | SchemaResolver, SchemaWriter, OutputValidator | Present; explicit serialization support boundary |
| F05 generated output guidance | OutputSpecification | Shared contract and locally validated examples |
| F06 typed and free text | OpenAiClient, OpenAiResponses | Present |
| F07–F08 vision/messages, continuation/storage | AdvancedContracts, OpenAiResponses | Present; explicit storage differs from legacy defaults |
| F09–F10 models/profiles and credentials | StructuredRequest, OpenAiOptions, OpenAiClient | Explicit profiles replace legacy preference mappings |
| F11 failures/metadata/idempotency | StructuredContracts, OpenAiClient | Present; materialization exception classification issue above |
| F12 streaming/progress/deadlines | OpenAiStreaming, OpenAiExecution | Present; false inactivity issue above |
| F13 usage/raw capture/observer | OpenAiClient, ExecutionContracts | Present; terminal usage can be lost in the streaming case above |
| F14 cache options/diagnostics/prewarm | OpenAiCache, OpenAiResponses | Present; compatibility explicitly configured by model |
| F15 embeddings | OpenAiEmbeddings | Batches, dimensions, finite vectors and index ordering present |
| F16 images | OpenAiImages, ImageContracts | Count, presets/custom size, quality/background/format and base64 present |
| F17 analyzer feedback | StructuredSchemaAnalyzer | Present and covered by the offline analyzer suite |

Migration requires new API calls and attributes. General JsonSerializerOptions customization
from ref2 is deliberately reduced to supported naming profiles; automatic model preference
mapping from ref1 becomes consumer-configured model profiles. These are documented design
choices, rather than source compatibility. Consumers relying on arbitrary serializer
converters/options need a separate migration assessment. Neither reference's provider
comments nor this offline comparison prove current model/API support.

## Verification

- `rtk dotnet build Structly.AI.slnx -c Release --no-restore`: seven projects, zero errors,
  zero warnings.
- `rtk proxy dotnet test --solution Structly.AI.slnx -c Release --no-build`: 427 passed,
  zero failed/skipped. The first sandboxed invocation could not create runner IPC pipes;
  an approved elevated offline run succeeded.
- Two additional reproductions ran in an isolated console under `/tmp/structly-audit`,
  referencing the built runtime assembly; both produced the findings above.

No production changes were made. Packaging, remote release setup, Windows execution and
live provider behavior were not revalidated. Phase 7 remains pending as documented.
