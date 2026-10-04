# Releases

Update the version manually, merge the release PR, then push a version tag to publish.
GitHub Actions uses NuGet trusted publishing to obtain a temporary API key.
GitHub Release creation uses `GITHUB_TOKEN`; no long-lived publishing token is needed.

## Prepare and publish

1. Update `version.txt`, `Directory.Build.props` and `CHANGELOG.md` in a PR. Add an exact
   `## VERSION` heading to the changelog. Update installation examples to the new version.
   Use scoped Conventional Commits with a body.
2. Run the checks in [the development guide](README.md), then merge after the required
   Linux and Windows CI checks pass.
3. Fetch `main` and tag the release commit. For version `0.4.0`:

   ```sh
   git switch main
   git pull --ff-only
   git tag v0.4.0
   git push origin v0.4.0
   ```

4. Check the release workflow. It verifies that the tag belongs to `main`, runs tests,
   builds and validates all three packages, creates the GitHub Release, attaches packages and
   debug symbols, and publishes the packages to nuget.org. It then installs the
   core package from that feed in a fresh test application and runs it.
5. Confirm that the release files download and that your applications can install all three
   `Structly.AI`, `Structly.AI.Hosting` and `Structly.AI.Testing` from nuget.org.

The packages target .NET 10, use the MIT license with copyright `menk-dev`, and publish to nuget.org.
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

The workflow logs into NuGet.org as `menk-dev` (the profile name, not email).
The policy must match repository owner `menk-dev`, repository `structly-ai`, workflow
`release.yml`, and environment `github-packages`. Allow new packages and new versions
matching `Structly.AI*`, under the NuGet owner that will own all three packages.

The workflow uses `id-token: write` and `NuGet/login@v1` to obtain a temporary API key,
then publishes to `https://api.nuget.org/v3/index.json`. The environment name remains
`github-packages` to match the trusted publishing policy.

## Package checks

CI and the release workflow build all three packages and run `tools/Structly.AI.PackageValidation`.
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

If release files upload but package publication fails, check the NuGet login username,
`id-token: write`, the trusted publishing policy and its package scopes, then retry.
If publication succeeds but the test application cannot restore, allow time for nuget.org
indexing before retrying. The workflow makes 30 restore attempts, waiting 20 seconds between attempts.
Check symbol downloads separately from package restore.

See [installation](../docs/INSTALLATION.md) for package and symbol setup.
NuGet documents [trusted publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing).
The .NET CLI documents [package push options](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-nuget-push).
