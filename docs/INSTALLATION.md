# Installation and debug symbols

## Consuming from your projects

Install from nuget.org. No GitHub token or additional package feed is required.
Install the core package, or install the hosting package for ASP.NET Core and Generic Host:

```sh
dotnet add package Structly.AI --version 0.2.0
# For ASP.NET Core and Generic Host; also installs the core package:
dotnet add package Structly.AI.Hosting --version 0.2.0
```

## Symbols

For each package, the workflow attaches `.nupkg` and `.snupkg` files to the GitHub Release.
It publishes `.nupkg` files to nuget.org with `--no-symbols`. Symbols are not
automatically indexed by a symbol server.

For debugging, download the matching `.snupkg`, extract its portable PDB, and add that
directory to your debugger's local symbol search path. Package validation checks Source
Link metadata. Check symbol downloads separately from package installation.

See the [maintainer release guide](../dev/RELEASES.md) for publication and recovery.
