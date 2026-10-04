# Release automation

## Flow

Releases use manual version updates and tag-triggered GitHub Actions. No release bot or
personal access token is required for publication. The workflow uses GITHUB_TOKEN.

1. Choose the next version and update `version.txt`, `Directory.Build.props` and
   `CHANGELOG.md` in a normal PR. Changelog entries use an exact `## VERSION` heading.
   Keep scoped Conventional Commits with a body; commit types no longer choose versions.
2. Merge after both required CI checks pass. Use the chosen version in the tag commands below.
3. Fetch main, create its release tag and push it:

   ```sh
   git switch main
   git pull --ff-only
   git tag v0.1.0
   git push origin v0.1.0
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

The main safeguards, environment and activation variable are configured. Version 0.1.0
was published and consumed successfully on 2026-10-04.

## Local release gates

CI and tag publication run the complete offline suite, pack the runtime package with its
analyzer, and run `tools/Structly.AI.PackageValidation`. This validates version.txt against
Directory.Build.props, the current changelog entry, package identity/framework/license,
dependencies, file allowlist, README/XML, portable PDB/Source Link, and a fresh local-feed-only
consumer. The consumer must also fail compilation for an unsupported DTO with STAI001.
The publish job requires the release tag to equal v plus the packed version. Mismatches
stop publication before release creation or upload. Symbol attachment and registry upload
are separate steps.

## Recovery

For a transient failure, rerun the original failed publish job. Alternatively, dispatch
`release.yml` from main with its required `tag` input set to the existing release tag.
Dispatch retries the same tag: it never creates or moves tags or bumps versions. Release
creation is skipped when the release already exists, package duplicates are skipped, and
release assets can be replaced. Keep the activation variable enabled for retries.
Treat published versions as immutable: use a new version for content corrections rather
than replacing a version already consumed by your projects.

Version 0.1.0 predates owner-authorized documentation history cleanup. Its published
package and symbols retain their original source revision; do not rebuild or replace its
release assets from the rewritten tag. Use a new version for subsequent content changes.

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
The first publication validated the tag, registry upload, authenticated consumer and symbol
downloads. Consumer feed and symbol setup is documented in [INSTALLATION.md](../docs/INSTALLATION.md).
