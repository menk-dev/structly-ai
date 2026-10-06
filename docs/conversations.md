# Bound output and conversations

With an application-defined `ticketTask` and `vocabularies`, bind runtime output settings once
to keep the schema and generated guidance stable:

```csharp
var output = ticketTask.BindOutput(new()
{
    Vocabularies = vocabularies,
    OutputSpecification = new() { IncludeExample = true }
});
```

Binding copies referenced vocabulary values, validates the effective schema, and generates
optional guidance before credentials or HTTP are involved. `CreateSchema()` returns a
detached schema; `ReadOutput()` validates against the captured values. You can also call
`client.ExecuteAsync(output, request)` directly; request vocabularies and output
specifications must be absent.

## Start and continue a conversation

Use the bound output with a configured `OpenAiClient`:

```csharp
var conversation = client.CreateConversation(output);
await conversation.ExecuteAsync(new() { Input = "Extract this issue..." });
await conversation.ExecuteAsync(new() { Input = "Correct the reference..." });
```

Conversation creation resolves instructions and model settings from conversation options,
the original task, and client defaults. Turns accept new user input and execution controls.
Messages must contain only user roles. Reasoning summaries configured at creation require
streaming on every turn.

OpenAI-backed conversations send `store: true`, even when `OpenAiClientOptions.Store`
is false. Responses are retained at OpenAI and later turns reference the last successful
response. Failures leave the local continuation position
unchanged, but may still have been stored or billed. Accounting and cancellation metadata
remain available. The library does not retry or restart unavailable provider history.

Mistral conversations also retain provider state through its beta Conversations API.
See [Mistral conversations](mistral.md#conversations) for continuation and configuration changes.

## Branch with different output settings

The following branches use application-defined `updatedVocabularies` and `planTask`:

```csharp
var updated = conversation.ChangeOutput(
    ticketTask.BindOutput(new() { Vocabularies = updatedVocabularies }));
var planning = conversation.ChangeOutput(planTask.BindOutput());
```

`ChangeOutput` returns an independent typed branch at the current position. It preserves
configuration and the original credential fallback, including when the new task specifies
other instructions, models, or credentials. `ChangeConfiguration(conversation.Configuration
with { Instructions = "Revised instructions" })` explicitly creates a configuration branch.
Both originals remain usable; branches advance independently. Overlapping operations on
the same conversation throw `InvalidOperationException`.

Existing task/request execution, manual continuation, batch, and prewarming APIs remain
available without migration. Use those advanced APIs for per-call structural overrides,
provider storage controls, cache controls, or raw capture. Binding and conversations make
no promises about cache writes, hits, or savings.

See [client configuration](configuration.md), [execution and cancellation](execution.md),
and [manual follow-up requests](advanced.md#follow-up-requests).
