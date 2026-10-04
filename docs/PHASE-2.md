# Phase 2 handoff

Status: complete, 2026-10-04. Phase 3 has not started.

## Delivered behavior

- Immutable StructuredTask<T>, startup options and SerializationProfile, schema and
  constraint attributes, detached deterministic CreateSchema inspection, and offline
  ReadOutput validation/deserialization. No transport, credentials or provider requests.
- StructuredResult<T>, categorized errors/issues, schema/operation exceptions, warnings,
  identity/accounting metadata and nullable nonnegative token counts. Result lists are
  snapshotted read-only collections; opt-in raw JSON metadata is detached. EnsureSuccess
  retains the error, metadata and warnings. Value-type default success is unambiguous.
- One resolved serialization contract for names/order/ignores, constructor binding,
  nested nullability, enum wire names, scalar types, nested DTOs and materializable
  collections. Schemas are inline, closed and fully required, with complete nullable
  branches. Unsupported/ambiguous shapes fail at task creation with path/code/correction.
- Request-scoped ordinal sorted vocabularies, static validation without runtime values,
  missing/empty/duplicate/blank-value diagnostics and isolation across calls/profiles.
  No global cache or retained tenant vocabularies.
- Recursive output checks before final deserialization: duplicate/missing/extra keys,
  exact tokens and enum values, nullability, CLR scalar ranges, typed set duplicates,
  string/pattern/format/numeric/cardinality constraints. Failures are sanitized and retain
  supplied billed metadata. Original JSON numbers are compared without CLR rounding.
- Incremental strict-schema property/depth/string/enum budgets and a capped 1 MiB
  serialized schema writer. The local JSON reader rejects input above 16 MiB, caps
  diagnostics at 100 and bounds regex evaluation to 100ms.

## Decisions and rationale

The runtime remains framework-only; no production or test dependencies were added,
so lockfiles remain unchanged. The concrete task owns read-only serializer options
and its resolved tree, allowing concurrent reuse without registration/lifecycle state.
ReadOutput is a public local entry point so consumers and phase 3 share the same checks.
Internal serializer required flags are cleared after presence validation, allowing
JsonRequired on constructor-bound get-only properties without losing required-key checks.

Corrected the phase 1 attribute proposal explicitly in DESIGN.md: C# cannot express
nullable int or decimal attribute named arguments. Length/count defaults are -1;
numeric bounds are finite doubles with NaN for unspecified. Emitted round-trip numeric
bounds are compared to JSON significands/exponents exactly, avoiding tiny-number and
decimal rounding bypasses. DTO decimal types are still supported.

Patterns intentionally expose a documented portable ASCII subset and bounded search
semantics. Nine supported formats have explicit positive/negative fixtures. Typed
dates/Guids additionally obey System.Text.Json conversions. Local CLR ranges and set
equality are stricter checks than provider schema keywords; unsupported uniqueItems
is not emitted. Custom collections require explicit serializer construction/population
contracts; arbitrary host constructor/Add/equality/setter failures remain host behavior
and become safe InvalidOutput (except cancellation/catastrophic exceptions).

Official OpenAI structured-output guidance was searched and opened with the OpenAI
Docs skill, and its relevant limits/formats rechecked. Source and assumptions are in
[DESIGN.md](DESIGN.md). Reference schema/attribute code and test scenarios were inspected;
references remain ignored and are neither dependencies nor committed inputs.

## Changed areas

Runtime: StructuredTask.cs, StructuredContracts.cs, SchemaAttributes.cs, SchemaResolver.cs,
SchemaWriter.cs and OutputValidator.cs under src/Structly.AI. The tree is shared by schema
and output behavior; later output guidance should use it rather than another reflection
walk. Tests replace the empty-public-API scaffold with SchemaTests, OutputTests,
VocabularyTests and SchemaLimitTests. DESIGN.md records implemented API and corrections;
PLAN.md marks only phase 2 complete.

## Verification

The repository was clean at entry. The actual phase 1 documents/scaffold and both
ignored references were present; no prerequisite gap or unrelated changes were found.

Final verification:

- `rtk dotnet restore Structly.AI.slnx --locked-mode`: passed, unchanged lockfiles.
- `rtk dotnet format Structly.AI.slnx --verify-no-changes --no-restore`: passed.
- `rtk dotnet build Structly.AI.slnx -c Release --no-restore`: passed, zero warnings/errors.
- `rtk proxy dotnet test --solution Structly.AI.slnx -c Release --no-build`: 129 passed,
  zero failed/skipped.
- `rtk dotnet pack src/Structly.AI/Structly.AI.csproj -c Release --no-build -o artifacts/packages`:
  produced the 0.1.0 package and symbols. ZIP inspection verified runtime assembly/XML
  documentation and symbol PDB; no reference files. Artifacts remain ignored/unpublished.
- `rtk git diff --check`: passed. Reference ignore/tracked-file checks passed;
  no changes to dependencies, lockfiles or release configuration.

Normal tests are offline,
deterministic and use no ambient credentials. Coverage includes every applicable
support-matrix row, serialization round trips, renamed/nullable enums/items, positional
and selected constructors, inherited/hidden properties, custom collection population,
every CLR numeric range, all nine formats, precision-sensitive numeric bounds,
concurrent vocabulary/profile isolation and strict limit boundaries. Dynamically
generated DTO fixtures exercise 5,000/5,001 properties and repeated sibling counts.

The sandbox denies Unix sockets needed by Microsoft.Testing.Platform and the formatter's
MSBuild host. Those commands require execution outside the sandbox for local IPC;
they still use no provider/network calls. RTK's format wrapper verifies rather than
applies changes, so `rtk proxy dotnet format ... --no-restore` was used to apply formatting.
The formatter cannot automatically fix IDE1006 names; these were corrected directly.
Locked solution restore also failed silently within the sandbox (MSBuild reported zero
errors even in diagnostic mode), then passed outside it without dependency changes.

## Limitations and next-phase inputs

No phase 2 required work is deferred. Transport, model/credential selection and request
types/validation belong to phase 3; streaming/deadline/observer policies to phase 4;
output specifications and the remaining advanced runtime features to phase 5; analyzer
and consumer package verification to phase 6. Publication remains unauthorized.

Phase 3 should:

1. Add OpenAI options/client and the approved task/request model/credential settings.
   Validate and snapshot per-call values and resolve schema before auth or HTTP.
2. Reuse CreateSchema and ReadOutput with captured metadata for typed processing;
   preserve usage before classification/deserialization and keep local error semantics.
3. Verify current Responses wire fields with official documentation and fake-handler
   fixtures. Reject provider refusal/incomplete/failed envelopes before DTO processing.
4. Preserve task/vocabulary/profile isolation, immutable public lists and explicit capture.
   SchemaName/Description belong to text.format metadata; CreateSchema returns only schema.
5. Integrate configured envelope byte ceilings and the whole execution budget in the
   provider path. The local ReadOutput reader currently has a fixed 16 MiB ceiling;
   phase 4 must check caller/deadline boundaries around synchronous schema/typed work.
6. Continue using offline tests; Linux/Windows CI remains cross-platform verification.
