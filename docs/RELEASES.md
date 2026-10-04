# Release automation

## Flow

Releases use manual version updates and tag-triggered GitHub Actions. No release bot or
personal access token is required for publication. The workflow uses GITHUB_TOKEN.

1. Choose the next version and update `version.txt`, `Directory.Build.props` and
   `CHANGELOG.md` in a normal PR. Changelog entries use an exact `## VERSION` heading.
   Keep scoped Conventional Commits with a body; commit types no longer choose versions.
2. Merge after both required CI checks pass. The first version is `0.1.0`.
3. Fetch main, create its release tag and push it:

   ```sh
   rtk git switch main
   rtk git pull --ff-only
   rtk git tag v0.1.0
   rtk git push origin v0.1.0
   ```

4. The release workflow verifies the tag belongs to main, runs the offline suite and
   package-consumer validation, creates a GitHub Release, attaches package/symbol artifacts,
   publishes the runtime package to GitHub Packages, and restores/runs a fresh consumer
   solely from that authenticated feed.
5. Verify the workflow succeeds, both release attachments download, and an authenticated
   consumer can install and use the registry package.

Owner-approved decisions: Structly.AI, .NET 10, MIT license with copyright menk-dev,
first version 0.1.0, GitHub Packages for personal projects, and manual releases. Manual
versioning supersedes the earlier Release Please bootstrap and release PR policy.

## One-time external setup

1. Enable GitHub Actions in `menk-dev/structly-ai`.
2. Require `validate (ubuntu-latest)` and `validate (windows-latest)` on main. Preserve
   scoped Conventional Commit subjects when squash merging. Release PRs are ordinary PRs;
   no release bot secret or RELEASE_AUTO_MERGE variable is needed.
3. Create the `github-packages` environment. Leave reviewers unset for automatic publication
   after an explicit tag push, or intentionally configure approval if desired.
4. Set `PACKAGES_PUBLISH_ENABLED` to `true` once release readiness is verified.
   The workflow publishes to the repository owner's feed, currently
   `https://nuget.pkg.github.com/menk-dev/index.json`, using GITHUB_TOKEN with job-scoped
   `packages: write`. No NuGet.org account or trusted publishing policy is needed.
5. After first publication, verify package association with this repository and inspect
   visibility/access. New packages default to private. Grant other consuming repositories
   Actions access when using their GITHUB_TOKEN.

The main safeguards and environment were configured during phase 7. Actual publication
and consumer verification must be recorded before declaring phase 7 complete.

## Local release gates

CI and tag publication run the complete offline suite, pack the runtime package with its
analyzer, and run `tools/Structly.AI.PackageValidation`. This validates version.txt against
Directory.Build.props, the current changelog entry, package identity/framework/license,
dependencies, file allowlist, README/XML, portable PDB/Source Link, and a fresh local-feed-only
consumer. The consumer must also fail compilation for an unsupported DTO with STAI001.
The publish job requires the release tag to equal v plus the packed version. Mismatches
stop publication before release creation or upload. Symbol attachment and registry upload
are separate steps.

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

For a transient failure, rerun the original failed publish job. Alternatively, dispatch
`release.yml` from main with its required `tag` input set to the existing release tag.
Dispatch retries the same tag: it never creates or moves tags or bumps versions. Release
creation is skipped when the release already exists, package duplicates are skipped, and
release assets can be replaced. Keep the activation variable enabled for retries.
Treat published versions as immutable: use a new version for content corrections rather
than replacing a version already consumed by your projects.

If attachments succeed but the registry upload fails, verify `packages: write`, package
association/access and the repository owner's feed, then rerun the original job.
If the upload succeeds but consumer restore fails, check consumer credentials, package
source mapping and cross-repository Actions access before creating another release.
Downloading symbols from release assets is independent of package restore.

Official sources: [GitHub NuGet registry authentication, publication and installation](https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-nuget-registry),
[GitHub Packages access permissions](https://docs.github.com/en/packages/learn-github-packages/configuring-a-packages-access-control-and-visibility),
[NuGet credential environment variables](https://learn.microsoft.com/en-us/nuget/consume-packages/consuming-packages-authenticated-feeds),
[package source mapping](https://learn.microsoft.com/en-us/nuget/consume-packages/package-source-mapping),
and [NuGet push options](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-nuget-push).
No remote setting or publication was exercised during local preparation. Phase 7 must
validate the actual tag, registry upload and authenticated consumer flow.
