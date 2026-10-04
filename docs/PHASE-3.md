# Phase 3 handoff

Status: complete, 2026-10-04. Phase 4 has not started.

## Delivered behavior

OpenAiClient executes typed nonstreaming text requests through caller-owned HttpClient.
Configuration and profiles are validated and snapshotted without credentials or transport.
Request validation, vocabulary snapshot, schema generation and model selection precede
credential resolution and HTTP. Model and credential precedence is request > task > client;
selected credentials never fall back. Static credentials are explicit; environment factories
read only the named variable on each execution. No model ID is built into the library.

The Responses wire sends model/instructions/input, text.format json_schema with name,
description/schema/strict, explicit store=false by default and stream=false, optional
max_output_tokens/reasoning effort/provider metadata and idempotency header. Authorization
belongs to each HttpRequestMessage; shared transport settings/headers are never mutated.
One library attempt; no automatic retry. Request, response, content and streams are disposed.

Completed output requires one completed assistant message; text content concatenates in
order, ancillary reasoning is ignored, refusal wins over text, and failed/incomplete status
wins over typed parsing. Malformed/unknown/nonterminal envelopes and unexpected answer
content fail safely. ReadOutput applies the existing resolved serialization and constraints.
Authentication, permission, rate/quota, provider rejection/unavailability and transport
failures use the agreed taxonomy. Retry-After seconds/date is preserved (past date -> zero).
No provider, credential or exception text enters safe errors/warnings.

Metadata retains execution/correlation/request/response IDs, requested/resolved model and
independent usage counts before output classification. Missing counts remain null; invalid
counts warn without discarding valid counts or successful output. Optional sensitive raw
envelopes and output text remain detached after response disposal, including partial text
on incomplete output. Envelope reading enforces a configurable byte ceiling (16 MiB default).

## Decisions and changed areas

Framework-only implementation, no dependency or lockfile changes. Public request/model/
credential contracts are in StructuredRequest.cs; task overrides extend StructuredTask.cs;
provider options/client are in OpenAiOptions.cs and OpenAiClient.cs. Tests are in
OpenAiClientTests.cs. No speculative public transport abstraction or provider hierarchy.
Profiles currently address structured output; phase 5 extends operation-specific selection.

The current provider contract was checked against official Responses create and Structured
Outputs documentation with the OpenAI Docs skill. Exact links and implemented/staged API
boundaries are recorded in DESIGN.md. Synthetic fixtures use arbitrary configured model
IDs, never an obsolete reference default. The ref2 provider implementation was inspected
for wire parsing and usage/error pitfalls; both reference trees remain ignored/untracked.
The API-key skill was read; the plan explicitly requires offline, credential-free tests and
does not authorize paid calls, so no key inspection, provisioning or live access occurred.

## Verification

Clean Git status at entry; phase 2 implementation, tests and handoff were verified present.
Final verification commands:

- `rtk dotnet restore Structly.AI.slnx --locked-mode`: passed; lockfiles unchanged.
- `rtk dotnet format Structly.AI.slnx --verify-no-changes --no-restore`: passed.
- `rtk dotnet build Structly.AI.slnx -c Release --no-restore`: passed, zero warnings/errors.
- `rtk proxy dotnet test --solution Structly.AI.slnx -c Release --no-build`: 164 passed,
  zero failed/skipped (129 existing tests, 35 provider fixture cases).
- `rtk dotnet pack src/Structly.AI/Structly.AI.csproj -c Release --no-build -o artifacts/packages`:
  package and symbols created; ignored and unpublished.
- `rtk git diff --check`: passed. Reference ignore/tracked-file checks passed.

Tests cover outgoing fields/schema/constraints/vocabularies, model/credential precedence,
profile snapshots, concurrent request credential isolation, per-call environment reads,
local invalid-input/schema suppression of auth/HTTP, success/refusal/incomplete/failed/
malformed/invalid-output paths, split content/reasoning, usage warnings and billed failure
metadata, safe diagnostics, rate/quota hints, one-attempt behavior, byte ceiling, transport
cancellation/failure, precancel, raw capture lifetime and resource ownership/disposal.
Tests are deterministic/offline with fake handlers and synthetic credentials. Initial test
build required adding xUnit test-context cancellation tokens; final checks pass. Formatter
application used `rtk proxy dotnet format ... --no-restore` because RTK's wrapper verifies.
Restore, formatter and test runner used sandbox escalation for local MSBuild/testing IPC,
consistent with phase 2's platform limitation. No provider requests. Windows CI remains
cross-platform validation.

## Remaining scope and next-phase inputs

No phase 3 required work remains. Reliability/observability belong to phase 4: total budget,
noncooperative resolver/transport handling and late-response cleanup, inactivity/SSE,
progress and usage callbacks, TimeProvider-based timing/retry date checks, cancellation
precedence and StructuredOperationCanceledException retaining metadata. Current execution
passes caller tokens through and propagates ordinary OperationCanceledException; no library
deadline or observer API has been introduced yet. Hosts can use their transport timeout.

Phase 4 should preserve preflight ordering, snapshots and the one-attempt/sanitized outcome
rules while bounding the full operation and callbacks. Reuse the usage capture before
classification and preserve warnings when reconstructing final outcomes. Streaming must
share schema/output validation and terminal-status precedence. Phase 5 retains advanced
messages, continuation/cache/prewarm, output guidance, text, embeddings and images; phase 6
retains analyzer and package-consumer readiness. No publication or remote change occurred.
