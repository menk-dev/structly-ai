# Development

User guides are in [docs/](../docs/). This directory covers development and releases:

- [Design and tests](DESIGN.md)
- [Source structure](STRUCTURE.md)
- [Release setup, publication and recovery](RELEASES.md)

Use the SDK in `global.json` and follow [AGENTS.md](../AGENTS.md). Package versions are
listed in `Directory.Packages.props` and recorded in lockfiles. After changing a dependency,
restore with `--force-evaluate` and commit the updated lockfiles. Ignored `ref1/` and `ref2/`
directories contain old reference code. They are excluded from builds and packages; the
project must build without them.

## Dependency updates

Dependabot checks NuGet packages and GitHub Actions weekly. Patch and minor updates
merge automatically once the required Linux and Windows CI checks pass against the
current base. Major updates and all `Microsoft.CodeAnalysis`
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
rtk dotnet run --project examples/Structly.AI.HostingConsumer -c Release --no-build
rtk dotnet pack src/Structly.AI/Structly.AI.csproj -c Release --no-build -o artifacts/packages
rtk dotnet pack src/Structly.AI.Hosting/Structly.AI.Hosting.csproj -c Release --no-build -o artifacts/packages
rtk dotnet pack src/Structly.AI.Testing/Structly.AI.Testing.csproj -c Release --no-build -o artifacts/packages
rtk dotnet run --project tools/Structly.AI.PackageValidation -c Release --no-build -- artifacts/packages
rtk git diff --check
```

Tests run offline without provider credentials. Use fake HTTP handlers and response
data. Control streams and time when testing cancellation or timeouts. Live provider tests
are optional; get explicit approval before making paid calls.

Package validation checks package IDs, target frameworks, licenses, dependencies, contents,
documentation, debug symbols, source links and version numbers. It installs the core package
from a local feed in a separate application with a fresh cache. It also checks that the
packaged analyzer rejects an unsupported output type during compilation.

Old implementation plans and resolved audit reports are available in Git history. Keep
current behavior documented in the user guides, code and tests.
