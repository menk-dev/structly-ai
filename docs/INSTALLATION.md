# Installation and debug symbols

## Consuming from your projects

To install locally, use a GitHub personal access token (classic) with `read:packages`
and access to the package. GitHub requires authentication even for public packages.
Add the feed alongside nuget.org in your application's `nuget.config`. The library's
own build only needs public dependencies, so it does not use this feed.

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
      <package pattern="Structly.AI.Hosting" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
```

Set `NuGetPackageSourceCredentials_github` in the local process environment to
`Username=YOUR_GITHUB_USERNAME;Password=YOUR_TOKEN`, using your credential manager or
secure shell entry. Keep tokens out of committed configuration and command history.
Install the core package, or install the hosting package for ASP.NET Core and Generic Host:

```sh
dotnet add package Structly.AI --version 0.2.0
# For ASP.NET Core and Generic Host; also installs the core package:
dotnet add package Structly.AI.Hosting --version 0.2.0
```

Source mapping restores both Structly packages from GitHub and other dependencies from
nuget.org. Commands that query package metadata may still contact all configured sources.

In a consuming GitHub Actions repository, grant that repository Actions access in the
package settings, set job permission `packages: read`, and supply the credential environment
variable with `Username=${{ github.actor }};Password=${{ secrets.GITHUB_TOKEN }}` during restore.
A token from another repository needs access to each package. Sharing a repository owner
does not grant that access automatically.

## Symbols

For each package, the workflow attaches `.nupkg` and `.snupkg` files to the GitHub Release.
It publishes `.nupkg` files to GitHub Packages with `--no-symbols`. Symbols are not
automatically indexed by a symbol server.

For debugging, download the matching `.snupkg`, extract its portable PDB, and add that
directory to your debugger's local symbol search path. Package validation checks Source
Link metadata. Check symbol downloads separately from package installation.

See the [maintainer release guide](../dev/RELEASES.md) for publication and recovery.
