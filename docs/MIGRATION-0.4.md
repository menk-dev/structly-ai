# Migrating to 0.4.0

Update core, hosting and testing packages together to 0.4.0.

- Remove placeholder instructions from tasks used only for schema inspection, validation or
  examples. Typed execution and typed prewarming still need task or request instructions.
- Handle `CredentialsMissing` for absent/blank selected credentials. `Authentication` remains
  for malformed keys, resolver failures and provider authentication rejection. Enum values
  existing before 0.4.0 keep their numeric values.
- Usage events require `Provider`, `RequestedModel` and `Usage` when constructed manually.
  Use requested model as the accounting fallback when resolved model is absent.
- Use `CreateExample` and the optional testing package instead of schema-based fixture generators.
- Read timeout budgets and `HttpStatusCodeValue` directly from errors. Invalid-output messages
  now include bounded escaped issue paths/codes; complete `Issues` remains available.
- Configure `VocabularyOrder.PreserveInput` when vocabulary order is meaningful. Ordinal sorting
  remains the default. Explicit application cache compatibility overrides built-in exact IDs.
- Hosted `OPENAI_API_KEY` fallback requires `UseEnvironmentApiKey = true`; default behavior
  remains explicit credentials. Custom resolvers take precedence without blank-result fallback.

Batch retries, polling, persistence, cleanup and durable accounting deduplication remain
application responsibilities. See [batches](BATCHES.md) and [testing](TESTING.md).
