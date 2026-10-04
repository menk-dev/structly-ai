# Structly.AI design and behavior contract

Phase 1 decision record, updated for phase 5 on 2026-10-04. The runtime capability
set F01–F16 is implemented, including advanced responses, caching, embeddings and images.
Analyzer feedback (F17) remains assigned to phase 6.
Initialization was verified: empty .NET 10 library, offline xUnit
project, locked dependencies and CI/release scaffolding; both references present,
ignored and outside the solution. No previous phase handoff exists.

## Release scope

The owner's clarified requirement is functional coverage of **at least the union of
ref1 and ref2**, with no requirement to preserve their names, style or architecture.
This supersedes the plan's default deferral of embeddings, image generation and
analyzers. All reference capabilities below are initial-release requirements, delivered
over phases 2–6. Structured output remains the first implemented path, not the release
boundary. Do not mark release readiness complete with a missing reference capability.

OpenAI is the first provider. Target `net10.0` for runtime code, matching both references.
Keep one runtime package `Structly.AI`; include the analyzer assembly under
`analyzers/dotnet/cs` rather than adding a second consumer package. Analyzer targets
`netstandard2.0` for compiler-host compatibility. Additional providers/frameworks,
tool calling, image editing, audio/video/file input, automatic retries and Native AOT
are deferred because neither reference implements them. Do not promise source/binary
compatibility, preservation of bugs, universal model support or provider deduplication.

### Feature coverage and acceptance map

| ID | Reference capability | New behavior, implementation phase, acceptance |
| --- | --- | --- |
| F01 | ref1 reusable sessions/type registration/startup validation | Immutable `StructuredTask<T>`; no mutable registration/history. Phase 2: startup diagnostics; 4: safe concurrent reuse. |
| F02 | Both strict schema, DTOs/nullability/enums/collections/names/descriptions | Shared resolved serialization contract. Phase 2: matrix below, deterministic closed schemas and local output checks. |
| F03 | ref2 schema metadata/string/number/array constraints | Standard description/naming attributes plus constraint attributes. Phase 2: valid bounds/pattern/format on correct shapes; phase 3: wire constraints; 5: guidance agrees. |
| F04 | ref2 runtime enums and vocabulary-specific schema caching | Request-scoped vocabularies and immutable task base contract; no global or vocabulary cache. Phase 2: isolation and validation; 5: nullable collection/concurrent interactions. Reuse performance without unbounded retained schemas. |
| F05 | ref2 generated field list/example JSON/additional instructions | Optional output specification derived from same resolved contract, public inspection. Phase 5: sample passes local validation, constraints/vocabularies/names match, switches work. |
| F06 | Both typed output; ref1 free text | Typed result and separate `GenerateTextAsync`. Phases 3/5: typed success and text without response schema, same failures/metadata. |
| F07 | ref2 text/image parts, message histories/roles | Ordered typed messages, text and HTTPS/data-URL images with auto/low/high detail. Phase 5: exact wire roles/order, malformed input suppressed, no image download by library. |
| F08 | Both continuation; ref2 Store control | Explicit previous response ID and storage; no local session state. Phase 5: ID passed, storage policy explicit, instructions resent, changed schema supported. |
| F09 | ref1 preferences/reasoning; ref2 direct model ID | Explicit IDs plus consumer-configured named profiles, per-request override, reasoning low/medium/high. Phase 3: deterministic selection; 5: profile coverage for all operation kinds. |
| F10 | ref1 request auth/static/env/session override | Async credential resolver, static/env factories, request > task > client, no fallback. Phase 3/4: reread environment per call and concurrent credential isolation. |
| F11 | ref1 categorized failures/correlation/metadata/idempotency passthrough | One result contract, caller cancellation exception, provider settings including metadata/header. Phase 3/4: taxonomy, precedence, hints, no retry/deduplication guarantee. |
| F12 | ref1 streaming output/reasoning-summary progress and idle timeout | Optional SSE, bounded progress callbacks, summary indexes, total/inactivity deadlines. Phase 4: active keep-alives survive idle window, stalls end, terminal usage retained, cleanup on every exit. |
| F13 | Both usage/model/IDs; ref2 billed failure observer/raw response | Usage retained before output processing, bounded observer, opt-in raw envelope/text. Phase 3/4: refusal/incomplete/parse failure usage, observer failure cannot mask outcome. |
| F14 | ref2 key/cache mode/TTL/prewarm/breakpoints/comparison diagnostics/cache-write usage | Explicit OpenAI cache settings and dedicated prewarm operation. Phase 5: exact fields, invalid combinations rejected, unknown diagnostic strings retained, no cache-hit promise. |
| F15 | ref1 embeddings/batching/dimensions/preferences/order | `EmbedAsync` with shared auth/deadline/result policy. Phase 5: reorder by index, complete finite vectors, dimension option, usage and errors. |
| F16 | ref1 images/count/size/custom dimensions/quality/background/format/base64 | `GenerateImagesAsync`, typed options and bytes/media type. Phase 5: presets/custom override, transparent JPEG suppressed, count/order/format/usage/errors. |
| F17 | ref1 Roslyn schema diagnostics and replacement guidance | Analyzer for task/schema call sites using settled rules. Phase 6: invalid shapes/names/attributes/limits diagnosed, valid DTO accepted, legacy attribute guidance, packed consumer sees diagnostics. |

Cache implementation choices, registration and interfaces are not themselves functional
requirements. Their observable benefits (reuse, early validation, isolation, configurable
calls) are preserved. Caller-owned DI can register the concrete client; no dependency
on an options/DI/cache framework is needed. Phase 5 and phase 6 must audit F01–F17
against the reference code/tests before completion, including any overlooked behavior.

## Reference assessment and failure modes

Evidence inspected: ref1 session, schema validator/builder, OpenAI provider/options,
request/result/progress/auth/embedding/image models and analyzer; ref2 client, schema
generator, dynamic enums, output specification, models/attributes; both READMEs and
schema/client/session/idle/analyzer tests. Paths below are relative to ignored reference roots.

| Evidence | Strength | Correction in the new design |
| --- | --- | --- |
| ref1 `Sessions/StructuredLlmSessionBase.cs`, session tests | Preflight and reusable task configuration | Mutable HashSet registration creates ordering/concurrency requirements; validate immutable typed tasks directly. |
| ref1 `Schema/StructuredLlmSchemaValidator.cs`, validator/builder tests | Actionable paths, shape/limit rejection | Validator and builder duplicate discovery; broad enumerable acceptance does not prove deserialization. One contract and explicit collection rules. |
| ref1 `OpenAiStructuredLlmSchemaBuilder.cs` | Standard attributes, element nullability | Nullable enum adds null to type but not enum, still excluding null. Wrap complete enum in a separate nullable branch. |
| ref2 `JsonSchemaGenerator.cs`, generator tests | Dynamic vocabularies, concurrent cache misses, constraints | Item nullability defaults to NotNull; enum names ignore wire renames; any JsonIgnore excludes property. Honor serialization and nested annotations exactly. |
| ref2 `DynamicEnums.cs`, cache tests | Vocabulary identity avoids collisions | Long-lived caches retain unbounded vocabularies; schema key only covers naming policy/description/type/values, not full serializer contract. Retain base contract per task only. |
| ref2 `LlmOutputSpecificationGenerator.cs`, specification tests | Human-readable fields and examples | Separate reflection traversal can drift; samples may violate constraints. Generate from resolved contract and validate sample before use. |
| ref1 `OpenAiStructuredLlmSession.cs`, provider and idle-timeout tests | Per-request auth, stream cleanup, idle versus total timeout | Typed processing outside HTTP timer, caller cancellation marked retryable, progress mode coupled to callback. Bound entire operation and make streaming explicit. |
| ref2 `OpenAiStructuredLlmClient.cs`, client tests | Usage captured before refusal/parse failure | Observer can hang/mask failure; parser ignores incomplete status and selects first text part; HTTP errors expose raw body. Bound callbacks, validate envelope/status, sanitize errors. |
| Both provider code/tests and ref1 analyzer | Offline handler fixtures, compile-time feedback | Model/cache/image constraints drift between comments/code; analyzer limits duplicate runtime assumptions. Verify official sources and test semantic parity. |

Tests demonstrate reference behavior, not official provider guarantees. Do not copy their
public surface or combine ref1 result errors with ref2 exception-only defaults arbitrarily.

## Proposed API and lifecycle

Runtime contracts in `Structly.AI`; OpenAI client/settings in `Structly.AI.OpenAI`;
embedding/image contracts in their feature namespaces. Use sealed concrete types;
records with more than two unrelated members use explicit properties.

```csharp
using System.ComponentModel;
using Structly.AI;
using Structly.AI.OpenAI;

public sealed record Ticket
{
    [Description("Short description of the reported problem.")]
    public required string Summary { get; init; }

    [DynamicVocabulary("queues")]
    public required string Queue { get; init; }

    public string? Reference { get; init; }
}

// Startup validation, independent of credentials/HTTP. Reuse concurrently.
var task = StructuredTask.Create<Ticket>(new StructuredTaskOptions
{
    SchemaName = "ticket",
    Instructions = "Extract a ticket and select its queue."
});
using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
var client = new OpenAiClient(http, new OpenAiClientOptions
{
    DefaultModel = new ModelSelection { ModelId = configuredModel },
    CredentialResolver = Credentials.FromEnvironment("OPENAI_API_KEY")
});
var request = new StructuredRequest
{
    Input = "Invoice INV-42 was charged twice.",
    Vocabularies = new Dictionary<string, IReadOnlyList<string>>
    {
        ["queues"] = ["billing", "technical"]
    },
    UsageObserver = (usage, token) => RecordUsageAsync(usage, token)
};
var result = await client.ExecuteAsync(task, request, cancellationToken);
if (result.IsSuccess)
{
    SaveTicket(result.Value!);
}
else
{
    RecordFailure(result.Error!.Kind, result.Metadata, result.Warnings);
}
// result.Metadata.Usage also retains counts; deduplicate observer/result accounting
// with Metadata.ExecutionId if consuming both.
```

Host functions, configured model and cancellation token are illustrative. No model ID
is chosen by the library. Static and environment credential factories require explicit
selection; normal tests never read ambient secrets.

| Public contract | Policy |
| --- | --- |
| `StructuredTask.Create<T>(StructuredTaskOptions)` | Immutable `StructuredTask<T>`, no class constraint (DTO structs allowed). Throws `StructuredSchemaException` for static schema issues. No registration, history, transport ownership or disposal. |
| `StructuredTaskOptions` | Required nonblank Instructions; optional SchemaName (`[A-Za-z0-9_-]{1,64}`), Description, credential resolver, ModelSelection, SerializationProfile. Name resolves explicit override > SchemaAttribute.Name > CLR type name (invalid characters replaced with underscore, truncated to 64). Blank explicit names invalid. |
| `StructuredTask<T>` | Read-only settings; `CreateSchema(vocabularies = null)` returns detached JsonElement or schema exception; `CreateOutputSpecification(options, vocabularies = null)` returns guidance or schema exception. Dictionary type as in example. |
| `StructuredRequest` | Required Input unless Messages supplied; optional Messages, Vocabularies, ModelSelection, MaxOutputTokens, TotalTimeout, InactivityTimeout, Stream (false), Progress, UsageObserver, CredentialResolver, CorrelationId, OutputSpecification, OpenAi settings. Init-only properties. |
| `ModelSelection` | Exactly one explicit ModelId or ProfileName, optional reasoning effort. Request replaces task selection, task replaces client default. Omitted effort is inherited only with omitted selection; selected profile may define effort. No automatic ranking/fallback. |
| `OpenAiClientOptions` | Required DefaultModel; optional default resolver, profiles by operation kind/name, BaseAddress (default https://api.openai.com/v1/), TotalTimeout (120s), default InactivityTimeout (null). Snapshot at construction. |
| `OpenAiClient(HttpClient, OpenAiClientOptions)` | Caller owns transport, headers/timeout/base address never mutated, client not disposed. Own/dispose request/response streams. No public transport interface for tests. Resolve relative endpoint against snapshotted options. |
| Execution | `Task<StructuredResult<T>> ExecuteAsync<T>(StructuredTask<T>, StructuredRequest, CancellationToken = default)`; same result family for text, embeddings, images, prewarm. Internal awaits use ConfigureAwait(false). |
| `StructuredResult<T>` | IsSuccess, Value, Error, Metadata, immutable Warnings. Success has Value and no Error, failure no usable Value and one Error; use IsSuccess for value types. `EnsureSuccess()` returns T or throws StructuredOperationException retaining Error/Metadata. |
| `StructuredError` | Kind, safe Message, IsTransient, nullable HttpStatusCode/RetryAfter, immutable Issues. No raw body or credential. |
| `StructuredIssue` / `StructuredSchemaException` | Path/Code/Message; exception derives ArgumentException and exposes Issues. Paths use serialized member names and `[]`. |
| `StructuredMetadata` | ExecutionId (Guid), Operation, CorrelationId, Provider, RequestedModel, ResolvedModel, ResponseId, ProviderRequestId, Usage, CacheDiagnostics; optional OutputText/RawResponse only on explicit capture. |
| `StructuredUsage` | Nullable nonnegative long InputTokens, OutputTokens, TotalTokens, CachedInputTokens, CacheWriteTokens, ReasoningTokens; image text/image token breakdowns as optional fields. Never infer missing zero/total/cost. |
| Usage delegate | `Func<StructuredUsageEvent, CancellationToken, ValueTask>`; event Metadata, Succeeded, FailureKind (nullable), CallerCancelled. Request observer overrides optional client observer; no duplicate delivery. |
| Progress delegate | `Func<StructuredProgress, CancellationToken, ValueTask>`; Kind Started/OutputTextDelta/ReasoningSummaryDelta/Completed, optional TextDelta, OutputIndex, SummaryIndex, response/model IDs. Request-scoped only. |
| `StructuredWarning` | Code/safe Message; observer failed/timed out/skipped, progress observer failure, invalid usage metadata. |
| Cancellation | `StructuredOperationCanceledException : OperationCanceledException` exposes Metadata/Warnings and original caller token, for usage retained on canceled calls. |
| Credentials | Resolver `Func<CancellationToken, ValueTask<string?>>`; factories FromStatic(string), FromEnvironment(string). Request > task > client; selected resolver never falls back. |

Nullable Value does not indicate failure; IsSuccess does. Collections must not be mutable
through downcasts. Snapshot lists/dictionaries/options at construction/entry; caller must
not mutate during snapshot. DTO constructors/setters and delegates are host code.

### Failure, cancellation and advanced examples

```csharp
public sealed record InvalidTicket
{
    public required Dictionary<string, string> Data { get; init; }
}
try
{
    StructuredTask.Create<InvalidTicket>(new StructuredTaskOptions
    {
        SchemaName = "invalid_ticket", Instructions = "Extract a ticket."
    });
}
catch (StructuredSchemaException exception)
{
    ReportSchemaIssues(exception.Issues); // $.data: dictionaries unsupported
}

var failed = await client.ExecuteAsync(task, request, cancellationToken);
if (failed.Error?.Kind == StructuredErrorKind.RateLimited)
{
    ScheduleRetry(failed.Error.RetryAfter); // Host owns attempts/job budget.
}
// Refusal/invalid output may still have failed.Metadata.Usage and observer event.

using var cancelled = new CancellationTokenSource();
cancelled.Cancel();
try { await client.ExecuteAsync(task, request, cancelled.Token); }
catch (StructuredOperationCanceledException exception)
{
    RecordCancellation(exception.Metadata); // Pre-cancel sends nothing.
}

var followUp = await client.ExecuteAsync(task, request with
{
    Input = "Here is the missing invoice detail.",
    Stream = true,
    InactivityTimeout = TimeSpan.FromSeconds(30),
    Progress = (item, token) => DisplayProgressAsync(item, token),
    OpenAi = new OpenAiResponseOptions
    {
        Store = true,
        PreviousResponseId = storedResponseId // Prior call explicitly used Store=true.
    }
}, cancellationToken);
```

## Behavior contract

Null method arguments throw ArgumentNullException. Invalid client/task options throw
ArgumentException (schema issues use its specialized subtype). Invalid per-call values
produce results so unattended jobs can classify them without exception parsing.

Execution order: execution ID/start total budget; caller cancellation; snapshot/validate
request; resolve schema/vocabularies; select model; resolve credential; send/read response;
capture metadata/usage; classify status/content; validate and deserialize; notify observer;
finalize result/cancellation. No auth resolution or HTTP for local validation failures.

| Outcome | Error kind / action | IsTransient |
| --- | --- | --- |
| Blank input/model, ambiguous input, invalid timeout/cap/options | InvalidRequest, issues, no HTTP | false |
| Runtime vocabulary or schema limit violation | UnsupportedSchema, issues, no HTTP | false |
| Missing/empty selected credential or resolver failure | Authentication, safe message, no fallback/HTTP | false |
| HTTP 401 / 403 | Authentication / PermissionDenied | false |
| HTTP 429 | RateLimited; Retry-After delta seconds/date if valid | true, except insufficient_quota false |
| HTTP 408 / 500–599 | ProviderUnavailable | true |
| Other non-success HTTP | ProviderRejected | false |
| HTTP/read/DNS/TLS failure | TransportFailure | true |
| Operation cancellation without library deadline/caller cancel | TransportFailure (including HttpClient.Timeout) | true |
| Caller cancellation | Throw StructuredOperationCanceledException with captured metadata | Host decides replay |
| Total / streaming inactivity deadline | DeadlineExceeded / InactivityExceeded | true |
| Refusal | Refused, no DTO, preserve usage | false |
| Incomplete output (cap/filter/other reason) | IncompleteOutput, no partial DTO | false |
| Provider status failed | ProviderUnavailable for known transient code, otherwise ProviderRejected | by code, unknown false |
| Malformed envelope/unknown or nonterminal status/missing output | InvalidResponse | false |
| Schema-incompatible JSON/deserialization failure | InvalidOutput, issues, no partial DTO | false |
| Observer exception/timeout | Warning, retain primary outcome | unchanged |

Caller cancellation wins over total deadline, total over inactivity, inactivity over
provider/output outcome if signaled before finalization. Check at async and bounded
synchronous-work boundaries; after completion cancellation is not retroactive.
Total includes validation, credentials, transport/body, typed processing and callback
waiting. Use monotonic elapsed time/internal TimeProvider injection in tests. Positive
finite TotalTimeout max 24h; per-request override replaces client timeout. Inactivity
positive finite, supported only with Stream=true; otherwise InvalidRequest. Streaming
starts inactivity at response-body read, resets on each complete SSE line including
keep-alives; total remains active while lines arrive. Progress requires Stream=true;
streaming without callback is allowed. No incomplete DTO published as final success.

Stop waiting at deadlines even for non-cooperative async transport/resolver/callback;
observe late faults and dispose late responses. Synchronous host callbacks/constructors
cannot be preempted and must return promptly. Recommend HttpClient.Timeout infinite,
never mutate it. Host transport must disable redirects (auth isolation) and automatic
retries if one HTTP attempt is required. Library performs one attempt, parses RetryAfter
with date against TimeProvider (past -> zero), owns no retry loop. IsTransient is a hint,
not a guarantee replay is safe/free. IdempotencyKey is optional OpenAI header passthrough;
no local replay store or deduplication guarantee. CorrelationId stays local; explicit
provider Metadata dictionary is separate and validated against endpoint limits.

Usage is parsed before status/refusal/typed processing, preserving independent valid
counts/IDs/reported model even on failure. Missing count remains null; malformed count
adds warning without discarding other valid counts or the answer. Truncated/malformed
envelopes may leave billing unknown. Never promise to observe every billed request.
Notify usage once if any count is valid; no event if omitted. Event outcome is computed
before observer (later cancellation can affect final outcome). Token independent of
caller cancellation, bounded by min(5s, remaining total budget); skip if no budget.
Observe late faults, never retry delivery. Observer exception/cancel yields safe warning;
observer-only timeout warns, total timeout yields DeadlineExceeded unless caller wins.
Progress callbacks run in order, bounded by min(1s, remaining total budget); failure or
timeout warns and disables further progress callbacks while stream reading continues.
No progress callback can silently change retry policy or mask provider outcome.
Completed progress occurs only after terminal output validation. Usage delivery remains
independent of progress failure. Reliable accounting uses result/cancellation metadata,
with ExecutionId to avoid duplicates; delegates are best effort.

No automatic prompt/body/auth logging. Raw capture opt-in on OpenAiResponseOptions;
clone JsonElement so lifetime survives disposal, include on billed failures too. Raw
data/output/refusal/summary deltas are sensitive and host-owned; sanitized Error/Warnings
never include provider body, auth/transport exception text or observer exception text.

## Schema support and serialization (phase 2)

SerializationProfile is immutable: naming CamelCase (default) or Preserve, strict
case-sensitive names/numbers, string enum names with integer values disabled. Explicit
JsonPropertyName overrides naming. Do not accept arbitrary converters/type resolvers
that schema generation cannot understand. Resolve names/members/nullability/constraints
once; use that contract for schema, sample/guidance and output validation/deserialization.

| Shape/attribute | Rule |
| --- | --- |
| Root | Concrete object DTO (class/record/struct), at least one included property; primitive/collection/interface/abstract/object roots rejected. |
| Properties/construction | Public instance readable properties with public setter/init, or constructor-bound get-only properties supported by System.Text.Json. Resolve unique supported constructor (JsonConstructor, public parameterless, otherwise single public parameterized), every constructor parameter bound to compatible property; reject computed get-only/indexers/ambiguous constructors/private setters. Positional records allowed. |
| Inheritance | Concrete inherited unique properties allowed; hidden/duplicate serialized names or polymorphic members rejected. |
| string/bool | JSON string/boolean. |
| byte/sbyte/short/ushort/int/uint/long/ulong | Integer; validate integrality/CLR range. |
| float/double/decimal | Number; finite representable value, no numeric strings or overflow. |
| Guid/DateOnly/DateTime/DateTimeOffset | String, uuid/date/date-time format respectively; validate using matching System.Text.Json conversion. |
| Nullable value/reference/property/item | Separate anyOf null branch around entire schema, including enum/constraints. Propagate NullabilityInfo recursively. MaybeNull/AllowNull widen member nullability; unknown reference annotations treated nullable and documented. Non-null root. |
| Presence | Every included key required, whether C# required/JsonRequired or nullable. Missing nullable key invalid. |
| Enum | Declared names or JsonStringEnumMemberName, exact ordinal names, no integer values. Reject flags, empty enum, numeric aliases, blank/duplicate wire names. |
| Collections | One-dimensional arrays except byte[], List, IReadOnlyList, IList, ICollection, IEnumerable, IReadOnlyCollection and HashSet/ISet of supported elements when System.Text.Json can materialize. Array/list order preserved, set duplicates rejected locally. Nested nullable collections/items supported. Custom enumerable supported only if standard serializer can construct/populate uniquely; otherwise diagnostic. |
| Unsupported | Dictionary, multidimensional/non-generic/async enumerable, cycles, object/dynamic/JsonElement/JsonNode, abstract/interface DTO, tuples/delegates/open generic, char/TimeSpan/TimeOnly/Uri, byte[] (serializer base64 mismatch). Repeated sibling DTO is not recursion. |
| JsonPropertyName / JsonPropertyOrder | Exact nonblank unique name; order ascending then ordinal serialized name for deterministic ties. |
| JsonIgnore | Always excludes; Never includes. Conditional writing/direction-specific conditions rejected for strict symmetric required contract. Exclude ignored member before validating attributes. |
| Description | DescriptionAttribute type/property; SchemaAttribute optional Name/Description; task metadata overrides type metadata. Blank supplied metadata invalid. |
| Converter/extension/include/polymorphism/number-handling/populate attributes | Reject incompatible serialization overrides with path; fields excluded unless inclusion requested (unsupported). JsonRequired allowed, redundant. |
| DynamicVocabulary(setName) | String or supported string collection only; constrains items, nullable independently. |
| StringConstraint | MinLength/MaxLength nonnegative int, or -1 for unspecified; Pattern, Format. Supported formats date-time/time/date/duration/email/hostname/ipv4/ipv6/uuid; schema and local checks agree. Invalid pattern/format/bounds/type rejected. |
| NumberConstraint | Optional finite double Minimum/Maximum (NaN means unspecified); inclusive, min <= max, numeric members only. Compare original JSON numbers against the emitted round-trip decimal representation without CLR rounding. |
| CollectionConstraint | Nonnegative MinItems/MaxItems, or -1 for unspecified; min <= max, collections only. |

Constraint attribute correction in phase 2: nullable int and decimal properties cannot
be used as C# attribute named arguments. Use -1 for omitted lengths/cardinalities and
Double.NaN for omitted numeric bounds. Supplied numeric bounds use finite doubles,
matching emitted JSON numbers; DTO decimal values remain supported.

Constraint strings count Unicode scalar length. Pattern uses JSON Schema search
semantics with a deliberately small portable ASCII ECMAScript subset: literals,
character classes, alternation, quantifiers and literal punctuation escapes. Groups,
backreferences, shorthand/Unicode escape classes and class subtraction are rejected;
patterns are nonblank and at most 4,096 UTF-16 characters. Regex execution is bounded
(100ms), timeout invalid output. Format validators
must have explicit positive/negative fixtures; no arbitrary format accepted as no-op.
DataAnnotations are not implicitly interpreted; use these constraint attributes or
host business validation. Fine-tuned/model-specific unsupported constraints yield a
provider rejection; never silently weaken a contract for a model. Runtime/analyzer
diagnostics explain unsupported shapes and path, not a vague serialization exception.

Vocabularies: referenced sets required/nonempty, null/blank/ordinal duplicate values
rejected, unreferenced sets ignored as in ref2. Snapshot, ordinal-sort values; never trim
or case-fold. Same resolved values drive schema/sample/output checks. No global cache;
task retains immutable base contract, per-call vocabularies/schema discarded after call.

Generate inline schemas, closed object nodes, all required, nullable anyOf below root.
OpenAI preflight ceilings (source below): 5,000 total object properties; 10 nesting
levels; 120,000 relevant string characters; 1,000 enum entries across schema; >250
string enum entries limited to 15,000 combined characters for that enum. Count emitted
occurrences including repeated DTOs, not distinct CLR types. Conservatively count
object/array nodes along path starting root=1; count decoded Unicode scalar names/
enum/const/definition characters, exclude descriptions from that provider ceiling.
Bound serialized schema to 1 MiB; structured/text envelope 16 MiB, image envelope
128 MiB (client-configurable positive limits). Enforce incrementally before unbounded
allocation; oversize schema UnsupportedSchema, oversize response InvalidResponse.

Validate output JSON before deserialization: duplicate keys, missing/additional keys,
token types/nulls, enum/vocabulary, numeric bounds, constraints recursively. Deserializer
alone accepts missing/null DTO values. No JSON repair/code-fence stripping/coercion.
Deserializer/host DTO construction failures become safe InvalidOutput; catastrophic
runtime exceptions not swallowed. Detached JsonElement schema inspection cannot mutate task.

### Implemented local API (phase 2)

`ReadOutput` exposes the local validation/deserialization path for offline use and for
the provider path to reuse. Invalid JSON/typed values produce InvalidOutput, invalid
vocabularies produce UnsupportedSchema, and null arguments throw. Optional supplied
metadata is retained by identity, including usage on output-processing failures.
Output diagnostics are bounded to 100 issues; unknown JSON keys are reported at their
containing object without reproducing untrusted key text. Host constructor/setter errors
are sanitized; host cancellation and catastrophic exceptions propagate.

```csharp
var task = StructuredTask.Create<Ticket>(new StructuredTaskOptions
{
    Instructions = "Extract a ticket.",
    SchemaName = "ticket"
});
IReadOnlyDictionary<string, IReadOnlyList<string>> choices =
    new Dictionary<string, IReadOnlyList<string>> { ["queues"] = ["billing", "technical"] };
var schema = task.CreateSchema(choices); // Detached JsonElement, no HTTP.
var result = task.ReadOutput(
    """{"summary":"Duplicate charge","queue":"billing","reference":null}""",
    choices);
Ticket ticket = result.EnsureSuccess();
```

Task construction validates static shape/constraints and strict-schema limits without
requiring runtime vocabularies; CreateSchema/ReadOutput require every referenced set.
SerializationProfile uses PropertyNaming.CamelCase (default) or Preserve, without
configurable converters/resolvers. Get-only DTO properties must bind to the selected
JSON constructor; hidden members and private setters are rejected. Overrides of the
same virtual property are resolved once. Unknown reference annotations are nullable;
MaybeNull/AllowNull widen reference members, without changing nonnullable value types.
The internal serializer clears IsRequired metadata because presence is already checked
for every key; this supports JsonRequired on constructor-bound get-only properties,
which the default serializer otherwise rejects despite valid constructor binding.

Custom enumerable support is deliberately explicit: a unique IEnumerable<T>,
ICollection<T>, public parameterless constructor and serializer CreateObject support.
The host owns constructor/Add/setter behavior; failures during actual materialization
become InvalidOutput. Get-only enumerable wrappers that cannot be populated are rejected.
Sets reject duplicates using deserialized element equality (matching HashSet/ISet),
which can invoke host DTO constructors/equality before final DTO construction.
No unsupported uniqueItems keyword is emitted; set uniqueness and CLR scalar ranges
are additional local checks. Integer lexical forms must be accepted by the default
System.Text.Json integer converter, so fractional/exponent integer spellings are not
coerced. Floating-point and decimal conversion otherwise follows System.Text.Json;
nonfinite/overflow values are rejected and bounds check the unrounded JSON number.

Formats use explicit string checks: exact D UUID, calendar date, timestamp/time with
required offset, ordered ISO duration components (including week-only durations),
ASCII mailbox syntax without display names, DNS hostname labels, strict dotted IPv4
and unscoped IPv6. CLR Guid/date/time properties additionally must deserialize using
their standard converter (for example DateTimeOffset's offset/range restrictions).
All nine formats have positive and negative fixtures; no unknown format is accepted.

The common schema boundary currently applies OpenAI strict ceilings to every task,
since OpenAI is the only approved provider. Property/static-enum/string/depth budgets
are checked while resolving/emitting, counting sibling repetitions. Dynamic entry counts
are checked before allocating vocabulary arrays, values are checked as copied, and a capped writer enforces the
1 MiB serialized limit including descriptions/escaping. There is no shared or
vocabulary cache; immutable task contracts retain neither caller dictionaries nor values.
Per-call input snapshots require the caller not to mutate while the snapshot is running.

Task model/credential settings and provider request validation remain phase 3;
usage callbacks, progress, cancellation exception and deadlines remain phase 4;
output specifications and cache diagnostics remain phase 5. Their table entries above
are still the approved contract, not claims of implementation in this phase.

## Advanced runtime and analyzer policies

**Inputs:** Input string shorthand becomes one user text message; exactly one of Input
or nonempty Messages. MessageRole enum System/Developer/User/Assistant, nonempty parts;
text nonblank, ImagePart absolute HTTPS or valid PNG/JPEG/WebP/GIF data URL and detail
Auto/Low/High. Images permitted in user messages only; assistant text maps to output_text,
other text to input_text. Do not fetch URLs/upload files. Reject unsupported roles/detail,
empty content and malformed URLs before HTTP. Explicit BaseAddress enables compatible
gateways with the same wire contract, not an assertion of Azure/other-provider support.

**Output specification:** options IncludeFields/IncludeExample default true, optional
AdditionalInstructions. Append to final user message after existing content, leaving
earlier messages intact; fail if no user target. Public CreateOutputSpecification uses
the same resolved schema. Generate deterministic schema-valid sample: nullable null,
enum/vocabulary first ordinal value, arrays meet minimum cardinality, constrained scalar
within bounds. If regex/format/combined constraints cannot produce an example reliably,
require caller-supplied ExampleJson or IncludeExample=false; validate supplied example
locally and return actionable InvalidRequest before auth. Never emit invalid example.

**Continuation:** OpenAiResponseOptions Store=false default, PreviousResponseId optional
nonblank; pass through, resend current task instructions/schema each call. Prior ID must
refer to provider-available stored response under same account; host owns IDs/history/
retention. No auto-storage or local sessions; Store=false on current continuation prevents
its future persistence, not use of prior stored ID. Changed response type/vocabulary allowed,
new response validated against current contract, no assumed prompt-cache reuse.

**Provider settings:** OpenAiResponseOptions also supports provider Metadata, IdempotencyKey,
CaptureRawResponse/CaptureOutputText (false), PromptCacheKey and Cache options.
StructuredRequest.IncludeReasoningSummary (false) controls summaries. Reasoning
low/medium/high and summary are explicitly sent only when chosen;
unsupported model combinations rejected by provider, never dropped. Profiles map named
choices to model ID/effort per operation; missing profile InvalidRequest. Profile names
Frontier/Balanced/Fast/Tiny can reproduce ref1, without hard-coded aging model IDs.

**Streaming:** Stream=true sends SSE; terminal completed/incomplete/failed event mandatory.
EOF before terminal InvalidResponse, no success from deltas alone. Parse multiline events,
comments/keep-alives and split chunks; ignore documented ancillary events, unknown event
types safely ignored unless terminal cannot be recognized. Malformed event InvalidResponse.
Process terminal envelope through same metadata/usage/output path as non-streaming. Progress
includes output deltas and opted-in reasoning summaries (OutputIndex/SummaryIndex distinguish
parts); never raw chain-of-thought. Size limits and deadline cleanup apply throughout.

**Cache:** OpenAiCacheOptions Mode Implicit/Explicit, Ttl (optional "30m"), comparison
response ID; ContentPart.CacheBreakpoint marks provider breakpoint. Explicit mode needs
at least one eligible input text/image breakpoint, maximum four explicit markers per call;
implicit mode permits at most three explicit markers, reserving the fourth write slot
for the implicit boundary; do not mark top-level Instructions or assistant output.
Breakpoints allowed also in implicit mode when documented; preserve exact placement.
Use prompt_cache_options on supported model family; older retention InMemory/24Hours
is a separate mutually exclusive setting. Require explicit cache compatibility profile
Modern/Legacy configured per model; unknown advanced capability rejects locally rather
than guessing from ID. Phase 5 re-verifies current limits/model support. CacheDiagnostics
preserve Type/Reason as strings (including unknown values), comparison reusable/missed
counts. Key/TTL/same prefix never guarantee hit; no pricing baked into library.
`PrewarmAsync(OpenAiPrewarmRequest, CancellationToken)` reuses input/schema/cache settings,
sends provider prewarm flag, returns metadata-only result and usage, never expects a DTO.
Reject output generation/progress/continuation combinations on prewarm; no typed request
option that silently returns no value. Cross-schema changes and comparisons tested.

**Text:** `GenerateTextAsync(TextRequest, CancellationToken)` returns StructuredResult<string>,
same input/settings/usage/failure/progress policy, no text.format/schema. Nonblank output
required, refusal/incomplete remain failures; instructions optional on TextRequest.

**Embeddings:** EmbedAsync(EmbeddingRequest, CancellationToken) returns
StructuredResult<IReadOnlyList<IReadOnlyList<float>>>. Required nonempty text Inputs,
explicit/profile model selection, optional positive Dimensions; common timeout/auth/
correlation/idempotency/observer settings. Send encoding_format=float. Validate unique
complete indexes, same nonempty vector length (requested dimensions if set), finite
components; return in input order. No streaming/continuation/cache options.

**Images:** GenerateImagesAsync(ImageGenerationRequest, CancellationToken) returns
StructuredResult<IReadOnlyList<GeneratedImage>>. Required Prompt, Count 1–10, explicit/profile
model selection, Size Auto/Square/Portrait/Landscape/Wide -> auto/1024x1024/1024x1536/
1536x1024/1536x864, optional positive CustomDimensions override, Quality Auto/Low/Medium/High,
Background Auto/Opaque/Transparent, Format Png/Jpeg/WebP default Png. Shared execution options.
Transparent JPEG locally invalid; pass model-specific positive dimensions to provider for
validation (no universal edge limits). Nonempty valid base64 per image, requested count,
matching returned format/media type; GeneratedImage exposes Base64Data, MediaType, ToBytes().
Validate decoding during response processing; failure retains usage. No URL downloading.

**Analyzer:** build after runtime rules settle. Compile-time errors for unsupported DTOs,
cycles, names/enums/descriptions/constraint applicability/static schema limits at generic
Create/inspection/Execute call sites when T concrete. Runtime validates unknown generic
types and vocabularies. Standard camelCase semantics, nullable/constructor rules must
agree with runtime tests. Legacy ref1 removed attribute names receive replacement guidance;
mapping now points to standard naming/ignore/nullability and new constraint attributes.
No reference namespace APIs required. Skip generated code, concurrent analysis enabled;
no runtime network/schema cache or Roslyn dependency in runtime package dependencies.

## Official provider evidence and assumptions

Official OpenAI documentation opened and checked 2026-10-04:

- [Structured Outputs](https://developers.openai.com/api/docs/guides/structured-outputs)
  supports Responses text.format json_schema/strict, object roots, required closed objects,
  nullable branches and the limits recorded above; refusals/incomplete answers require handling.
  Phase 2 rechecked this guide on 2026-10-04: same ceilings and nine formats; set
  uniqueness stays local because uniqueItems is absent from the supported strict subset.
- [Conversation state](https://developers.openai.com/api/docs/guides/conversation-state)
  documents stateless store=false and previous-response continuation. Storage is explicit
  library policy; local reuse is distinct from provider history.
- [Streaming responses](https://developers.openai.com/api/docs/guides/streaming-responses)
  documents stream=true/SSE; [reasoning](https://developers.openai.com/api/docs/guides/reasoning)
  documents opt-in summary=auto and model-specific reasoning support, not raw reasoning exposure.
- [Prompt caching](https://developers.openai.com/api/docs/guides/prompt-caching)
  distinguishes model generations: modern mode/TTL/breakpoints versus legacy retention,
  cache-write/read counts and model-dependent routing. Reference universal prices/cache-key
  guidance must not become guarantees. Advanced flags and diagnostics rechecked in phase 5.
- [Embeddings](https://developers.openai.com/api/docs/guides/embeddings)
  documents float encoding and optional dimensions for compatible models.
- [Cache diagnostics](https://developers.openai.com/api/docs/guides/prompt-caching/diagnostics)
  documents comparison_response_id and terminal streaming diagnostics, independent of
  conversation loading; records expire and unknown/unavailable results do not imply hits.
- [Image generation](https://developers.openai.com/api/docs/guides/image-generation)
  documents Image API generation, count, base64, dimensions, quality/background/format;
  provider size rules differ from reference README (current edge ceiling can include 3840).
  Do not inherit obsolete model defaults or constraints.

Phase 3 rechecks endpoint reference and versioned fixtures; phase 5 rechecks advanced
model/cache/image fields. Wire structured request: model, instructions, input, text.format
(name/schema/strict=true), explicit store/stream, optional max_output_tokens/reasoning.
Select credential per request header, never default headers. Parse output message content,
not SDK convenience output_text. Concatenate text content in order from one assistant
answer; multiple answer messages/unexpected answer content InvalidResponse. Refusal wins
over text, failed/incomplete status over parseable JSON; capture metadata first. Ignore
ancillary reasoning safely. Unknown envelope fields allowed; no hidden JSON-mode fallback.

## Dependencies, verification and next phases

Runtime: framework HttpClient/System.Text.Json/reflection/TimeProvider, no new production
packages. Analyzer alone needs Microsoft.CodeAnalysis.CSharp PrivateAssets=all; select
current compatible version and lock in phase 6. Keep xUnit v3/Microsoft.Testing.Platform
scaffold and central package/lock policies. Do not run references as project dependencies.

Normal tests deterministic/offline: schema/serialization matrix, golden wire JSON and
synthetic complete/refusal/incomplete/failed/malformed fixtures through fake HTTP handler;
gated streams and controlled TimeProvider for cancellation/deadlines, no timing-sensitive
sleeps or real credentials. Verify invalid schema/request suppresses auth/HTTP; usage on
all billed failure paths; one send on transient failure; no raw data in safe errors;
disposal and concurrent credential/vocabulary/options isolation. Constraints, generated
samples, nullable enum collections, changed-schema continuation, cache prewarm/comparison,
embedding index reorder and image format failures need interaction tests, not only happy paths.
Analyzer tests compile snippets and compare runtime diagnostics, then packed consumer in
phase 6. Live tests opt-in, paid calls separately authorized, never needed for CI success.

Acceptance gates: phase 2 covers F01–F04 local contracts and each supported/unsupported
matrix row; phase 3 minimal typed path F06/F09–F11/F13; phase 4 F12/reliability and
observer precedence; phase 5 completes F05–F16 and interaction audit; phase 6 F17 plus
consumer/package/release readiness. Required contracts may be introduced when their phase
needs them, but policies above cannot be silently changed or deferred to shrink scope.

Publication decisions (phase 6): owner confirmation of package identity/license/initial
version; runtime framework decision net10.0 is settled for implementation. No legal
license selected, no remote settings/publishing/paid calls authorized by phase 1.

## Phase 3 implementation boundary and evidence (historical)

The concrete OpenAiClient now executes nonstreaming structured text requests through
caller-owned HttpClient, using CreateSchema and ReadOutput. StructuredRequest includes
Input, Vocabularies, ModelSelection, credentials, MaxOutputTokens, CorrelationId and
OpenAiResponseOptions. Task-specific selection/credentials are implemented. DefaultModel
is a required ModelSelection; Profiles is an ordinal name-to-explicit-selection dictionary
for structured output, snapshotted at construction. Phase 5 extends profile selection to
other operation kinds. Selection replaces the entire prior selection, including effort;
profile effort applies unless explicitly overridden. Unknown profiles fail before auth.

OpenAiClientOptions currently includes BaseAddress and MaxResponseBytes (16 MiB default,
positive, streaming read enforced even when Content-Length is absent). API directory URI
must be absolute HTTP(S), end with slash, and contain no credentials/query/fragment.
Default transport properties and headers are untouched. OpenAiResponseOptions currently
includes Store (false), IdempotencyKey, Metadata (16 pairs, 64-character keys and
512-character values), CaptureRawResponse and CaptureOutputText. No automatic retries.
Callers must configure redirects/retries themselves and choose models supporting strict
schemas; no embedded model ranking, IDs or model capability list.

At the phase 3 handoff, library deadlines, SSE, observers and metadata-bearing caller
cancellation remained staged for phase 4. They are now implemented as recorded below.
Independent transport cancellation remains TransportFailure. Phase 5 retains messages,
continuation/cache/output guidance and the remaining runtime operations.

Official documentation rechecked 2026-10-04:
[Responses create reference](https://developers.openai.com/api/reference/cli/resources/responses/methods/create)
and [Structured Outputs](https://developers.openai.com/api/docs/guides/structured-outputs).
The typed wire uses text.format json_schema/strict, explicit store/stream and optional
reasoning effort/token cap/metadata. Output comes from assistant message content rather
than convenience output_text; reasoning is ignored. One completed assistant answer is
required, text parts concatenate in order, refusal beats text, and terminal failed or
incomplete status beats output parsing. Unknown/nonterminal envelopes fail safely.
Usage counts are independent, nonnegative long values with malformed counts warned;
cache-write and reasoning details are preserved without inferring missing totals.
Fixtures model the documented envelope with synthetic model IDs and counts; no live
provider access or API-key provisioning is required or performed for this phase.

## Phase 4 implementation boundary and evidence

Phase 4 implements the cancellation, total/inactivity, SSE and bounded-observer contract
above. See [Execution guidance](EXECUTION.md) for concrete limits, warnings, retries and
accounting. OpenAiClientOptions now exposes TotalTimeout (120 seconds), optional default
InactivityTimeout, UsageObserver and TimeProvider (System by default). Making the clock
explicit keeps deterministic offline testing framework-only and allows hosts to control
retry-date/budget time without a transport abstraction. StructuredRequest adds those
per-call settings plus Stream, Progress, UsageObserver and IncludeReasoningSummary.
Reasoning summary opt-in is explicit, independent of observing output progress, and sends
reasoning.summary=auto. It requires streaming. No raw reasoning progress is exposed.

Each execution owns its cancellation sources and metadata. Async waits are abandoned when
the combined operation token fires, with eventual faults observed and late response/stream
ownership cleaned up. Callback tokens link only their own cap and total deadline, allowing
available usage to be observed after caller cancellation. Final precedence is caller > total
> inactivity > provider/output. Expiration during synchronous host code is checked when
control returns; synchronous callbacks and DTO construction cannot be forcibly interrupted.
Progress failure disables subsequent progress but does not disable usage delivery.

SSE is parsed from bounded bytes with strict UTF-8, split chunks, complete-line inactivity,
multiline data and CR/LF/CRLF/BOM/comment support. Terminal event/status must agree;
terminal response goes through the existing metadata/status/refusal/output validator.
No success from deltas/EOF/standalone error events. Unknown ancillary events are ignored;
malformed recognized events fail safely. Stream limits cover consumed SSE bytes, including
keep-alives. Completed progress follows typed validation, including InvalidOutput, but is
not emitted for refusal/incomplete/provider failures. Streaming raw capture is the terminal
envelope rather than an event transcript. Streaming without callbacks is supported.

No automatic retries, logging sink or retry framework was added. Safe categorized results,
immutable warnings and metadata are diagnostics; consumers choose their own logging.
The library makes one HTTP send; handler redirects/retries are host policy. Idempotency
header passthrough never guarantees deduplication or eliminates duplicate billing.
Retry-After dates now use the configured TimeProvider. The reference idle-timeout/provider
code was consulted for cleanup and timer differences; ignored reference roots remain outside
builds and commits. No runtime packages or test dependencies were added.

Official documentation rechecked 2026-10-04:
[Streaming responses](https://developers.openai.com/api/docs/guides/streaming-responses)
for typed SSE lifecycle,
[Streaming event reference](https://developers.openai.com/api/reference/resources/responses/streaming-events)
for terminal and delta event fields, and
[Reasoning summaries](https://developers.openai.com/api/docs/guides/reasoning#reasoning-summaries)
for explicit reasoning.summary=auto and model-dependent support. Existing
phase 3 fixtures remain valid; phase 4 fixtures add synthetic SSE terminal envelopes and
controlled-clock failure/concurrency scenarios. No live calls or credential access occurred.

Phase 5 remains responsible for advanced messages/continuation/cache/output guidance,
free text, embeddings and images. It must reuse these budget/observer/cancellation policies
rather than introducing separate retry loops or unbounded operations.


## Phase 5 implementation boundary and evidence

All F01–F16 runtime capabilities now have implementation, documentation and offline
acceptance coverage. See [Advanced features](ADVANCED.md) for examples and concrete
options. F17 analyzer feedback remains phase 6; no compiler dependencies were added.
The audit revisited ref1's text/embedding/image requests, provider parsing and tests,
and ref2's messages/continuation/cache models, provider tests and specification generator.
Reference assumptions about fallback indexes, fabricated resolved image models, unvalidated
base64 and invalid generated samples were corrected rather than preserved.

TextRequest and OpenAiPrewarmRequest compose StructuredRequest in their Request property;
this shares response controls without duplicating them. TextRequest adds optional
instructions; typed prewarm uses task instructions/model/credentials/schema. Embedding
and image requests live in Structly.AI.Embeddings and Structly.AI.Imaging, expose only
the common execution/auth/accounting controls relevant to those endpoints, and require
explicit ModelSelection. Profiles serves responses/text/prewarm; EmbeddingProfiles and
ImageProfiles allow the same named preference to select different models. Auxiliary
reasoning effort is locally invalid. All maps are snapshotted at client construction.

StructuredRequest.Input is now optional, mutually exclusive with Messages. Its existing
string wire form is retained when no guidance is appended; the provider treats it as one
user message. Ordered messages are copied before credentials, images stay as supplied
URLs/data URLs, and guidance appends to the last user content list. Assistant history
text maps to output_text. Current instructions/schema are resent on continuation, Store
remains false by default, and no local session or automatic provider storage was added.
Changed type and dynamic vocabularies are supported on subsequent or concurrent calls.

CreateOutputSpecification derives human-readable serialized field paths, descriptions,
constraints and the complete strict schema from the same resolved contract. Samples
walk the immutable contract and then pass ReadOutput, including host construction.
Patterns/formats/sets or combined constraints without a reliable sample require ExampleJson
or disabling examples. Generation has per-array/string and total-node/text ceilings to
prevent multiplicative collection expansion; guidance failure is actionable ArgumentException
for inspection or InvalidRequest on execution. IncludeFields/IncludeExample switches and
AdditionalInstructions are independent. No unchecked sample reaches a provider request.

CacheCompatibility maps exact selected model IDs to Modern or Legacy. Modern fields are
prompt_cache_options mode/ttl/comparison_response_id and marked content-part boundaries;
prewarm is emitted only by PrewarmAsync. Explicit mode requires 1–4 markers; implicit/default
allows 0–3 explicit markers because the provider uses one write slot for its implicit
boundary. Only 30m TTL is accepted. Legacy retention and modern controls stay mutually
exclusive as an intentionally conservative library policy. Unknown support and unsupported
combinations fail before auth. PromptCacheKey alone does not require advanced compatibility.
Terminal cache diagnostics retain unknown strings and valid independent counts, warning
on malformed counts without changing output. Prewarm requires completed, empty output,
returns OpenAiPrewarmResult plus normal metadata/usage, and rejects generation/progress,
continuation, output capture/guidance and Store=true. Neither operation promises a write
or a hit; comparisons never load history.

OpenAiClient now shares bounded send/read, safe HTTP classification, credentials and
finalization across all endpoints. Responses retain the existing terminal/refusal/output
parser and SSE behavior. Embeddings send float batches and optional dimensions, require
unique complete indexes/equal nonempty finite vectors, and return immutable lists in input
order. Images send GPT Image-style size/count/quality/background/output_format controls;
positive custom dimensions override presets, transparent JPEG fails locally, and returned
count/base64/format signatures are verified. No full image decoder or URL fetch is used.
MaxImageResponseBytes defaults to 128 MiB separately from the 16 MiB response/embedding
limit. Usage is captured before feature-specific parsing; image text/image input/output
breakdowns and embedding prompt_tokens are preserved. Unknown image model/response IDs
stay unknown instead of being copied from request configuration.

Official documentation rechecked 2026-10-04: [Responses create](https://developers.openai.com/api/reference/cli/resources/responses/methods/create),
[conversation state](https://developers.openai.com/api/docs/guides/conversation-state),
[vision inputs](https://developers.openai.com/api/docs/guides/images-vision),
[prompt caching](https://developers.openai.com/api/docs/guides/prompt-caching),
[cache diagnostics](https://developers.openai.com/api/docs/guides/prompt-caching/diagnostics),
[embeddings create](https://developers.openai.com/api/reference/cli/resources/embeddings/methods/create),
and [images generate](https://developers.openai.com/api/reference/cli/resources/images/methods/generate).
The cache guide establishes the prewarm flag; endpoint references/guide differ in some
lookup-boundary details, so no lookup-window or cache-reuse guarantee is encoded. Image
dimension restrictions and advanced feature availability remain model/provider policy,
with unsupported model-specific options returned as provider failures rather than dropped.
No model defaults/ranking, pricing, automatic retries or paid calls were introduced.
