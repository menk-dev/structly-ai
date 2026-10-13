# OpenAI Decisions

Decisions evaluate shared evidence against one or more questions and return a typed answer
for every property in your contract. Use them for finite choices, predicate probabilities
and scores against an ordered rubric. Use structured output for extracting arbitrary fields
or generating explanations.

The [OpenAI Decisions guide](https://developers.openai.com/api/docs/guides/decisions)
currently documents a public beta using `gpt-6-luna`. Structly sends one request to
`POST /v1/decisions`; your application selects the model and handles retries.

## Define the answers and questions

Install `Structly.AI` and `Structly.AI.OpenAI`. Answer types and decision tasks are in
`Structly.AI.OpenAI`:

```csharp
using Structly.AI;
using Structly.AI.OpenAI;
using System.ComponentModel;

public sealed record TicketDecision
{
    public required DecisionChoice<Department> Department { get; init; }
    public required DecisionPredicate Blocking { get; init; }
    public required DecisionScore Severity { get; init; }
}

public enum Department
{
    [Description("Payments, invoices, and refunds.")]
    Billing,
    [Description("Problems using the product.")]
    Technical,
    [Description("Requests outside these categories.")]
    Other
}
```

Instructions belong to the task, not to attributes on answer properties:

```csharp
var task = DecisionTask.Create<TicketDecision>(questions => questions
    .Ask(x => x.Department, "Which department should handle this request?")
    .Ask(x => x.Blocking, "Does this issue prevent the customer from working?")
    .Ask(x => x.Severity, "How severe is this issue?", levels:
    [
        new("Cosmetic", "Appearance only; no lost functionality."),
        new("Workaround", "A task fails, but another way works."),
        new("Blocked", "A task fails with no workaround.")
    ]));
```

Each included property must have exactly one direct property selector. Task creation
validates the contract and copies all choices and levels without credentials or HTTP.
Tasks are immutable and can be reused concurrently. Create another task with the same
contract when the context changes the instructions, choices or rubric.

## Execute a task

Decision models are configured separately from Responses models. `DefaultModel` remains
required by the client constructor; Decisions use only `DefaultDecisionModel` or an explicit
per-call selection:

```csharp
using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
var client = new OpenAiClient(http, new()
{
    DefaultModel = new() { ModelId = "your-responses-model" },
    DefaultDecisionModel = new() { ModelId = "gpt-6-luna" },
    CredentialResolver = Credentials.FromEnvironment("OPENAI_API_KEY")
});

var result = await client.DecideAsync(task, "I was charged twice for my order.");
if (result.IsSuccess)
{
    var answer = result.Value;
    Console.WriteLine($"Department: {answer.Department.Choice}, confidence: {answer.Department.Confidence}");
    Console.WriteLine($"Blocking probability: {answer.Blocking.Probability}");
    Console.WriteLine($"Severity: {answer.Severity.Score}");
}
else
    Console.WriteLine($"{result.Error.Kind}: {result.Error.Message}");
```

Per-call `OpenAiDecisionRequest.ModelSelection` takes precedence over `DefaultDecisionModel`.
Either can select a named `DecisionProfiles` entry. Profiles require explicit model IDs
without reasoning settings. Decisions never inherit the Responses model.

## Strings and custom choices

`DecisionChoice<T>` supports `string`, `bool`, and non-flags enums. Enum members supply
the default choices; `DescriptionAttribute` explains each member and
`JsonStringEnumMemberNameAttribute` controls its wire value. Duplicate enum values or
serialized names are rejected. Booleans infer `false` and `true`.

Strings require a finite list of allowed values:

```csharp
public sealed record TeamDecision
{
    public required DecisionChoice<string> Team { get; init; }
}

var task = DecisionTask.Create<TeamDecision>(questions => questions
    .Ask(x => x.Team, "Which team should handle this request?",
        choices: ["billing", "technical", "shipping"]));
```

Use `DecisionOption<T>` when the values need descriptions:

```csharp
var choices = teams
    .Select(team => new DecisionOption<string>(team.Id, team.Description))
    .ToArray();

var task = DecisionTask.Create<TeamDecision>(questions => questions
    .Ask(x => x.Team, routingInstructions, choices));
```

Each question requires 2–255 distinct choices. Strings must be nonblank. Explicit lists
can restrict an enum to a subset or describe the boolean alternatives. Enum descriptions
are inherited unless an explicit option supplies its own description. The returned
`Choice` is the exact allowed string, bool or enum value, not a domain object.

## Single questions

Scalar tasks return the answer wrapper directly:

```csharp
var predicate = DecisionTask.Create<DecisionPredicate>("Does this describe a billing problem?");
var result = await client.DecideAsync(predicate, input);
if (result.IsSuccess)
    Console.WriteLine(result.Value.Probability);

var category = DecisionTask.CreateChoice<Department>("Which department should handle this?");
var team = DecisionTask.CreateChoice<string>("Which team should handle this?",
    choices: ["billing", "technical"]);
var score = DecisionTask.CreateScore("How severe is this issue?",
    levels: [new("Minor"), new("Major")]);
```

`CreateChoice<T>` also accepts described options. Scalar tasks use `answer` as their wire
question name.

## Interpret answers and failures

- A predicate returns the model's estimated `Probability` from 0 to 1. Applications choose
  thresholds; Structly does not turn it into a boolean.
- A choice returns `Choice`, separate provider `Confidence`, and `Probabilities` in task
  choice order. Each probability entry has a typed `Value` and `Probability`.
- A score returns a fractional `Score`, separate `Confidence`, and `Probabilities` in rubric
  order. Entries contain the zero-based `Value`, `Label`, and `Probability`. Scores use
  positional indices, not enum numeric values. Provide at least two distinct rubric levels.

Structly validates finite numbers, ranges, allowed values and complete distributions.
Distribution sums and weighted-score consistency use an absolute tolerance of `1e-4`.
Provider numbers are preserved, never normalized or repaired. Answers are matched by
question name, so a reordered response still maps to the correct properties.

Success guarantees a complete contract. Any question refusal returns `Refused` with no
partial value. Malformed responses return `InvalidResponse`; exceptions in application
constructors or setters return sanitized `InvalidOutput`. Usage and available request/model
identifiers remain available on failures. Missing usage stays unknown. Decisions do not
currently provide a response ID in the response body.

Contracts are flat concrete reference DTOs with nonnullable answer properties and public
setters/init accessors or JSON constructor binding. `JsonPropertyName`, `JsonConstructor`
and unconditional `JsonIgnore` are supported. CLR property names identify fixed wire questions,
even when a JSON name differs. `DecisionSet<TAnswer>` properties contain runtime question collections. Nested contracts, nullable answers, custom converters,
polymorphism and conditional ignoring are unsupported. The structured-output analyzer does
not validate decision contracts; task creation performs their validation.

## Evidence and execution controls

Use `OpenAiDecisionRequest` for per-call settings:

```csharp
var result = await client.DecideAsync(task, new OpenAiDecisionRequest
{
    Messages =
    [
        new(MessageRole.User,
        [
            new TextPart("Inspect this product."),
            ImagePart.FromBytes(imageBytes)
        ])
    ],
    TotalTimeout = TimeSpan.FromSeconds(10),
    CorrelationId = "local-ticket-id",
    SafetyIdentifier = "opaque-user-id"
}, cancellationToken);
```

Supply exactly one of nonblank `Input` or nonempty `Messages`. Only user messages, text
and inline PNG/JPEG/WebP/GIF base64 images are supported, with at most 128 images. Cache
breakpoints are rejected. Structly never downloads an image. The OpenAI guide requires
inline images while the [API reference](https://developers.openai.com/api/reference/resources/decisions/methods/create)
also lists hosted URLs; this release conservatively accepts only inline images.

Requests and message collections are copied before credential resolution. Total timeout,
caller cancellation, response-size limits, credential selection and bounded usage observers
use the same [execution rules](execution.md) as other OpenAI operations. `UsageObserver`
and `CredentialResolver` override their client defaults. `SafetyIdentifier` is sent to
OpenAI and has a maximum length of 128 characters; `CorrelationId` stays local.

Raw response capture is opt-in through `CaptureRawResponse` and can contain sensitive
answers. No automatic logging, retries, model fallback, streaming, per-call question
overrides, batches or named hosting task registration are included.

Run the [offline consumer](../examples/README.md) for a complete routing example without
provider access or real credentials.

## Dynamic question collections

Use `DecisionSet<TAnswer>` when question identifiers and instructions come from runtime data.
A DTO can combine fixed answers with predicate, choice, and score collections:

```csharp
public sealed record MiniatureDecision
{
    public required DecisionPredicate IsMiniature { get; init; }
    public required DecisionSet<DecisionPredicate> Traits { get; init; }
    public required DecisionSet<DecisionChoice<bool>> Conditions { get; init; }
}

IReadOnlyDictionary<string, string> traits = new Dictionary<string, string>
{
    ["helmet"] = "Does the miniature wear a helmet?",
    ["shield"] = "Does the miniature carry a shield?",
};

var task = DecisionTask.Create<MiniatureDecision>(questions => questions
    .Ask(x => x.IsMiniature, "Does the photo show a miniature?")
    .AskEach(x => x.Traits, traits)
    .AskEach(x => x.Conditions, new Dictionary<string, string>
    {
        ["base"] = "Is the base damaged?",
    }));

var result = await client.DecideAsync(task, imageRequest);
var answer = result.EnsureSuccess();
var helmet = answer.Traits["helmet"];
var matchingTraits = answer.Traits
    .Where(x => x.Answer.Probability >= 0.8)
    .OrderByDescending(x => x.Answer.Probability)
    .Select(x => x.Key)
    .ToArray();

if(answer.Traits.TryGetValue("shield", out var shield))
    Console.WriteLine(shield.Probability);
```

`AskEach` accepts an `IReadOnlyDictionary<string, string>` of identifiers and instructions.
Identifiers and instructions must be nonblank. Identifiers use ordinal, case-sensitive
matching and may repeat across different collection properties. The task copies the
questions and preserves their enumeration order; response order does not affect result order.
An empty dictionary produces an empty result collection and adds no provider questions.

Choice collections infer bool or enum options. Supply `choices` for explicit values or
`DecisionOption<T>` descriptions, as with `Ask`. Score collections require `levels`.
All entries in a collection share the supplied choices or rubric. Collections enumerate
`DecisionEntry<TAnswer>` values with `Key` and `Answer`, supporting standard LINQ.
They expose `Count`, an indexer, and `TryGetValue`, and cannot be modified.

All fixed and dynamic questions share the same evidence and request. Missing, duplicate,
unexpected, or malformed answers fail the complete result; any refusal also fails the
complete result. Internal provider question names are generated by the task and are
separate from application identifiers.
