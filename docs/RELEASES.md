# Release automation

## Flow

Conventional Commits on `main` feed Release Please. It opens or updates a release PR that
changes `version.txt`, `Directory.Build.props`, the release manifest, and `CHANGELOG.md`.
After that PR merges, it creates the version tag and GitHub release. The same workflow
checks out the tag, validates, builds, tests, packs, attaches artifacts, and publishes
the runtime package to GitHub Packages if publishing has been enabled. Both the runtime
package and symbol package are attached to the GitHub release for download.

Use scoped subjects with a body, for example `feat(schema): support nullable enums`.
`feat` and `fix` drive releases; use `!` or a `BREAKING CHANGE:` footer for breaking changes.
The current `0.1.0` is the intended first release, not an already published release.
The bootstrap manifest is empty and `initial-version` is `0.1.0`; putting `0.1.0` in
that manifest before the first release would treat it as the last release and bump again.
Release Please populates the manifest on the first release PR. Afterward, all version
files must agree; do not reset the manifest or pin `release-as` for routine releases.

Owner-approved release decisions (2026-10-04): Structly.AI, .NET 10, MIT license with
copyright menk-dev, first version 0.1.0 and the existing Conventional Commit policy.
The license file and SPDX package expression are committed. The owner selected GitHub Packages
on 2026-10-04 for use in their own projects; this supersedes the original NuGet.org
publishing setup. Package access must still be verified externally.

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
4. Verify phase 6 readiness and its owner-approved license/version decisions. The publishing
   job uses the repository owner's feed, currently
   `https://nuget.pkg.github.com/menk-dev/index.json`, with the workflow's built-in
   `GITHUB_TOKEN` and job-scoped `packages: write` permission. No NuGet.org account,
   NuGet API key or trusted publishing policy is needed.
5. Create the `github-packages` GitHub environment. Leave environment reviewers unset
   for unattended publication, or intentionally configure approval if desired.
6. Set repository variable `GITHUB_PACKAGES_PUBLISH_ENABLED` to `true` only when the
   release PR/check prerequisites are verified and publication is authorized. The old
   `NUGET_PUBLISH_ENABLED` and `NUGET_USER` variables are no longer used.
7. After the first publication, verify the package is associated with this repository
   through its existing RepositoryUrl metadata and inspect its visibility/access.
   New packages default to private. Keep it private for personal use; grant other
   consuming repositories Actions access when using their `GITHUB_TOKEN`.

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
Version mismatches stop publication. Release asset attachment and registry upload are
separate steps.

The release bot token must trigger release PR CI; required matrix checks must guard
squash auto-merge. Queuing auto-merge alone does not guarantee checks are required: phase 7
must inspect remote branch protection and bot-created PR checks. Do not enable publishing
until the generated first PR proposes exactly 0.1.0 and checks pass. Remote accounts,
branch settings and GitHub Packages access remain unverified by local execution.

## Consuming from your projects

Local consumers need a GitHub personal access token (classic) with `read:packages` and
access to the package, including when the package is public. Configure this feed alongside
nuget.org in the consuming project's `nuget.config`; do not add the private feed to this
library's build, which only restores public build dependencies:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="github" value="https://nuget.pkg.github.com/menk-dev/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="github">
      <package pattern="Structly.AI" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
```

Set `NuGetPackageSourceCredentials_github` in the local process environment to
`Username=YOUR_GITHUB_USERNAME;Password=YOUR_TOKEN`, using your credential manager or
secure shell entry. Keep token values out of committed configuration and command history.
Then install:

```sh
dotnet add package Structly.AI --version 0.1.0
```

The exact package mapping routes Structly.AI to GitHub and other dependencies to nuget.org,
avoiding unrelated public dependency downloads from the authenticated feed during restore.
Package metadata commands can still query all sources.

In a consuming GitHub Actions repository, grant that repository Actions access in the
package settings, set job permission `packages: read`, and supply the credential environment
variable with `Username=${{ github.actor }};Password=${{ secrets.GITHUB_TOKEN }}` during restore.
A token from another repository cannot read this package solely because both repositories
have the same owner; verify package access explicitly.

## Symbols

The workflow attaches both `.nupkg` and `.snupkg` to the GitHub release and pushes only
the runtime `.nupkg` to the GitHub Packages registry with `--no-symbols`. This setup
does not provide automatic symbol-server indexing. For debugging, download the matching
`.snupkg`, extract its runtime portable PDB, and configure your debugger's local symbol
search path. Source Link metadata remains validated locally. Verify the symbol download
separately from registry availability.

## Recovery

If publishing fails after release creation, rerun the failed publish job in the original
workflow run. A fresh workflow dispatch only reconciles releases; it does not republish an
existing release. Duplicate package uploads are skipped and release assets can be replaced.
Treat released package versions as immutable: use a new version for content corrections,
rather than deleting/replacing a version already consumed by your projects.

If attachments succeed but the registry upload fails, verify `packages: write`, package
association/access and the repository owner's feed, then rerun the original job.
If the upload succeeds but consumer restore fails, check consumer credentials, package
source mapping and cross-repository Actions access before creating another release.
Downloading symbols from release assets is independent of package restore.

Official sources: [GitHub NuGet registry authentication, publication and installation](https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-nuget-registry),
[GitHub Packages access permissions](https://docs.github.com/en/packages/learn-github-packages/configuring-a-packages-access-control-and-visibility),
[NuGet credential environment variables](https://learn.microsoft.com/en-us/nuget/consume-packages/consuming-packages-authenticated-feeds),
[package source mapping](https://learn.microsoft.com/en-us/nuget/consume-packages/package-source-mapping),
[Release Please bootstrap and manifest policy](https://github.com/googleapis/release-please/blob/main/docs/manifest-releaser.md),
[initial-version strategy](https://github.com/googleapis/release-please/blob/main/src/strategies/base.ts),
and [NuGet push options](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-nuget-push).
No remote setting or publication was exercised during local preparation. Phase 7 must
validate the actual release PR, tag, registry upload and authenticated consumer flow.
