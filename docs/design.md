# Library direction

## Scope of initialization

One package, one library project, and an offline test project targeting .NET 10, matching
both references. No copied implementation, client contracts, provider dependencies,
schema attributes, or speculative service layers. The initial assembly has no public API.
Remove the scaffold's empty-API assertion when introducing the first public types.

## Reference assessment

These are candidates for a new design, not promises to preserve either API.

| Area | Useful input | Direction for implementation |
| --- | --- | --- |
| Typed tasks | ref1: reusable sessions, response-type registration, preflight validation | Fail locally for unsupported schemas before spending tokens; evaluate whether registration is necessary. |
| Schema fidelity | Both: strict JSON schemas, nullability, collections, enum names, descriptions | Keep schema generation and serialization aligned; prefer standard .NET attributes where sufficient. |
| Runtime vocabulary | ref2: request-scoped dynamic enums and vocabulary-aware caching | Validate supplied vocabularies and bound cache growth; avoid global mutable schema state. |
| Prompt guidance | ref2: field descriptions and example JSON | Consider optional output specifications generated from the same schema contract. |
| Inputs and continuation | ref2: text/image parts and messages; both: continuation metadata | Design typed inputs and explicit continuation ownership without importing either request model. |
| Failure semantics | ref1: categorized failures, retryability, cancellation/deadline distinctions | Decide and document one consistent error contract, including observer failures and caller cancellation. |
| Progress | ref1: streaming progress and inactivity timeout | Keep progress optional; distinguish total deadline from inactivity and never expose raw chain-of-thought. |
| Authentication | ref1: credentials resolved per request, session override without fallback | Support concurrent callers and credential isolation; avoid placing per-user auth on shared default headers. |
| Accounting | ref2: usage observer, including billed refusals and parse failures; ref1: usage/results | Preserve usage on failed output processing and define safe observability behavior. |
| Diagnostics | ref2: provider metadata and cache diagnostics | Expose useful metadata without binding the core contract to unverified provider extensions. |
| Adjacent capabilities | ref1: embeddings and image generation | Decide scope later; keep structured output as the initial focus. |
| Developer feedback | ref1: separate Roslyn analyzer | Consider later only when runtime rules and public DTO contracts are settled. |

Reference documentation includes provider-specific model and cache claims. Verify those
against provider documentation during implementation; do not treat them as specifications.

## Implementation principles

Design for unattended jobs: predictable validation, explicit resource limits, actionable
errors, cancellation, and observable usage. Choose retry ownership deliberately so provider
and host retries cannot silently multiply. Idempotency metadata does not guarantee deduplication.

Keep feature behavior, models, and helpers together. Add abstractions only for real alternate
implementations or meaningful duplication. Keep dependencies minimal and avoid adding a cache,
provider SDK, DI integration, or analyzer merely because a reference used one.

Before implementation, settle the public API, error/cancellation contract, initial provider
scope, schema support matrix, retry policy, and test fixtures. Exercise HTTP behavior with a
fake handler; normal CI must not require API keys, network provider calls, or paid usage.
Any live integration tests should be explicitly opted into.

## Publication decisions still needed

Confirm the package ID, license, initial release version, and supported framework range
before the first publication. No distribution license has been assumed in this scaffold.
