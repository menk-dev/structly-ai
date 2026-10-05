# Installation and debug symbols

## Consuming from your projects

Install from nuget.org. No GitHub token or additional package feed is required.
Install the core package, or install the hosting package for ASP.NET Core and Generic Host:

```sh
dotnet add package Structly.AI --version 0.7.0
dotnet add package Structly.AI.OpenAI --version 0.7.0
```

For ASP.NET Core and Generic Host, install the hosting package, which also installs the core:

```sh
dotnet add package Structly.AI.Hosting --version 0.7.0
```

For offline envelope builders, install the testing package alongside the matching core:

```sh
dotnet add package Structly.AI.Testing --version 0.7.0
```

See the [hosting](hosting.md) and [offline testing](testing.md) guides for usage.

## Symbols

For each package, the workflow attaches `.nupkg` and `.snupkg` files to the GitHub Release.
It publishes `.nupkg` files to nuget.org with `--no-symbols`. Symbols are not
automatically indexed by a symbol server.

For debugging, download the matching `.snupkg`, extract its portable PDB, and add that
directory to your debugger's local symbol search path. Package validation checks Source
Link metadata. Check symbol downloads separately from package installation.

See the [maintainer release guide](../dev/releases.md) for publication and recovery.

For Mistral, install `Structly.AI.Mistral --version 0.7.0` and follow [the Mistral guide](mistral.md).
