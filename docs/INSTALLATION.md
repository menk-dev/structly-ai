# Installation and symbols

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


See the [maintainer release guide](../dev/RELEASES.md) for publication and recovery.
