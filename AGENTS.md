## Coding Style & Naming Conventions

Use lowercase built-in type aliases for type declarations, such as `string`, `int`, and `bool`. When calling static members on framework types, use PascalCase type names, for example `String.IsNullOrEmpty(value)`.

Class fields should use readonly-first declarations with underscore-prefixed camelCase names, for example `readonly int _someVar;`. Prefer inline constructors where they keep dependencies clear and concise. Do not write explicit `private` or `internal` modifiers where C# already implies the same accessibility.

Program architecture should follow vertical slices: keep endpoint behavior, request models, validators, and feature-specific helpers close to the feature they serve. When working on FastEndpoints endpoints, follow the patterns in `AGENTS.fast-endpoints.md`.

Avoid abstraction unless it reduces meaningful duplication or is necessary because multiple implementations of the abstraction exist. Prefer direct, readable feature code over speculative interfaces or service layers.

Extract a method when its name communicates domain intent and its implementation hides incidental complexity or policy. Keep transparent construction and straightforward one-line delegation visible at the call site instead of wrapping them merely to shorten the caller. For example, a helper that sanitizes provider failures or applies a business rule is useful; a helper that only forwards to another method or replaces an already self-documenting constructor or object initializer is not.

Records with more than 2 unrelated properties should use explicit property definitions (optionally required, get/set/init, whatever is appropriate).
