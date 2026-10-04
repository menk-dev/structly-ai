# Phase 4 handoff

Status: complete, 2026-10-04. Phase 5 has not started.

## Delivered behavior

OpenAiClient now bounds the complete typed execution with a monotonic total budget:
preflight, credentials, HTTP headers/content/body, output validation and callback waits.
Default total timeout is 120 seconds, per-call replacement is supported, and total budgets
must be positive and at most 24 hours. Optional positive SSE inactivity starts at body
reading and resets on complete lines, including keep-alives, rather than partial bytes.
A default inactivity window applies only to streaming; explicit inactivity/progress/summary
options without streaming fail locally before auth or transport.

Caller cancellation throws StructuredOperationCanceledException with original caller token,
available metadata and immutable warnings. Final precedence is caller > total > inactivity
> provider/output. Library deadlines return transient DeadlineExceeded/InactivityExceeded;
independent transport cancellation remains TransportFailure. Noncooperative async credential,
send, content acquisition, read and callback waits are abandoned at their bounds. Late faults
are observed and late responses/streams disposed. Request/response/body resources are disposed
on success, failure, cancellation and deadlines; caller-owned HttpClient remains reusable.

Stream=true uses bounded UTF-8 SSE without requiring a progress callback. Parser handles
split UTF-8/chunks, multiline data, BOM, CR/LF/CRLF, comments, unknown/ancillary events and
keep-alives. Completed/incomplete/failed terminal event/status agreement is mandatory;
malformed events, wrong content type, standalone errors and premature EOF fail safely.
No DTO success is inferred from deltas. Terminal envelopes share the buffered metadata,
usage, refusal/status and typed output pipeline. MaxResponseBytes bounds consumed SSE bytes,
including comments. Raw capture retains the detached terminal envelope, not an event history.

Progress emits ordered Started/output deltas/opted-in summary deltas/Completed after typed
validation. Completed is emitted after successful validation or InvalidOutput processing;
refusal/incomplete/provider failures do not emit it. IncludeReasoningSummary explicitly
requests reasoning.summary=auto; indexed summaries and output deltas are sensitive host data.
Raw reasoning events are never exposed. Progress callbacks wait at most one second and
remaining total time; fault/cancellation/timeout warns and disables further progress.

Request usage observer overrides client observer. Once any count is known, usage is delivered
once with the outcome before notification, including billed unsuccessful output. Caller
cancellation does not cancel its token. Observer waits at most five seconds/remaining total;
fault/cancellation/timeout/skipping adds safe warnings without masking the primary outcome.
Total expiration and caller cancellation still take precedence. Final accounting remains in
result/cancellation metadata; ExecutionId supports event/result deduplication. Unknown usage
is never invented. Late callback faults are observed; delivery is never retried.

No automatic retry loop or logging sink. Retry-After seconds/date hints are retained with
dates measured against the configured clock. IdempotencyKey remains header passthrough with
no local/provider deduplication promise. Default diagnostics are safe categorized errors,
immutable warnings, IDs/models and usage; credentials, bodies and exception text never enter
error/warning messages. Opt-in raw/output capture and host callbacks own sensitive data.

## Decisions, changed areas and evidence

Framework-only implementation, no dependencies/lockfile changes. Existing OpenAiClient.cs
now separates execution finalization from the single-attempt provider path. OpenAiStreaming.cs
keeps SSE handling close to the feature; OpenAiExecution.cs owns per-call clocks, sources,
metadata, bounded waits and callback policy. ExecutionContracts.cs adds progress/usage and
cancellation contracts. OpenAiOptions.cs and StructuredRequest.cs expose accepted settings.
ReliabilityTests.cs adds 41 offline cases, extending 164 prior tests to 205.

TimeProvider is an explicit client option, System by default, rather than a new dependency or
public transport interface. This supports controlled clocks in hosts/tests and consistent
retry-date evaluation. Extremely long positive inactivity settings remain valid; their timer
interval is clamped to 24 hours because no allowed total execution can outlive that interval.
Synchronous host code cannot be preempted; boundary checks enforce elapsed budgets once it
returns. Callbacks must yield/return promptly. Noncooperative async code may keep executing
externally after the library stops waiting, so cancellation is not a provider stop guarantee.

Both ignored reference trees were present, untracked and outside the solution. Relevant ref1
provider/idle code was consulted; phase 3 provider tests/prerequisites were verified rather
than relying on the status label. The working tree was clean at entry. Official streaming,
streaming event fields and reasoning summary documentation were read using OpenAI Docs;
links and assumptions are in DESIGN.md. The API-key skill was read; the plan explicitly
requires offline credential-free validation and does not authorize paid calls, so no key
inspection, provisioning or live provider access occurred.

Consumer retry/limit/callback guidance is in [EXECUTION.md](EXECUTION.md). DESIGN.md records
the implementation boundary; PLAN.md marks only phase 4 complete.

## Verification

Final verification commands and results:

- `dotnet restore Structly.AI.slnx --locked-mode`: passed; lockfiles unchanged.
- `dotnet format Structly.AI.slnx --verify-no-changes --no-restore`: passed.
- `dotnet build Structly.AI.slnx -c Release --no-restore`: passed, zero warnings/errors.
- `dotnet test --solution Structly.AI.slnx -c Release --no-build`: 205 passed,
  zero failed/skipped; all deterministic/offline.
- `dotnet pack src/Structly.AI/Structly.AI.csproj -c Release --no-build -o artifacts/packages`:
  package and symbols created locally; ignored and unpublished.
- `git diff --check`: passed. Reference/package ignore and tracked-reference checks passed.

Restore, formatter and test runner required sandbox escalation for local MSBuild/testing IPC,
as in prior phases. Formatter application used `dotnet format ... --no-restore`. The initial sandbox test/restore/format attempts
could not access required local pipes; subsequent approved runs passed. Linux validation is
complete; Windows CI remains the cross-platform check. No live calls, remote settings changes,
publishing or credentials were involved.

Controlled TimeProvider/handlers/streams and explicit synchronization replace sleep-based
tests. Coverage includes invalid/preflight settings, bounded noncooperative credentials/send/
stream acquisition/body/observers, simultaneous cancellation/deadline precedence, cleanup and
transport reuse, active/partial keep-alives, total time while activity continues, fragmented/
multiline/ancillary/malformed/unterminated/oversized SSE, terminal failures and refusal usage,
safe callback diagnostics, observer caps/skipping/cancellation independence/override, no usage
when absent, no raw/unrequested reasoning, retry dates/one send and concurrent task/client
isolation across credentials, vocabularies, model/cap/capture options, observers and deadlines.

## Remaining limitations and next-phase inputs

No phase 4 required work remains. Automatic retries remain explicitly deferred; host retry
policy must account for duplicate work and unknown billing. Progress/usage callbacks are best
effort; synchronous host code and server-side cancellation/deduplication cannot be guaranteed.
Billing may be unknown on abandoned requests or streams without terminal usage.

Phase 5 must complete F05–F16 runtime coverage: free text, ordered multimodal input,
continuation/storage, output specifications, advanced model profiles, cache controls/diagnostics/
prewarm, embeddings and images. Reuse the execution budget/callback/finalization behavior for
those operations, keep one-attempt ownership, validate unsupported combinations before auth,
and retain usage before feature-specific output processing. Do not invent provider guarantees
or bypass these reliability rules in a separate operation. Phase 6 retains F17 analyzer and
package-consumer/release readiness. No phase 5 or publishing work was started.
