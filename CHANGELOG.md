# Changelog

## Unreleased

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
