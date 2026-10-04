# Release automation

## Flow

Conventional Commits on `main` feed Release Please. It opens or updates a release PR that
changes `version.txt`, `Directory.Build.props`, the release manifest, and `CHANGELOG.md`.
After that PR merges, it creates the version tag and GitHub release. The same workflow
checks out the tag, validates, builds, tests, packs, attaches artifacts, and publishes
the package and symbols if publishing has been enabled.

Use scoped subjects with a body, for example `feat(schema): support nullable enums`.
`feat` and `fix` drive releases; use `!` or a `BREAKING CHANGE:` footer for breaking changes.
The current `0.1.0` is the intended first release, not an already published release.
The bootstrap manifest is empty and `initial-version` is `0.1.0`; putting `0.1.0` in
that manifest before the first release would treat it as the last release and bump again.
Release Please populates the manifest on the first release PR. Afterward, all version
files must agree; do not reset the manifest or pin `release-as` for routine releases.

Owner-approved release decisions (2026-10-04): Structly.AI, .NET 10, MIT license with
copyright menk-dev, first version 0.1.0 and the existing Conventional Commit policy.
The license file and SPDX package expression are committed; availability and ownership
in the target NuGet account must still be verified externally.

## One-time external setup

1. Push the repository to `menk-dev/structly-ai` and enable GitHub Actions.
2. Configure `RELEASE_PLEASE_TOKEN` as a repository secret using a bot token with repository
   contents and pull-request read/write permissions. The default GitHub token does not
   trigger CI on bot-created release PRs. See the [Release Please action documentation](https://github.com/googleapis/release-please-action).
3. Require both CI matrix checks (`validate (ubuntu-latest)` and `validate (windows-latest)`)
   on `main`. Enable squash merges and preserve Conventional
   Commit subjects. For unattended release PR merging, enable repository auto-merge and
   set the `RELEASE_AUTO_MERGE` repository variable to `true`. Release PRs will then queue
   for squash auto-merge after required checks pass. Do not require a human review if the
   intended policy is fully unattended releases.
4. Confirm ownership/availability of `Structly.AI` in the target NuGet account. Verify phase 6
   readiness and its owner-approved license/version decisions before enabling publishing.
5. Create the `nuget` GitHub environment and set repository variable `NUGET_USER` to the
   NuGet account's profile name. Add a [NuGet trusted publishing policy](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing)
   for owner `menk-dev`, repository `structly-ai`, workflow `release.yml`, environment
   `nuget`, scoped to `Structly.AI` (including new-package publication if needed).
6. Set repository variable `NUGET_PUBLISH_ENABLED` to `true` when ready. Leave environment
   reviewers unset for unattended publication, or intentionally configure approval if desired.

No remote settings, secrets, trust policies, releases, or packages are created by initializing
this repository. Account setup cannot be completed by committed workflow files alone.

## Local release gates

CI and tag publication run the complete offline suite, pack the single runtime package
with its analyzer, and run `tools/Structly.AI.PackageValidation`. This validates version.txt,
Directory.Build.props and the release manifest (or initial-version during bootstrap),
package identity/framework/license/dependencies, the exact file allowlist, README/XML,
portable PDB and Source Link, and a fresh local-feed-only consumer. The consumer exercises
runtime behavior and must also fail compilation for an unsupported DTO with STAI001.
The publish job additionally requires the release tag to equal `v` plus the packed version.
Version mismatches stop publication. Package and symbol publication are separate steps.

The release bot token must trigger release PR CI; required matrix checks must guard
squash auto-merge. Queuing auto-merge alone does not guarantee checks are required: phase 7
must inspect remote branch protection and bot-created PR checks. Do not enable publishing
until the generated first PR proposes exactly 0.1.0 and checks pass. Remote accounts,
branch settings and trusted publishing remain unverified by local execution.

## Recovery

If publishing fails after release creation, rerun the failed publish job in the original
workflow run. A fresh workflow dispatch only reconciles releases; it does not republish an
existing release. Duplicate package uploads are skipped and release assets can be replaced.
NuGet package contents cannot be replaced for an existing version: release a new version
for a content correction.

A package upload can succeed while symbols fail. Rerun the original failed job: package
upload uses `--no-symbols --skip-duplicate`, followed by an independent `.snupkg` upload
with `--skip-duplicate`. Check symbol ingestion/indexing separately on NuGet; a successful
command does not certify that consumers can retrieve symbols immediately. Trusted publishing
must still be valid for the original tag/workflow/environment. Fix account policy or temporary
service failures rather than generating new versions for an upload retry. For a content defect,
use a new release and never overwrite an existing NuGet version.

Official sources reviewed in phase 6: [Release Please bootstrap and manifest policy](https://github.com/googleapis/release-please/blob/main/docs/manifest-releaser.md),
[initial-version strategy](https://github.com/googleapis/release-please/blob/main/src/strategies/base.ts),
and [NuGet push options](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-nuget-push).
No workflow, remote setting or publication was exercised against an external account during
local preparation. Phase 7 must validate the actual release PR, tag and trusted publishing flow.
