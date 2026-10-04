# Schemas and analyzer feedback

`StructuredTask.Create<T>` resolves and validates a finite strict schema immediately,
without credentials or transport. It throws `StructuredSchemaException` with actionable
`Issues` (`Path`, `Code`, `Message`). `CreateSchema(vocabularies)` returns a detached
`JsonElement`. `ReadOutput(json, vocabularies)` validates the same contract before
deserializing and returns `InvalidOutput` or `UnsupportedSchema` on failure.

## Supported contract

| Shape | Rule |
| --- | --- |
| Root | Non-null concrete class or struct DTO with at least one included property. |
| Members | Public getters with public setters/init, or get-only properties bound to the selected JSON constructor. All included members required. Fields excluded. |
| Naming | System.Text.Json camel case by default; `SerializationProfile.Naming=PropertyNaming.Preserve` retains CLR names. `JsonPropertyName` wins; names nonblank and unique, including inherited members. |
| Nullability | Nullable types and nullability attributes allow explicit null. Missing members remain invalid. Nullable collections and nullable items are independent. |
| Scalars | string, bool, integral types, float/double/decimal, Guid, DateOnly, DateTime, DateTimeOffset. Date/identity types use format validation. |
| Enums | String values, ordinal exact wire names; `JsonStringEnumMemberName` supported. Nonempty, no flags or numeric aliases. Nullable enums use a separate null branch. |
| Collections | One-dimensional arrays (except byte[]), List, IReadOnlyList, IList, ICollection, IEnumerable, IReadOnlyCollection, HashSet and ISet. Supported custom collections need one IEnumerable<T>, ICollection<T> and a public parameterless constructor supported by System.Text.Json. |
| Constructors | One JsonConstructor, otherwise public parameterless or sole public constructor. Parameters bind to included properties by CLR name (case insensitive) and exact type. DTO construction must also succeed at output time. |
| Ignore | `JsonIgnore` Always excludes; Never includes. Conditional omission is incompatible with required properties. |
| Descriptions | `Description` or `Schema(Description=...)`, nonblank if supplied. `Schema.Name` uses 1–64 ASCII letters/digits/underscore/hyphen. |

Dictionaries, arbitrary object/JsonElement/JsonNode, abstract/interface DTOs, tuples,
delegates, recursive DTOs/collections, multidimensional arrays, TimeSpan/TimeOnly/Uri,
async enumerables, converters, inclusion/extension data, polymorphism, number handling
and population overrides are rejected. Standard DataAnnotations do not change the schema.

## Constraints and vocabularies

```csharp
public sealed record Ticket
{
    [StringConstraint(MinLength = 1, MaxLength = 120)]
    public required string Summary { get; init; }
    [DynamicVocabulary("queues")]
    public required string Queue { get; init; }
    [NumberConstraint(Minimum = 0, Maximum = 1)]
    public double Confidence { get; init; }
    [CollectionConstraint(MaxItems = 5)]
    public string?[]? References { get; init; }
}
```

Length/item bounds are nonnegative or -1 (unspecified); minima cannot exceed maxima.
Numeric bounds are finite or NaN (unspecified). String length counts Unicode scalars.
`StringConstraint.Format` supports date-time, time, date, duration, email, hostname,
ipv4, ipv6 and uuid. Patterns use search semantics and a portable ASCII ECMAScript
subset: literals, character classes, alternation, quantifiers and literal punctuation
escapes. Groups, shorthand classes, backreferences and class subtraction are rejected;
patterns are nonblank, at most 4,096 characters, with 100ms matching timeout.

Supply request vocabularies as `IReadOnlyDictionary<string, IReadOnlyList<string>>`.
DynamicVocabulary targets a string or string collection. Referenced sets must be
nonempty with nonblank, ordinally unique values; values are neither trimmed nor case
folded. Unreferenced sets are ignored. Values are snapshotted and sorted; there is no
global vocabulary cache. Nullable items can still be null. Generated guidance and
output validation use the same resolved vocabulary. See [advanced guidance](ADVANCED.md).

Schemas support 10 object/array nesting levels, 5,000 emitted properties, 1,000 emitted
enum/vocabulary entries and 120,000 relevant string scalars. Enums with more than 250
entries allow at most 15,000 enum string scalars. Repeated inline types count repeatedly.
The serialized schema is capped at 1 MiB. Model-specific limitations may be stricter;
the library never drops a constraint to obtain a successful response.

## Compiler diagnostics

The NuGet package automatically includes the C# analyzer under `analyzers/dotnet/cs`.
It targets netstandard2.0 and uses Roslyn 4.14 APIs; the .NET 10 SDK satisfies that
compiler requirement. It adds no compiler dependencies to the runtime package.

- `STAI001` (error): a statically provable invalid task/schema contract. The diagnostic
  includes the serialized path and runtime issue code. Covers task creation, typed
  schema/output inspection, execution and typed prewarming calls.
- `STAI002` (warning): replacement guidance for known legacy StructuredLlm attributes,
  including unresolved names during migration. Use JsonPropertyName, JsonIgnore,
  nullable annotations, JsonStringEnumMemberName, Description/Schema or the corresponding
  String/Number/CollectionConstraint attributes.

The analyzer handles static DTOs, names, attributes, constructor bindings and structural/
enum/string limits. It skips unresolved generic type parameters and generated code.
When naming settings are supplied dynamically, it only reports violations present
under both supported policies. It inspects constant inline schema names/descriptions.
Request vocabularies, dynamic options, serialized schema byte size, host DTO construction,
output values and provider support remain runtime checks. Runtime startup validation
always runs; compiler diagnostics do not certify a runtime-configured provider request.

Adjust severity through `.editorconfig`, for example
`dotnet_diagnostic.STAI002.severity = suggestion`. Suppression does not change runtime rules.
