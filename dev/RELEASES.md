# Releases

Update the version manually, merge the release PR, then push a version tag to publish.
GitHub Actions uses `GITHUB_TOKEN`; publication does not need a release bot or personal
access token.

## Prepare and publish

1. Update `version.txt`, `Directory.Build.props` and `CHANGELOG.md` in a PR. Add an exact
   `## VERSION` heading to the changelog. Update installation examples to the new version.
   Use scoped Conventional Commits with a body.
2. Run the checks in [the development guide](README.md), then merge after the required
   Linux and Windows CI checks pass.
3. Fetch `main` and tag the release commit. For version `0.2.0`:

   ```sh
   git switch main
   git pull --ff-only
   git tag v0.2.0
   git push origin v0.2.0
   ```

4. Check the release workflow. It verifies that the tag belongs to `main`, runs tests,
   builds and validates both packages, creates the GitHub Release, attaches packages and
   debug symbols, and publishes the packages to GitHub Packages. It then installs the
   core package from that feed in a fresh test application and runs it.
5. Confirm that the release files download and that your applications can install both
   `Structly.AI` and `Structly.AI.Hosting` from the authenticated feed.

The packages target .NET 10, use the MIT license with copyright `menk-dev`, and publish to GitHub Packages.
Version numbers are chosen manually; commit types do not set them.

## Repository setup

The following settings are already configured:

- GitHub Actions is enabled in `menk-dev/structly-ai`.
- `main` requires `validate (ubuntu-latest)` and `validate (windows-latest)`.
- The `github-packages` environment has no required reviewers.
- `PACKAGES_PUBLISH_ENABLED` is `true`.
- Merging a PR deletes its branch automatically.

Version `0.1.0` was published and installed successfully on 2026-10-04. Manual versioning
replaced the earlier Release Please setup; no release bot secret or `RELEASE_AUTO_MERGE`
variable is needed. Preserve scoped Conventional Commit subjects when squash merging.

The workflow publishes to `https://nuget.pkg.github.com/menk-dev/index.json` with
`packages: write`. It does not publish to NuGet.org. New GitHub packages are private
by default. Check each package's access settings and grant consuming repositories
Actions access if they use their own `GITHUB_TOKEN` to restore packages.

## Package checks

CI and the release workflow build both packages and run `tools/Structly.AI.PackageValidation`.
It checks version agreement, package IDs, target frameworks, licenses, dependencies,
contents, README and XML documentation, debug symbols and Source Link metadata. It also
installs the core package from a local feed in a separate application with a fresh cache.
That application must run successfully, and an unsupported output type must fail compilation
with `STAI001` from the packaged analyzer.

The release tag must equal `v` plus the package version. A mismatch stops publication.
Uploading release files and publishing to the package registry are separate steps.

## Retry a failed release

For a temporary failure, rerun the original failed publish job. You can also run
`release.yml` manually from `main` with its `tag` input set to the existing release tag.
This retries the same version. It does not create or move tags or change version numbers.

The workflow skips release creation if the release already exists, skips packages already
in the registry, and can replace attached release files. Keep `PACKAGES_PUBLISH_ENABLED`
enabled while retrying. Publish a new version for content changes rather than replacing
a version that applications may already use.

Version `0.1.0` was published before the repository's documentation history was rewritten.
Its packages and symbols refer to the original source commit. Do not rebuild or replace
its release files from the rewritten tag.

If release files upload but package publication fails, check `packages: write`, package
access and the owner's feed, then retry the original job. If publication succeeds but
the test application cannot restore, check credentials, source mapping and repository
access before creating another release. Check symbol downloads separately from package restore.

See [installation](../docs/INSTALLATION.md) for feed and symbol setup. GitHub documents
[NuGet authentication and publication](https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-nuget-registry)
and [package access](https://docs.github.com/en/packages/learn-github-packages/configuring-a-packages-access-control-and-visibility).
NuGet documents [credential environment variables](https://learn.microsoft.com/en-us/nuget/consume-packages/consuming-packages-authenticated-feeds)
and [source mapping](https://learn.microsoft.com/en-us/nuget/consume-packages/package-source-mapping).
The .NET CLI documents [package push options](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-nuget-push).
