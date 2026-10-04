# Phase 6 handoff

Status: complete, 2026-10-04. Phase 7 has not started.

## Delivered behavior

The required reference union F01–F17 is implemented, documented and covered by offline
acceptance checks. Phase 6 adds compile-time schema feedback in the shipping package,
consumer documentation, a runnable offline example, isolated package-consumption validation,
license metadata and release gates/recovery guidance. The runtime remains dependency-free.

The owner explicitly approved MIT licensing (copyright menk-dev), package ID Structly.AI,
.NET 10 support, initial release 0.1.0 and the existing Conventional Commit/Release Please
version policy. LICENSE and PackageLicenseExpression=MIT are included in the NuGet package.
No paid calls, real credential inspection, remote setting changes or publication occurred.

The working tree was clean at entry. Phase 5 prerequisites were inspected against the
implementation and its full 339-case regression suite, not assumed from the status table.
Both ignored references were present. The audit revisited their READMEs, model contracts,
provider/schema/specification tests and ref1 analyzer/tests against the F01–F17 map;
no additional unimplemented required capability was found. Reference trees remain ignored,
outside the solution, tracked files and package artifacts.

## Decisions and changed areas

StructuredSchemaAnalyzer targets netstandard2.0 and is packed under analyzers/dotnet/cs
in Structly.AI. Microsoft.CodeAnalysis.CSharp 4.14.0 is a compatible compiler API baseline
for the .NET 10 host; PrivateAssets prevents compiler packages from becoming consumer
dependencies. Its dedicated tests use Roslyn too. Runtime/compiler-host targets require
separate symbol/reflection implementations, so tests compile snippets, run diagnostics,
invoke the actual runtime task factory and compare issue code/path to prevent drift.

STAI001 errors identify statically provable unsupported shapes, cycles, serialized names,
constructors, overrides, descriptions, constraints and structural/enum/string limits.
Supported call sites include Create, schema/output inspection, ExecuteAsync and typed
PrewarmAsync. The first issue follows runtime resolution. Repeated inline emissions,
Unicode scalar counting, 1,000-entry enum limits and System.Text.Json acronym casing
correct shortcomings in ref1's analyzer. Generated code/open generic parameters are
skipped. Dynamic naming only reports issues present under both supported naming policies.

STAI002 warns about known legacy attributes, even unresolved qualified names during
migration, and recommends standard attributes or new String/Number/CollectionConstraint
attributes. Warning severity makes replacement guidance advisory; unresolved attributes
already cause compiler errors. Runtime vocabularies/options, serialized schema byte-size,
host DTO construction, output values and provider support remain runtime checks. The
analyzer never replaces startup validation or certifies provider compatibility.

README now describes the implemented API, defaults and limitations. CONFIGURATION,
SCHEMAS, FAILURES and USAGE complement existing EXECUTION/ADVANCED. The offline consumer
uses a fake Responses handler and exercises typed output, dynamic vocabulary, generated
guidance, usage, failed output validation and caller cancellation without provider access.

Structly.AI.PackageValidation inspects package identity/version/license/framework,
dependency freedom, exact file allowlist, README/license freshness, public XML documentation,
runtime portable PDB/Source Link revision and both packed assembly versions. It copies the
consumer outside the repository, restores solely from the local feed into a fresh temporary
cache, builds/runs it, then requires STAI001 when replacing the DTO with a dictionary shape.
Repository project references, central settings and existing package caches cannot mask a
packing defect. Temporary consumers are removed on exit; NuGet artifacts remain ignored.

CI and publication preparation now run that validator. The release job checks v-prefixed
tag/version consistency before uploading anything. Bootstrap uses an empty manifest and
initial-version=0.1.0; the prior manifest would have treated 0.1.0 as previously released
and bumped again. Package upload uses --no-symbols; independent symbol upload makes
partial success visible and retryable. RELEASES records bot/required-check prerequisites
and original-run recovery rather than dispatching a new release to hide an upload failure.

Production changes are limited to analyzer packaging and MIT metadata. New projects are
the analyzer, its tests, the example and package validator. New/changed documentation,
workflows, central compiler version and new dependency lockfiles are part of this phase.
Official compiler/NuGet and Release Please sources are linked in DESIGN and RELEASES.

## Acceptance audit

| Capability | Final acceptance evidence |
| --- | --- |
| F01 | Immutable task/startup validation, concurrent reuse in Schema/ReliabilityTests; consumer creates and reuses a task. |
| F02–F04 | Schema/Output/SchemaLimit/VocabularyTests; analyzer compares static shapes/names/constraints with the runtime; consumer resolves dynamic vocabulary. |
| F05 | AdvancedResponseTests contract-derived guidance/examples, constraints and bounded generation; consumer generates validated guidance. |
| F06–F08 | Typed/text buffered and streaming output, multimodal roles/order, explicit continuation/storage and changed-schema interactions in provider/advanced tests. |
| F09–F11 | Profile/credential precedence and concurrent isolation, HTTP taxonomy, safe errors, retry hints and one attempt in provider/reliability/auxiliary tests. |
| F12–F13 | Controlled SSE/deadlines/progress/cleanup and billed-failure accounting in ReliabilityTests; consumer observes usage and preserves caller cancellation. |
| F14 | Modern/legacy cache fields/marker limits, unsupported combinations, diagnostics and typed/untyped prewarm in AdvancedResponseTests. |
| F15–F16 | Complete ordered finite embedding vectors and image count/base64/signature/format/options, common budgets/usage/cancellation in AuxiliaryOperationTests. |
| F17 | 88 analyzer tests for valid DTOs and runtime code/path parity, limit boundaries, provider/inspection call sites, generic/generated-code behavior and legacy guidance; packed consumer sees STAI001. |
| Consumer/release | Fresh local-feed-only restore/run, packed analyzer failure, metadata/content/source/symbol/version checks; mismatched release tag rejected. |

## Verification

Commands and final results:

- `dotnet restore Structly.AI.slnx --force-evaluate`: generated new locked graphs.
- `dotnet restore Structly.AI.slnx --locked-mode`: passed, all seven projects.
- `dotnet format Structly.AI.slnx --verify-no-changes --no-restore`: passed.
- `dotnet build Structly.AI.slnx -c Release --no-restore`: passed, zero warnings/errors.
- `dotnet test --solution Structly.AI.slnx -c Release --no-build`: 427 passed
  (339 runtime + 88 analyzer), zero failed/skipped, entirely offline.
- `dotnet pack src/Structly.AI/Structly.AI.csproj -c Release --no-build -o artifacts/packages`:
  local nupkg/snupkg created, unpublished.
- `dotnet run --project tools/Structly.AI.PackageValidation -c Release --no-build -- artifacts/packages v0.1.0`:
  metadata/content/XML/PDB/Source Link/versions passed; fresh consumer passed; packed
  analyzer rejected invalid DTO with STAI001. Wrong tag v0.2.0 was separately rejected.
- `git diff --check`: passed. Local documentation links, release JSON and workflow
  YAML parsing passed. Reference/artifact ignore and tracked-file checks passed.

Restore/formatter/test runner/fresh consumer needed sandbox escalation for local
MSBuild/Microsoft.Testing.Platform communication pipes, consistent with earlier phases.
Formatting was applied with `dotnet format`.
Initial test runs exposed implicit conversion handling on target-typed options, indexer
metadata names and an oversized Unicode fixture; these were corrected. Initial consumer
validation exposed a non-seekable ZIP PDB stream and a fake message missing its required
completed status; the validator/fixture were corrected. Final checks above pass.
Linux local verification is complete; Windows CI remains the cross-platform check.

## Remaining limitations and phase 7 inputs

No required local phase 6 work remains. Analyzer limitations are documented in SCHEMAS;
runtime validation covers dynamic values and emitted byte size. Only the runtime portable
PDB ships in snupkg; the analyzer DLL loads from its conventional package path. Existing
runtime/model/cache/storage/image/host-code limitations remain as recorded in phase 5.
Extra providers/frameworks, automatic retries, Native AOT, tools, editing and audio/video/
file input remain deferred beyond the required reference union.

Phase 7 requires explicit publication authorization and external setup: verify NuGet
package ownership/availability, bot token, both required CI matrix checks, auto-merge,
nuget environment and trusted publishing identity/policy. Inspect actual remote settings
before modifying them. Ensure the first generated release PR proposes 0.1.0, its checks
pass before merge and tag/package/changelog agree. Publishing is still gated by
NUGET_PUBLISH_ENABLED; it was not enabled here. Verify package availability/consumption
and symbol indexing separately after the authorized release. Use RELEASES recovery for
upload failures. No phase 7 work or external-account validation is claimed.
