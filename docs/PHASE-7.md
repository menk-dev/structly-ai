# Phase 7 handoff

Status: complete, 2026-10-04.

## Delivered behavior

Structly.AI 0.1.0 is published to GitHub Packages. The release workflow restored and ran
an isolated consumer directly from the authenticated registry after upload. Both the
runtime package and symbol package are downloadable from the GitHub Release:
https://github.com/menk-dev/structly-ai/releases/tag/v0.1.0

The owner selected GitHub Packages for personal projects and manual version updates/tagging.
These decisions supersede the phase 6 NuGet.org and Release Please setup. No release bot
secret, NuGet account or trusted publishing policy is needed. Scoped Conventional Commits,
.NET 10, MIT license and package identity remain unchanged.

## Release process and safeguards

Update version.txt, Directory.Build.props and CHANGELOG.md through an ordinary PR, merge
after required CI passes, and push a v-prefixed tag on main. The release job verifies main
ancestry, version/changelog agreement, offline behavior, package metadata and fresh consumer
consumption before creating the release and uploading packages. Post-upload verification
uses the authenticated GitHub feed only, an isolated cache and bounded availability retries.
Explicit dispatch with an existing tag supports recovery without inventing new versions.

GitHub Actions is enabled. Main requires validate (ubuntu-latest) and validate (windows-latest),
current-base enforcement, linear history and admin enforcement. Force pushes and branch
deletion are disabled. Squash auto-merge is available with PR title/body as commit defaults;
version bumps and release tags remain manual. The github-packages environment has no required
reviewers. PACKAGES_PUBLISH_ENABLED=true activates tag publication. The publishing job uses
GITHUB_TOKEN with contents:write and packages:write. Cross-repository package access and
local read:packages credentials are documented in RELEASES.

## Identity cleanup

The owner authorized retroactive commit identity correction before publication. All local
refs and eight remote branch histories were rewritten, replacing the previous email with
the owner-selected address in identities, commit messages and file content. Main's file
tree was unchanged. Atomic force-with-lease updates preserved the inspected remote state.
Temporary admin force-push access was restored immediately; the protected policy was
verified afterward. Reflogs were expired, unreachable objects pruned, and all 256 remaining
local Git objects passed cleanup verification. Subsequent fetched remote histories also
passed. Future repository commits use the owner-selected email.

GitHub-controlled cached commit views and pull-request refs cannot be erased by force-push.
Other clones must use the rewritten history to avoid reintroducing the old commits. No claim
is made that those external caches or other clones were purged.

## Verification and publication

Local manual-release checks passed: locked restore, formatting, Release build with zero
warnings/errors, 437 offline tests, package creation, metadata/content/XML/PDB/Source Link,
a fresh local-feed consumer and packed analyzer rejection. Workflow YAML/shell syntax and
Git whitespace checks passed. Linux and Windows CI passed before each workflow change merged.

At publication, tag v0.1.0 resolved to a1bc60f5bbbe7352648318c386e5f46f0632de18. Version.txt,
Directory.Build.props, changelog, packed versions and the tag agree on 0.1.0.
Release run 37218608918 succeeded:
https://github.com/menk-dev/structly-ai/actions/runs/37218608918

Both registry upload and authenticated fresh-consumer verification steps succeeded. Main
CI run 37218578511 also passed. Runtime and symbol assets were independently downloaded:
Structly.AI.0.1.0.nupkg (105669 bytes) and Structly.AI.0.1.0.snupkg (25379 bytes). Downloaded
package metadata confirms 0.1.0 and the tagged source revision; the symbol archive contains
the runtime portable PDB. Symbols are release downloads, without automatic server indexing.

## Recovery adjustments and limitations

The initial activation run exposed Windows CRLF checkout conflicting with editorconfig;
.gitattributes now preserves LF. An absent Release Please token initially blocked the old
bot flow; manual publication removed that dependency. GitHub rejects GITHUB_-prefixed
repository variables, so the gate is PACKAGES_PUBLISH_ENABLED. The first tag attempt
skipped before creating any release/package; the unpublished tag was removed before retrying
with the corrected workflow. No published package was replaced and no extra version was
created to conceal a failure.

Future consumers need the documented authenticated feed and package access. The publisher's
consumer check verifies same-repository access; access for each additional consuming repository
must be granted independently. No provider calls were made. Extra providers/frameworks and
runtime capabilities beyond the approved scope remain deferred as in earlier handoffs.

Owner-authorized Git history cleanup may subsequently change commit and tag hashes.
Published package and symbol contents remain immutable and retain their original source
revision. Documentation-only history cleanup does not trigger another package upload.
