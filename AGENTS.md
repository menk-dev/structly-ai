## Coding Style & Naming Conventions

Use lowercase built-in type aliases for type declarations, such as `string`, `int`, and `bool`. When calling static members on framework types, use PascalCase type names, for example `String.IsNullOrEmpty(value)`.

Class fields should use readonly-first declarations with underscore-prefixed camelCase names, for example `readonly int _someVar;`. Prefer inline constructors where they keep dependencies clear and concise. Do not write explicit `private` or `internal` modifiers where C# already implies the same accessibility.

Organize code by feature: keep endpoint behavior, request models, validators, and feature-specific helpers together.

Give each top-level public type its own file named after the type, including enums and attributes. Use `OfT` in filenames to distinguish generic and nongeneric types with the same name. Name partial implementation files after the type and responsibility, for example `OpenAiClient.BatchImport.cs`.

Keep substantial internal types in separate files. Small nested types used only by their containing implementation or test may stay there. Put shared test fixtures in `Support/` and group tests by the feature they exercise. Avoid catch-all folders and filenames such as `Helpers` or `AdvancedContracts`.

Folders describe source responsibilities; existing public namespaces do not need to match folders. Preserve public namespaces when moving files so consumers keep their existing imports. See [dev/STRUCTURE.md](dev/STRUCTURE.md) for the project layout.

Add an abstraction when it removes meaningful duplication or supports multiple existing implementations. Otherwise, use direct feature code rather than interfaces or service layers for possible future uses.

Extract a method when its name explains what the code does and its implementation contains details the caller does not need to follow. Keep straightforward construction and one-line delegation at the call site rather than wrapping them just to shorten the caller. A helper that sanitizes provider errors or applies a business rule is useful. A helper that only forwards a call or replaces a clear constructor or object initializer is not.

Records with more than two unrelated properties should declare those properties explicitly. Use required, get, set, or init as appropriate.

## Documentation

Write for programmers in plain, precise language. Keep technical details, examples, and limitations. Explain what code does and where behavior must be implemented. Use words in their literal technical meaning; for example, describe resource ownership and disposal precisely, and say that applications implement retries rather than that a host "owns" them. Avoid figurative descriptions of code and management language.

## Commits, Pull Requests & Merging

Use Conventional Commits with a scope and an explanatory body. Commit completed work after running the relevant checks in [dev/README.md](dev/README.md).

`main` is protected and requires CI status checks. Publish changes on a feature branch and merge through a pull request. A request to push authorizes publishing the feature branch; a request to handle the pull request and merge authorizes creating the pull request, fixing check failures, and merging after the required checks pass.

1. Check the working tree, current branch, upstream and unpushed commits. Fetch `origin` before publishing. If the remote has advanced, rebase unpublished commits onto `origin/main`, resolve conflicts and rerun checks affected by the changes. Do not force-push `main` or bypass its protections.
2. Push the feature branch with `rtk git push -u origin <branch>`. If completed commits are already on local `main`, create a feature branch at that commit before continuing.
3. Look for an existing pull request with `rtk gh pr list --head <branch>`. Create one if needed, targeting `main`, with a Conventional Commit title and a description of the final behavior, relevant implementation details and validation. Write multiline descriptions to a temporary file and pass `--body-file` to `rtk gh pr create` or `rtk gh pr edit`.
4. Inspect checks with `rtk gh pr checks <number>` and pull request status with `rtk gh pr view <number>`. Wait for the required checks on the latest commit. Investigate failures with `rtk gh run view <run-id> --log-failed`, fix them on the same branch and rerun relevant local checks before pushing. Preserve required reviews and other branch protections.
5. When merge is authorized and the required checks and reviews are satisfied, use `rtk gh pr merge <number> --squash --delete-branch`. Verify the merged state, then fetch and synchronize local `main` with `origin/main`. If local `main` contains the original commits from a squash merge, verify that they are fully represented in the merged result before moving its branch pointer; do not discard unrelated work.
6. Report the pull request URL, merge result and any remaining blocker. Enabling auto-merge is not the same as completing a merge; verify the final state before reporting completion.

Prefix shell commands with `rtk`. Use `rtk proxy` when raw output or direct command behavior is necessary. In particular, use `rtk proxy dotnet format` to apply formatting and `rtk proxy dotnet test` if RTK's invocation reports zero tests while the Microsoft Testing Platform runner succeeds directly. Sandbox restrictions can block Git metadata writes, network access or local MSBuild/test-runner pipes; request tool escalation for those operations when needed.
