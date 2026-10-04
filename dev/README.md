# Development

Consumer documentation lives in [docs/](../docs/). This directory contains ongoing
maintainer guidance:

- [Design and regression boundaries](DESIGN.md)
- [Release setup, publication and recovery](RELEASES.md)

Use the SDK in `global.json` and follow [AGENTS.md](../AGENTS.md). Dependencies are
centrally managed and locked. Dependency changes require restore with `--force-evaluate`
and committed lockfiles. Ignored `ref1/` and `ref2/` are historical assessment material,
excluded from the solution and packages; development must not depend on their presence.

## Dependency updates

Dependabot checks NuGet packages and GitHub Actions weekly. Patch and minor updates
automatically enable squash merging; both required Linux and Windows CI checks must pass
against the current base before merging. Major updates and all `Microsoft.CodeAnalysis`
updates require maintainer review because analyzer compatibility depends on consumers'
compiler versions. Merged branches are deleted automatically.

The auto-merge workflow reads Dependabot metadata without checking out or executing PR
code. It does not bypass branch protection or approve major updates.

## Local verification

CI runs these checks on Linux and Windows. Commands below use the repository's RTK proxy:

```sh
rtk dotnet restore Structly.AI.slnx --locked-mode
rtk dotnet format Structly.AI.slnx --verify-no-changes --no-restore
rtk dotnet build Structly.AI.slnx -c Release --no-restore
rtk dotnet test --solution Structly.AI.slnx -c Release --no-build
rtk dotnet pack src/Structly.AI/Structly.AI.csproj -c Release --no-build -o artifacts/packages
rtk dotnet pack src/Structly.AI.Hosting/Structly.AI.Hosting.csproj -c Release --no-build -o artifacts/packages
rtk dotnet run --project tools/Structly.AI.PackageValidation -c Release --no-build -- artifacts/packages
rtk git diff --check
```

Normal tests are deterministic, offline and independent of provider credentials. Use fake
HTTP handlers, synthetic response/SSE fixtures, gated streams and controlled time where
practical. Live provider checks are optional and paid calls require explicit authorization.

Package validation checks identity, framework, license, dependencies, file allowlist,
README/XML documentation, portable PDB/Source Link and version/changelog consistency.
It runs a consumer outside the repository with a fresh cache and local feed only, then
requires a compilation failure from the packed analyzer for an unsupported DTO.

Completed implementation plans, handoffs and fixed audit reports were removed. Git history
retains those records; current behavior belongs in consumer guides, code and regression tests.
