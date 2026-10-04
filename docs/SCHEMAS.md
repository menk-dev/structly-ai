# Schemas and analyzer diagnostics

`StructuredTask.Create<T>` creates and validates a strict JSON schema for the output type.
It does not read credentials or send HTTP requests. An unsupported schema throws
`StructuredSchemaException` with `Issues`, each containing a `Path`, `Code` and `Message`.

`CreateSchema(vocabularies)` returns a detached `JsonElement` containing the schema.
`ReadOutput(json, vocabularies)` checks JSON against the same schema before deserializing
it. Failure returns `InvalidOutput` or `UnsupportedSchema`.

## Supported types and serialization rules

| Shape | Rule |
| --- | --- |
| Root | A non-null concrete class or struct with at least one included property. |
| Members | Public getters with public setters or init accessors, or get-only properties bound to the selected JSON constructor. Every included property is required. Fields are excluded. |
| Naming | System.Text.Json camel case by default. `SerializationProfile.Naming=PropertyNaming.Preserve` keeps CLR names. `JsonPropertyName` overrides both. Names must be nonblank and unique, including inherited properties. |
| Order | Properties retain declaration order within each type, with derived members before inherited members. `JsonPropertyOrder` overrides this order; equal order values retain declaration order. Schema properties, required entries, field guidance and generated examples use the same order. Enum entries retain declaration order. |
| Nullability | Nullable types and nullability attributes allow explicit null. Missing properties are invalid. A nullable collection and nullable items are separate settings. |
| Scalars | string, bool, integral types, float/double/decimal, Guid, DateOnly, DateTime, DateTimeOffset. Date/identity types use format validation. |
| Enums | Strings with exact, case-sensitive serialized names. `JsonStringEnumMemberName` is supported. Enums must be nonempty, with no flags or numeric aliases. Nullable enums use a separate null branch. Default `[JsonConverter(typeof(JsonStringEnumConverter))]` and `[JsonConverter(typeof(JsonStringEnumConverter<TEnum>))]` are supported on enum types and enum properties, including nullable properties. Numeric output remains invalid. |
| Collections | One-dimensional arrays (except byte[]), List, IReadOnlyList, IList, ICollection, IEnumerable, IReadOnlyCollection, HashSet and ISet. Supported custom collections need one IEnumerable<T>, ICollection<T> and a public parameterless constructor supported by System.Text.Json. |
| Constructors | One JsonConstructor, otherwise public parameterless or sole public constructor. Parameters bind to included properties by CLR name (case insensitive) and exact type. DTO construction must also succeed at output time. |
| Ignore | `JsonIgnore` with Always excludes the property; Never includes it. Conditional omission is unsupported because included properties are required. |
| Descriptions | `System.ComponentModel.DescriptionAttribute` (for example, `[Description("Why this match was selected")]`) or `Schema(Description=...)`, nonblank if supplied. `Schema.Name` uses 1–64 ASCII letters/digits/underscore/hyphen. |

Dictionaries, arbitrary object/JsonElement/JsonNode, abstract/interface DTOs, tuples,
delegates, recursive DTOs/collections, multidimensional arrays, TimeSpan/TimeOnly/Uri,
async enumerables, other converters, inclusion/extension data, polymorphism, number handling
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

Length and item limits must be nonnegative, or -1 for unspecified. A minimum cannot exceed
its maximum. Numeric limits must be finite, or NaN for unspecified. String length counts
Unicode scalar values.
`StringConstraint.Format` supports date-time, time, date, duration, email, hostname,
ipv4, ipv6 and uuid. Patterns search for a match rather than requiring the entire string
to match. They support an ASCII ECMAScript subset: literals, character classes,
alternation, quantifiers and escaped punctuation. Groups, shorthand classes, backreferences
and class subtraction are unsupported. Patterns must be nonblank and no longer than
4,096 characters. Matching has a 100 ms timeout.

Supply request vocabularies as `IReadOnlyDictionary<string, IReadOnlyList<string>>`.
`DynamicVocabulary` applies to a string or string collection. Each referenced set must
be nonempty, with nonblank values that are unique using ordinal comparison. Values are
not trimmed or converted to another case. Unreferenced sets are ignored. The library copies
and sorts values for each request; there is no global vocabulary cache. Nullable items
can still be null. Generated output instructions and output validation use the same values.
See [output instructions](ADVANCED.md).

Schemas allow up to 10 object or array nesting levels, 5,000 generated properties,
1,000 generated enum or vocabulary entries, and 120,000 relevant Unicode scalar values
in schema strings. Enums with more than 250 entries allow at most 15,000 scalar values
in enum strings. Repeated inline types count each time. The serialized schema is limited
to 1 MiB. Models may impose stricter limits. The library does not remove constraints
to make a request succeed.

## Compiler diagnostics

The NuGet package automatically includes the C# analyzer under `analyzers/dotnet/cs`.
It targets netstandard2.0 and uses Roslyn 4.14 APIs; the .NET 10 SDK satisfies that
compiler requirement. It adds no compiler dependencies to the runtime package.

- `STAI001` (error): the analyzer can determine that a task or schema is invalid. The
  diagnostic includes the JSON path and runtime issue code. It checks task creation,
  typed schema and output inspection, execution, and typed prewarming calls.
- `STAI002` (warning): suggests replacements for known legacy StructuredLlm attributes,
  including unresolved names during migration. Use JsonPropertyName, JsonIgnore,
  nullable annotations, JsonStringEnumMemberName, Description/Schema or the corresponding
  String/Number/CollectionConstraint attributes.

The analyzer checks output types, names, attributes, constructor bindings and structural,
enum and string limits. It skips unresolved generic type parameters and generated code.
If naming settings are dynamic, it only reports errors that apply under both naming
policies. It checks constant inline schema names and descriptions.

Request vocabularies, dynamic options, serialized schema size, output construction, output
values and provider support are checked at runtime. Creating a task still validates its
schema even if compilation passes. The analyzer cannot prove that a provider will accept
a request configured at runtime.

Adjust severity through `.editorconfig`, for example
`dotnet_diagnostic.STAI002.severity = suggestion`. Suppression does not change runtime rules.

## Examples and vocabulary order

Tasks used only for schemas, validation or examples may omit `Instructions`. Explicit blank
instructions are rejected. Typed execution and typed prewarming require effective nonblank
instructions: request instructions override task instructions, and missing instructions return
`InvalidRequest` before credentials or HTTP execution.

`task.CreateExample(vocabularies)` returns detached JSON validated through `ReadOutput`.
Patterns and constraints that the bounded generator cannot reliably satisfy throw
`ArgumentException`; provide your own example for output specifications in that case.
The generator limits examples to 10,000 nodes, 1,000 array items and 1 MiB of string characters.

`StructuredTaskOptions.VocabularyOrder` defaults to `VocabularyOrder.Ordinal`.
Choose `PreserveInput` to retain caller ordering in schemas, examples, specifications and
batch preparation. Inputs are copied; ordinal duplicates remain invalid under either policy.
