# Phase 5 handoff

Status: complete, 2026-10-04. Phase 6 has not started.

## Delivered behavior

The required runtime union F01–F16 is implemented and documented. New capabilities:
contract-derived field guidance/validated examples; free text with streaming; ordered
multimodal message histories; explicit continuation/storage; per-operation model profiles;
modern/legacy cache controls, comparison diagnostics and metadata-only prewarm; float-batch
embeddings; and base64 Image API generation. Analyzer feedback (F17) remains phase 6.

Every endpoint shares phase 4's total-budget/cancellation/observer/cleanup rules and
single-attempt transport. Usage is retained before status/output processing, including
failed vector/image validation. No retries, live credentials, provider calls, publishing,
new dependencies or lockfile changes were introduced.

The working tree was clean at entry. Phase 4 prerequisites were checked against the
actual runtime and regression suite. Both ignored references were present and consulted:
ref1 text/provider, embedding/image models, parsers and tests; ref2 messages/cache/options,
specification generator and provider/specification tests. The audit found no additional
runtime feature outside the F01–F16 map. Reference differences corrected include missing/
duplicate embedding indexes, unvalidated base64, fabricated resolved image models and
samples that disagree with serialization/constraints. Reference trees remain outside
the solution, tracked files and package artifacts.

## Decisions and changed areas

TextRequest and OpenAiPrewarmRequest compose response controls in Request, adding optional
instructions. The typed prewarm overload uses the task's current schema, instructions,
model and credentials. EmbeddingRequest and ImageGenerationRequest expose only applicable
auth/budget/accounting settings and require explicit model/profile selection. OpenAiClient
uses Profiles for response/text/prewarm, EmbeddingProfiles and ImageProfiles for auxiliary
operations, all snapshotted. Auxiliary reasoning settings fail locally.

StructuredRequest accepts exactly one of Input or Messages. String shorthand keeps its
existing provider wire form until guidance requires a user content list; this is equivalent
to one user message. Roles/content order and supplied image URLs/base64 are preserved.
Guidance appends to the final user message. Current task instructions/schema are resent
on continuation, new type/vocabularies are supported, and Store defaults to false. The
host owns available prior IDs and storage policy; there is no local session/history.

OutputSpecification uses the immutable contract/resolved schema for field paths,
descriptions, constraints and examples. Samples must pass ReadOutput, including DTO
construction. Unsupported sample generation requires caller ExampleJson or disabling
examples; no invalid sample is emitted. Node/text/per-array bounds prevent multiplicative
collection expansion. Guidance inspection throws actionable ArgumentException; execution
returns InvalidRequest before auth. Vocabulary/schema errors retain UnsupportedSchema.

CacheCompatibility explicitly declares Modern or Legacy by exact selected model ID;
the library does not infer capabilities from names. Modern options/breakpoints/prewarm
and legacy retention are mutually exclusive by conservative library policy. Modern TTL
accepts only 30m. Explicit mode needs 1–4 markers; implicit/default mode accepts at most
three explicit markers, reserving one of the provider's four write slots. Unknown support
and unsupported combinations are rejected locally. Diagnostics retain unknown strings
and valid counts, warning on malformed counts without masking output. Prewarm is separate,
requires completed empty output, and rejects generation/progress/continuation/storage
combinations. No key/TTL/prewarm/comparison guarantees a cache write or reuse.

Embedding output requires complete unique indexes, consistent nonempty finite vectors
and requested dimensions when specified; immutable vectors return in input order. Image
generation supports count/presets/custom override/quality/background/format, suppresses
transparent JPEG, checks count/base64/reported format/file signatures and preserves order.
Signature checks do not fully decode image integrity. Positive custom dimensions are sent
for model-specific provider validation. Images have independent MaxImageResponseBytes
(128 MiB default); other envelopes remain bounded by MaxResponseBytes (16 MiB). Missing
image model/response identity stays unknown. Embedding prompt_tokens and image text/image
input/output token details are retained independently without inferred counts.

Runtime additions are in AdvancedContracts.cs, OutputSpecification.cs, OpenAiCache.cs,
EmbeddingContracts.cs, ImageContracts.cs and feature-specific OpenAiResponses/Embeddings/
Images partials. OpenAiClient's shared send/finalization path now serves all operations.
Options, requests and accounting metadata were extended. AdvancedResponseTests and
AuxiliaryOperationTests add 134 cases to the prior 205-test regression suite.

Official OpenAI documentation was checked through OpenAI Docs; exact sources and provider
assumptions are recorded in [DESIGN.md](DESIGN.md). [ADVANCED.md](ADVANCED.md) supplies examples
and feature rules; [EXECUTION.md](EXECUTION.md) now covers all operations. The API-key skill
was read, but the explicitly authorized offline/credential-free scope requires no key
inspection, provisioning or paid calls. No credentials were inspected or recorded.

## Runtime acceptance audit

Each required runtime capability has production code, guidance in DESIGN/EXECUTION/
ADVANCED and passing acceptance coverage. Existing phase 2–4 behavior is included in
the final suite, not assumed complete from status labels.

| ID | Acceptance evidence |
| --- | --- |
| F01 | Schema/startup diagnostics and ReliabilityTests concurrent immutable task/client reuse. |
| F02 | SchemaTests, OutputTests, SchemaLimitTests support/serialization/nullability/limits matrix. |
| F03 | Constraint schema/output tests plus guidance with constrained strings, numbers and arrays. |
| F04 | VocabularyTests isolation plus concurrent guidance/nullable vocabulary and enum collections. |
| F05 | Guidance uses serialized names/descriptions/full constraints; generated and caller examples validated; switches and bounded expansion. |
| F06 | Typed regression path plus buffered/streamed text, schema omission, terminal/refusal/blank output failures and accounting. |
| F07 | Exact system/developer/user/assistant ordering, text/image parts/detail/data URLs, invalid content suppression and snapshots. |
| F08 | Explicit storage/previous ID, resent instructions and changed type/schema/vocabulary on continuation; billed current-schema failure. |
| F09 | Response/text profiles, distinct embedding/image profiles using the same preference name, direct IDs, optional dimensions and invalid reasoning. |
| F10 | Prior precedence/environment/concurrency tests plus auxiliary request credentials, no fallback and concurrent credential isolation. |
| F11 | Existing status taxonomy/retry-hint/idempotency tests plus auxiliary HTTP categories, safe errors and one attempt. |
| F12 | Existing controlled SSE/deadline/progress/summary/cleanup matrix plus text streaming through the same parser. |
| F13 | Existing billed status/output failures, observers/capture plus embedding/image processing failures, image token details and billed caller cancellation. |
| F14 | Modern/legacy wire fields, valid marker limits, unsupported cache combinations, terminal streaming diagnostics/unknown strings and typed/untyped prewarm. |
| F15 | Float batch/dimensions/profile wire, immutable input-order vectors, complete unique indexes, finite/consistent lengths, failures/usage/snapshots/deadlines. |
| F16 | Preset/custom/count/quality/background/format wire, transparent JPEG suppression, count/base64/signature/media/order checks, usage/failures/cancellation/limits. |

## Verification

Final commands and outcomes:

- `dotnet restore Structly.AI.slnx --locked-mode`: passed; dependencies/lockfiles unchanged.
- `dotnet format Structly.AI.slnx --verify-no-changes --no-restore`: passed.
- `dotnet build Structly.AI.slnx -c Release --no-restore`: passed, zero warnings/errors.
- `dotnet test --solution Structly.AI.slnx -c Release --no-build`: 339 passed,
  zero failed/skipped, all offline.
- `dotnet pack src/Structly.AI/Structly.AI.csproj -c Release --no-build -o artifacts/packages`:
  package and symbols created locally, ignored and unpublished.
- `git diff --check`: passed. Tracked-reference, ignored-reference/artifact and local
  documentation-link checks passed.

Restore, formatter and test runner needed approved sandbox escalation for local MSBuild/
Microsoft.Testing.Platform named pipes, as in prior phases. Formatting application used
`dotnet format ... --no-restore`.
An initial concurrency test incorrectly matched the substring red in the schema word
required; matching the quoted vocabulary value corrected the test. Subsequent full
suites passed. Controlled clocks and explicit gates cover async budgets and isolation;
there are no sleep-based assertions or live requests. Linux validation is complete;
Windows CI remains the cross-platform check.

## Remaining limitations and next phase

No required phase 5 runtime work remains. Model-specific vision/reasoning/cache/image/
dimension support remains caller configuration/provider validation. Cache diagnostics,
prewarming and continuation do not guarantee storage availability, prefix reuse or costs.
Image validation checks encoding/signatures, not full decoder integrity. Generated
examples can require caller JSON or disabled examples. Synchronous host callbacks/DTO
construction cannot be forcibly preempted; observer delivery is best effort and abandoned
operations can still be billed. Automatic retries, extra providers/frameworks, image
editing, tool calling and audio/video/file input remain outside the required union.

Phase 6 must implement F17 analyzer parity against the settled contract and package it
for consumers; audit the complete F01–F17 map; finish README/focused guides and a runnable
consumer; validate packed consumption/metadata/symbols/release automation; and settle
package identity, version and license with the owner as required by the plan. No legal
license, paid/live call, remote setting change or publication is authorized here.
