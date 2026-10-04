# Release automation

## Flow

Conventional Commits on `main` feed Release Please. It opens or updates a release PR that
changes `version.txt`, `Directory.Build.props`, the release manifest, and `CHANGELOG.md`.
After that PR merges, it creates the version tag and GitHub release. The same workflow
checks out the tag, validates, builds, tests, packs, attaches artifacts, and publishes
the package and symbols if publishing has been enabled.

Use scoped subjects with a body, for example `feat(schema): support nullable enums`.
`feat` and `fix` drive releases; use `!` or a `BREAKING CHANGE:` footer for breaking changes.
The current `0.1.0` is a development baseline, not an already published release.

## One-time external setup

1. Push the repository to `menk-dev/structly-ai` and enable GitHub Actions.
2. Configure `RELEASE_PLEASE_TOKEN` as a repository secret using a bot token with repository
   contents and pull-request read/write permissions. The default GitHub token does not
   trigger CI on bot-created release PRs. See the [Release Please action documentation](https://github.com/googleapis/release-please-action).
3. Require both CI matrix checks on `main`. Enable squash merges and preserve Conventional
   Commit subjects. For unattended release PR merging, enable repository auto-merge and
   set the `RELEASE_AUTO_MERGE` repository variable to `true`. Release PRs will then queue
   for squash auto-merge after required checks pass. Do not require a human review if the
   intended policy is fully unattended releases.
4. Confirm `Structly.AI` is available and choose a license; add license metadata and the
   license file to the package. Implement and validate the library before enabling publishing.
5. Create the `nuget` GitHub environment and set repository variable `NUGET_USER` to the
   NuGet account's profile name. Add a [NuGet trusted publishing policy](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing)
   for owner `menk-dev`, repository `structly-ai`, workflow `release.yml`, environment
   `nuget`, scoped to `Structly.AI` (including new-package publication if needed).
6. Set repository variable `NUGET_PUBLISH_ENABLED` to `true` when ready. Leave environment
   reviewers unset for unattended publication, or intentionally configure approval if desired.

No remote settings, secrets, trust policies, releases, or packages are created by initializing
this repository. Account setup cannot be completed by committed workflow files alone.

## Recovery

If publishing fails after release creation, rerun the failed publish job in the original
workflow run. A fresh workflow dispatch only reconciles releases; it does not republish an
existing release. Duplicate package uploads are skipped and release assets can be replaced.
NuGet package contents cannot be replaced for an existing version: release a new version
for a content correction.
