# Phase 1 handoff

Status: complete, 2026-10-04. Documentation only; phase 2 has not started.

## Delivered

- Replaced the initial assessment in [DESIGN.md](DESIGN.md) with release scope,
  illustrative consumer API, schema/serialization matrix, failure/deadline/accounting
  contract, dependencies, provider evidence and acceptance scenarios.
- Compared available reference source and tests, recording strengths and specific
  failure modes rather than preserving either API.
- Recorded the owner's clarification: initial release must functionally cover at least
  the union of ref1/ref2. F01–F17 map every capability to implementation/verification.
  Embeddings, images and analyzer feedback are required, as are streaming, guidance,
  continuation, multimodal inputs and cache controls. These are scheduled work, not deferrals.
- Updated [PLAN.md](PLAN.md) scope and phase 5/6 requirements and marked phase 1 complete.
  No production code, projects, dependencies, lockfiles or reference files changed.

## Decisions and rationale

Use OpenAI initially, explicit model IDs/configured profiles, immutable typed tasks
instead of sessions/registration, and one categorized result API with conventional caller
cancellation exceptions. Reuse keeps startup validation without mutable lifecycle state.
Runtime uses framework HTTP/JSON facilities; no provider SDK, DI or cache dependency.
The analyzer is a build-only dependency added in phase 6 and packaged with the runtime.

Resolve schema/serialization/guidance from the same immutable contract, preserving
names, enum wire values and nested nullability. Support request-scoped vocabularies
without a global schema cache, which avoids retained tenant data and unbounded growth.
Explicit scalar/collection/constructor/attribute rules keep unsupported shapes actionable.

Host owns retries; library sends once. Bound whole execution and callback waiting,
distinguish inactivity from total deadline, retain usage before output processing and
sanitize failure diagnostics. Raw data capture is explicit. Continuation/storage/cache
settings remain provider-specific, with no local persistence or deduplication guarantees.
Official sources are linked and dated in DESIGN.md; current cache/image behavior differs
from some reference comments, so implementation phases must recheck wire/model support.

## Verification

Initial `rtk git status --short` was clean. Inspected repository instructions, plan,
design, solution/project/package configuration and scaffold test, plus available reference
files via `rtk cat`, `rtk sed` and `rtk rg`. No prerequisite gap found; no prior handoff.

Checks for this documentary phase:

- `rtk git check-ignore ref1/StructuredLlmClient/README.md ref2/StructuredLlm/README.md`:
  both ignored; `rtk git ls-files ref1 ref2`: empty. Solution and package project inspected;
  references are neither solution projects nor package inputs.
- Official OpenAI documentation search/open: structured outputs, conversation state,
  streaming, reasoning summaries, cache controls/diagnostics, embeddings and image generation
  verified. Some initial endpoint/Markdown URLs failed to fetch; working official guide
  pages supplied evidence. Endpoint-specific fields must still be rechecked in phases 3/5.
- `rtk python3` documentation check: F01–F17 coverage entries, balanced code fences,
  local documentation links and phase status consistency passed.
- `rtk git diff --check`: passed. Reviewed changed scope and contracts for consistency.

No restore/build/test/pack needed: no production API or build configuration changed.
Examples are conceptual and not compiled against nonexistent types. No credentials,
provider calls, package publication or remote settings changes. Cross-platform build
verification remains an implementation/CI concern.

## Inputs for phase 2

1. Read DESIGN.md as the required behavior contract, including F01–F17 and scope change.
2. Implement task/result/error/schema contracts and local validation first; introduce
   provider/advanced types when their delivery phase needs them, preserving the policies.
3. Resolve serialization members, constructor binding, names/nullability/enums/constraints
   into one contract. Use it for deterministic schema and pre-deserialization checks.
4. Test every support-matrix row and limit boundary; especially nullable enums/items,
   ignored versus conditional-ignore members, renamed enum values, positional records,
   constructor ambiguity, duplicate keys, scalar overflow and vocabulary isolation.
5. Replace the empty-public-API scaffold assertion with meaningful offline behavior tests.
   Do not add credentials or network calls. Run the implementation verification baseline,
   update locks only if needed, and write PHASE-2.md before committing.

Remaining implementation is intentionally assigned to phases 2–6; no required phase 1
decision remains unresolved. Beyond-reference providers/tool calling/image editing/audio/
file inputs/automatic retries/Native AOT remain deferred. Package identity confirmation,
legal license and first release version belong to phase 6; external publishing setup
and explicit publication authorization belong to phase 7.
