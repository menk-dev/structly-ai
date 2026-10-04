## Coding Style & Naming Conventions

Use lowercase built-in type aliases for type declarations, such as `string`, `int`, and `bool`. When calling static members on framework types, use PascalCase type names, for example `String.IsNullOrEmpty(value)`.

Class fields should use readonly-first declarations with underscore-prefixed camelCase names, for example `readonly int _someVar;`. Prefer inline constructors where they keep dependencies clear and concise. Do not write explicit `private` or `internal` modifiers where C# already implies the same accessibility.

Organize code by feature: keep endpoint behavior, request models, validators, and feature-specific helpers together.

Add an abstraction when it removes meaningful duplication or supports multiple existing implementations. Otherwise, use direct feature code rather than interfaces or service layers for possible future uses.

Extract a method when its name explains what the code does and its implementation contains details the caller does not need to follow. Keep straightforward construction and one-line delegation at the call site rather than wrapping them just to shorten the caller. A helper that sanitizes provider errors or applies a business rule is useful. A helper that only forwards a call or replaces a clear constructor or object initializer is not.

Records with more than two unrelated properties should declare those properties explicitly. Use required, get, set, or init as appropriate.

## Documentation

Write for programmers in plain, precise language. Keep technical details, examples, and limitations. Explain what code does and where behavior must be implemented. Use words in their literal technical meaning; for example, describe resource ownership and disposal precisely, and say that applications implement retries rather than that a host "owns" them. Avoid figurative descriptions of code and management language.
