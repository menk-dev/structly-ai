# Changelog

## 0.6.0

- Breaking: batch imports return `BatchImportResult<T>` with ordered item wrappers exposing nonblank custom IDs and each item result.
- Add per-model reported batch usage sums with per-field coverage, including usage from failed output processing and nonfatal overflow warnings.
- Add `.AddUsageObserver<TObserver>()` in Hosting with a separate async DI scope per notification and request/import observer overrides.
- Add request-schema completion and asynchronous offline responders to the testing fixture, with construction-time client options and queued-response precedence.
- Add coarse error categories, safe error summaries and exception mapping to simplify consumer failure handling.
- Add image parts constructed from supported byte signatures.

## 0.5.0

- Add optional typed task references for hosting registration and execution; named string APIs remain available.
- Add validated task execution defaults for total timeout, streaming inactivity timeout and output-token limits, with per-call overrides.
- Add nullable flow annotations and `TryGetValue` to structured results.

- Add instruction-only task creation and text-input execution overloads for tasks and bound output.
- Add a disposable OpenAiTestFixture with queued envelopes, request snapshots and unused-response counts.
- Add reusable named AI tasks through `StructlyAi` in the hosting package.
- Add bound output contracts and typed conversations with independent output and configuration branches.
- Reorganize user guides and clarify runnable examples.

## 0.4.0

- Add validated examples, optional task instructions, vocabulary ordering, timeout budgets, missing-credential classification and direct usage-event accounting fields.
- Add callback hosting configuration and opt-in environment credentials; supply exact GPT-6 cache defaults.
- Add batch files, lifecycle operations, prepared embedding/typed Responses batches and manifest-based streaming result imports.
- Add Structly.AI.Testing envelope builders and explicit example completion.
- Breaking: missing credentials now return CredentialsMissing; typed execution still requires effective instructions.

## 0.3.0

- Supports default built-in string-enum converter attributes on enum types and properties.
- Preserves declaration order for properties and enum entries, with `JsonPropertyOrder`
  taking precedence for properties.
- Includes schema names in typed result and usage metadata.
- Treats explicitly null OpenAI request options as defaults and supports per-request
  instructions, including typed prewarming.
- Clarifies description attributes and developer-message instructions in the documentation.

## 0.2.0

- Adds `Structly.AI.Hosting` for ASP.NET Core and Generic Host. It registers `OpenAiClient`
  with dependency injection, reads settings from configuration, validates them at startup,
  and manages HTTP handlers through `IHttpClientFactory`.
- Rewrites the user and maintainer guides in plain language and documents installation
  of both packages.
- Updates GitHub Actions and enables automatic merging for Dependabot patch and minor
  updates after CI passes. Major and Roslyn updates still require review.

## 0.1.0

- First .NET 10 release. Supports typed and free-text OpenAI output, schema validation,
  streaming, text and image input, follow-up requests, and dynamic vocabularies.
- Includes timeouts, token usage callbacks, cache settings, embeddings and image generation.
- Includes a schema analyzer, user guides and an example that runs without provider calls.
