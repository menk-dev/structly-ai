# Structly.AI implementation plan

## Objective

Build a first-class NuGet library for typed, structured LLM output in unattended .NET
workflows. Combine the useful capabilities of the independently developed clients in
`ref1/` and `ref2/` through a coherent new design. Neither reference defines a compatibility
requirement or a public API to preserve.

This plan describes outcomes and verification rather than prescribing class names or
implementation details. The initial baseline is one empty .NET 10 library, an offline test
project, and CI/release scaffolding. Initialization is complete; phase completion is
tracked below.

The owner clarified during phase 1 that the release must functionally replace either
reference: the required feature set is at least the union of `ref1/` and `ref2/`, without
preserving their API style. The F01–F17 coverage map in `docs/DESIGN.md` is the required
release scope. This explicitly includes embeddings, image generation and analyzer
feedback and supersedes their default deferral. No capability in that map may be dropped
merely because it was originally described as optional.

## Instructions for the executing agent

An instruction such as **“Execute phase 1 of docs/PLAN.md”** authorizes the work and local
verification described by that phase. Complete that phase and stop before starting the next.
It does not authorize publishing packages, changing remote repository settings, or paid
provider calls unless the instruction explicitly includes those actions.

Before working:

1. Read `AGENTS.md`, this plan, `docs/DESIGN.md`, and the previous phase's handoff, if any.
2. Inspect the current code and Git status. Preserve unrelated changes. Check prerequisites
   against the actual repository; a status label alone is not proof of completion.
3. Consult the relevant reference code when available. Keep both reference directories
   ignored, outside the solution, and out of commits and package contents. If absent, use
   the committed assessment and record any gaps rather than treating them as dependencies.
4. Resolve routine technical choices autonomously and record the reasoning. Ask only when
   a material product decision, missing external access, or conflicting requirement prevents
   progress. Continue independent work while clarification is pending.

During execution:

- Stay within the phase's scope. Implement the simplest design that meets the agreed
  behavior; do not introduce speculative provider layers or unneeded packages. The
  approved analyzer's compiler dependencies belong only to its phase 6 project.
- Verify current provider behavior against official documentation when needed. Record
  sources and relevant assumptions in the design document; do not inherit model IDs,
  cache semantics, or limits from reference comments without verification.
- Normal tests must be deterministic, offline, and free of provider credentials. Live
  tests are opt-in and must not be needed for a phase to pass.
- Follow repository coding and commit policies. Prefix shell commands with `rtk`.
  If its test wrapper obscures Microsoft.Testing.Platform results, use `rtk proxy dotnet test`.
- If an earlier decision needs correction, update the design and affected behavior/tests
  explicitly. Do not silently carry contradictions into the next phase.

Before declaring a phase complete:

1. Check every completion criterion and run the relevant verification below.
2. Update the phase status table and create or update `docs/PHASE-N.md`, where `N` is the
   phase number. Use uppercase Markdown filenames for all new documentation.
3. In the handoff, record delivered behavior, decisions and rationale, changed areas,
   commands and results, remaining limitations, and concrete inputs for the next phase.
   Distinguish deferred scope from unfinished required work. Never record credentials.
4. Commit the completed phase using a scoped Conventional Commit with a body, including
   documentation and dependency lockfile changes. Do not commit unrelated user changes.
5. Report completion, validation, commit ID, and any limitations. If blocked or partial,
   record that status and explain the unmet criteria rather than marking it complete.

## Status

| Phase | Outcome | Prerequisite | Status |
| --- | --- | --- | --- |
| 1 | Scope, API proposal, and behavior contract | Initialization | Complete |
| 2 | Public contracts and schema engine | Phase 1 | Complete |
| 3 | Working typed calls through the initial provider | Phase 2 | Complete |
| 4 | Reliable execution and observability | Phase 3 | Complete |
| 5 | Approved advanced structured-output capabilities | Phase 4 | Complete |
| 6 | Consumer usability and release readiness | Phase 5 | Complete |
| 7 | Release automation activation and first publication | Phase 6 and external setup | Complete |

## Phase 1 — Establish scope and contracts

**Purpose:** turn the reference assessment into an implementable, coherent specification.
This phase produces documents and illustrative API examples, not production client code.

Work:

- Compare reference implementations and tests against the assessment in `docs/DESIGN.md`.
  Identify strengths, incompatibilities, and failure modes rather than copying their shape.
- Define the initial provider and release scope. Start implementation with structured
  output; assess OpenAI as the initial provider. Include embeddings, image generation
  and analyzer feedback under the owner's clarified functional coverage requirement.
- Propose the consumer API for configuration, typed requests/results, task reuse, and
  async execution. Decide whether sessions or response-type registration provide enough
  value to justify their lifecycle and complexity.
- Specify validation, errors versus exceptions, caller cancellation, total and inactivity
  deadlines, retry ownership, credential selection, progress, and usage on unsuccessful
  but billed responses. Include what happens when an observer fails.
- Establish a schema support matrix covering primitive types, DTOs, nullability, enums,
  collections, serialization attributes, unsupported shapes, and dynamic vocabularies.
- Classify optional output guidance, multimodal inputs, continuation, model selection,
  and cache controls as initial-release or deferred capabilities. For each included
  capability, name its implementation phase and acceptance scenarios.
- Record dependency choices and the offline testing strategy. Keep provider-specific
  settings explicit without forcing a speculative universal abstraction.

Deliverables: an updated `docs/DESIGN.md` with scope, API examples, behavior tables,
acceptance scenarios, and decisions; `docs/PHASE-1.md` as the handoff.

Complete when the next agent can implement contracts and schema rules without inventing
core product policy. Required decisions must be resolved; deferred capabilities must be
named. Proposed examples should cover success, invalid schema, provider failure,
cancellation, and usage recording. No new production API is required in this phase.

## Phase 2 — Implement contracts and schema behavior

**Purpose:** establish the provider-independent typed-output foundation.

Work:

- Implement the public contracts accepted in phase 1 and their local validation rules.
  Replace the scaffold's empty-public-API assertion with meaningful behavioral tests.
- Build schema generation aligned with the serialization contract, including names,
  ignored members, descriptions, nullability, enums, nested DTOs, and supported collections.
- Reject unsupported or ambiguous shapes with actionable diagnostics before any transport
  call. Apply provider strict-schema constraints at the appropriate boundary.
- Implement dynamic enum validation here if included in the initial scope. Ensure schema
  reuse cannot mix different vocabularies or serialization settings; bound caching if used.
- Keep generated schema and deserialization expectations consistent. Define whether and
  how output validation catches values that deserialization alone would accept.

Deliverables: implemented contracts and schema engine, tests for the support matrix,
updated design/examples where needed, and `docs/PHASE-2.md`.

Complete when supported shapes produce valid, deterministic schemas and unsupported
shapes fail predictably. Tests must cover contract fidelity and meaningful edge cases,
including isolation between differing schema inputs. No provider requests are required.

## Phase 3 — Implement the initial provider path

**Purpose:** make the smallest useful typed request work end to end.

Work:

- Implement provider configuration, request construction, HTTP execution, and typed response
  processing according to the phase 1 contract and current official provider documentation.
- Resolve credentials per the agreed policy without mutating shared headers for individual
  users. Respect ownership and disposal of caller-supplied transport resources.
- Validate requests and schemas before sending. Support model selection and basic text
  input without importing hard-coded preferences from a reference blindly.
- Handle successful output, provider refusal, incomplete output, malformed responses,
  authentication failures, rate limits, and transport/provider failures consistently.
- Preserve available response identity, resolved model, and usage metadata, including
  failures during typed-output processing.
- Test the wire contract with a fake HTTP handler and representative response fixtures.
  Avoid extra public transport abstractions created solely to make tests possible.

Deliverables: a functioning initial provider implementation, offline end-to-end tests,
and `docs/PHASE-3.md`.

Complete when a consumer can configure the library and obtain a typed result through
the simulated provider. Tests must verify outgoing requests, invalid-input call suppression,
failure classification, credential isolation, and metadata preservation. Live access is
optional and cannot replace fixture-based validation.

## Phase 4 — Make execution reliable for automation

**Purpose:** make behavior predictable under concurrency, failure, and resource limits.

Work:

- Implement the agreed cancellation and deadline behavior, including precedence when
  multiple cancellation sources fire. Bound the entire execution budget where specified.
- Implement or explicitly delegate retries according to the chosen ownership policy.
  Honor relevant provider retry hints, avoid nested retries, and document duplicate-work
  risks and the actual guarantees of any idempotency metadata.
- Implement optional progress and streaming if included in scope. Handle stream termination,
  malformed events, inactivity, cancellation, and cleanup without exposing raw reasoning.
- Implement usage observation and diagnostics with defined callback-failure behavior.
  Prevent credentials and sensitive request/output bodies from leaking into default logs.
- Verify safe concurrent reuse of clients/tasks and independence of request-specific
  credentials, schema state, options, and observers. Dispose resources on all exit paths.
- Use controlled handlers, synchronization, and time sources where practical instead of
  timing-sensitive sleeps in tests.

Deliverables: reliability and observability behavior, targeted failure/concurrency tests,
consumer guidance on retries and limits, and `docs/PHASE-4.md`.

Complete when tests demonstrate bounded execution, correct cancellation semantics,
observable billed failures, cleanup, and concurrent request isolation. Behavior must match
the design; unsupported progress or retry features must be explicitly documented.

## Phase 5 — Complete the approved capability set

**Purpose:** complete the required reference capability set, except analyzer feedback
assigned to phase 6 in the phase 1 design.

Work:

- Revisit the F01–F17 phase 1 coverage map and implement every runtime capability not
  already delivered, including free text, multimodal messages, continuation, output
  specifications, dynamic vocabulary interactions, provider cache controls/diagnostics
  and prewarming, embeddings and image generation.
- Keep output specifications, examples, and schemas derived from the same contract so
  prompts cannot disagree with validation or serialization.
- Make continuation and any provider-side persistence semantics explicit. Do not imply
  local session storage or guaranteed cache reuse where neither exists.
- Verify provider-specific options and capabilities against official documentation. Reject
  unsupported combinations rather than accepting settings that have no effect.
- Test interactions, not just isolated features: different vocabularies on concurrent calls,
  nullable enum collections, continuation with changed schemas, and usage on failed output
  processing, as applicable to the approved scope.

Deliverables: the remaining approved capabilities, interaction tests, revised examples,
and `docs/PHASE-5.md`.

Complete when every required runtime capability in F01–F16 has implementation,
documentation, and acceptance coverage. Analyzer feedback (F17) remains assigned to
phase 6. Deferred capabilities beyond the reference union remain deferred.

## Phase 6 — Prepare the library for consumers

**Purpose:** validate that the library works as a package, not just within its own solution.

Work:

- Implement the approved Roslyn analyzer feedback (F17) against the settled runtime
  rules, verify diagnostic parity, and include it in the packed consumer validation.
- Audit the F01–F17 map against both references; all required runtime and analyzer
  capabilities must be implemented before release readiness is complete.
- Review the public surface for consistency, minimal dependencies, clear defaults, useful
  diagnostics, and correct lifecycle behavior. Resolve rough edges without restoring
  compatibility with the references.
- Write the README quick start and focused guides for configuration, schema rules,
  failures, cancellation/retries, usage, and advanced features included in scope. Clearly
  state framework support and limitations; remove obsolete scaffold claims.
- Add a small runnable consumer example and validate consumption of the packed NuGet
  artifact from a local feed, including its public API and dependency resolution.
- Run the complete offline checks and inspect package metadata, README, XML documentation,
  symbols, source information, and accidental file inclusions.
- Confirm the package ID, framework range, release version policy, and license with the
  owner where unresolved. Add the chosen license file and package metadata; do not select
  a legal license on the owner's behalf.
- Review release automation against the intended unattended workflow, including version
  consistency, release PR checks, publication failures, and recovery instructions.

Deliverables: consumer documentation and example, package-consumption validation,
release-readiness assessment, and `docs/PHASE-6.md`.

Complete when a fresh consumer can use the packed library with documented behavior and
all local checks pass. Required publication decisions must be settled before release
readiness is marked complete. External account configuration may remain for phase 7.

## Phase 7 — Activate and validate publishing

**Purpose:** connect the prepared repository to its release accounts and publish the first
usable version. This phase needs external access and explicit authorization for publication.

Work:

- Follow `docs/RELEASES.md` and inspect existing remote settings before changing them.
  Configure required checks, GitHub Packages identity/access, and
  environment settings as authorized. Never store credentials in the repository.
- Enable publishing only after phase 6 readiness is confirmed. Confirm version.txt, Directory.Build.props and CHANGELOG.md agree on the intended
  version before tagging. Releases use manual versioning and tag-triggered publishing.
- Verify the version-update PR passes required checks before merging and tagging main.
  Confirm the released tag, built package version, changelog, and uploaded artifacts agree.
- Publish the authorized release, verify package availability and consumption, and record
  the outcome. Verify the symbol package is downloadable from the GitHub release separately from
  package registry publication; automatic symbol-server indexing is outside this setup.
- Document any recovery adjustments found during execution. Do not generate repeated
  releases to conceal a failed upload or replace immutable published package contents.

Deliverables: operational release automation, verified first release, updated setup and
recovery documentation, and `docs/PHASE-7.md`.

Complete when the first usable package is available and the configured process can handle
subsequent releases with manual version updates/tagging and automated packaging/upload. If access
or publication authorization is missing, complete independent preparation and record the
remaining external actions; keep this phase partial or blocked.

## Verification baseline

For implementation phases, run the following from the repository root:

```sh
rtk dotnet restore Structly.AI.slnx --locked-mode
rtk dotnet format Structly.AI.slnx --verify-no-changes --no-restore
rtk dotnet build Structly.AI.slnx -c Release --no-restore
rtk proxy dotnet test --solution Structly.AI.slnx -c Release --no-build
rtk dotnet pack src/Structly.AI/Structly.AI.csproj -c Release --no-build -o artifacts/packages
rtk git diff --check
```

Regenerate and commit lockfiles when dependencies change. Run phase-specific checks in
addition to this baseline, such as consumer-package validation in phase 6. For a purely
documentary phase, verify references, internal consistency, and the diff; a full build is
unnecessary unless build configuration also changes. Report checks actually run, including
limitations of the local platform; Linux/Windows CI remains the cross-platform check.
