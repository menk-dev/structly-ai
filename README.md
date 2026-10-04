# Structly.AI

A .NET library for typed, structured LLM output in automated workflows.

**Status: repository scaffold.** There is no client implementation or supported public API yet.
The intended NuGet package is `Structly.AI`, targeting .NET 10. Package-name availability
and the distribution license must be confirmed before publishing.

## Development

Install the SDK specified in `global.json`, then run:

```sh
dotnet restore Structly.AI.slnx --locked-mode
dotnet format Structly.AI.slnx --verify-no-changes --no-restore
dotnet build Structly.AI.slnx -c Release --no-restore
dotnet test --solution Structly.AI.slnx -c Release --no-build
dotnet pack src/Structly.AI/Structly.AI.csproj -c Release --no-build -o artifacts/packages
```

In Codex sessions, prefix shell commands with `rtk` as instructed by `AGENTS.md`.
Dependency changes require `dotnet restore --force-evaluate` and committing the updated lockfiles.

- `src/Structly.AI`: the single shipping library; currently an empty assembly.
- `tests/Structly.AI.Tests`: offline tests using xUnit and Microsoft.Testing.Platform.
- `docs/design.md`: reference assessment and decisions for the implementation phase.
- `docs/releases.md`: automation and the external setup needed to enable it.

`ref1/` and `ref2/` are local reference material, ignored by Git and excluded from the
solution and packages. Neither is a compatibility contract.

CI checks formatting, locked restore, compilation, tests, and packing on Linux and Windows.
Release Please manages version changes and changelogs from Conventional Commits; the release
workflow can publish through NuGet trusted publishing once configured. Publishing is disabled
until the library is ready.
