# Phase 7 handoff

Status: partial, 2026-10-04. Local publishing preparation is complete; external activation
and first publication remain pending.

The owner selected GitHub Packages for use in their own projects instead of NuGet.org.
This supersedes the NuGet.org setup inputs in the phase 6 handoff. Package identity,
.NET 10 support, MIT license and first version 0.1.0 remain unchanged.

The release workflow now publishes the runtime package to the repository owner's GitHub
NuGet registry with GITHUB_TOKEN and job-scoped packages:write permission. The activation
variable is GITHUB_PACKAGES_PUBLISH_ENABLED and the environment is github-packages.
NuGet/login, NUGET_USER and OIDC permissions are removed. Both nupkg and snupkg remain
GitHub Release attachments; symbols are distributed as a download rather than through
an automatic symbol-server indexing flow. Existing offline checks and tag/package version
validation remain required before publication.

RELEASES documents account setup, authenticated consumer restore, package source mapping,
cross-repository workflow access and original-run recovery. README links consumer feed
setup, and PLAN reflects the selected registry and symbol distribution.

Initial remote activation: pushed main to menk-dev/structly-ai and confirmed owner admin
access and enabled Actions. Enabled squash auto-merge with PR title/body as commit defaults,
created the github-packages environment without required reviewers, and protected main
with both required CI matrix checks, current-base enforcement, linear history and admin
enforcement. Publishing and release auto-merge variables remain unset until the release
PR prerequisites are verified. No credentials or packages were published. Still required:
verify the release bot, required Linux/Windows checks and auto-merge; configure the
github-packages environment and package access; explicitly activate publication; verify
the first release PR proposes 0.1.0 and passes checks before merge; confirm tag, changelog,
package and release artifacts agree; restore/run a real consumer from the published feed
and download the symbol artifact. Keep phase 7 partial until these checks succeed.

## Local verification

Both workflow YAML files parsed; their shell steps passed bash syntax checks. Publication
configuration checks confirmed the gate, environment, job permissions and single runtime
package push, with no NuGet login action. The documented consumer feed XML parsed and
Git diff whitespace checks passed. Official GitHub/NuGet sources were reviewed for token
authentication, repository access, credential environment variables and source mapping.
Runtime code and dependencies are unchanged, so the offline .NET suite was not rerun.
Actual Actions execution and authenticated registry consumption remain external checks.

The first release workflow run failed because RELEASE_PLEASE_TOKEN is absent. It also
exposed empty release PR JSON evaluation in the auto-merge step; the expression now
uses an empty-object fallback so missing PR output does not produce a template error.
The owner must configure a dedicated release token in GitHub Secrets.
