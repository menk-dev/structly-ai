using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Structly.AI.OpenAI;

sealed class DecisionQuestion
{
    public required string Name { get; init; }
    public required string JsonName { get; init; }
    public string? Key { get; init; }
    public required string Kind { get; init; }
    public required string Instructions { get; init; }
    public IReadOnlyList<DecisionOption<object>> Choices { get; init; } = [];
    public IReadOnlyList<DecisionLevel> Levels { get; init; } = [];

    public static bool IsAnswer(Type type) => type == typeof(DecisionPredicate) || type == typeof(DecisionScore) ||
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(DecisionChoice<>);

    public static bool IsSet(Type type) => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(DecisionSet<>) &&
        IsAnswer(type.GetGenericArguments()[0]);

    public static DecisionQuestion Create(Type type, string name, string jsonName, string instructions,
        IReadOnlyList<DecisionOption<object>>? choices = null, IReadOnlyList<DecisionLevel>? levels = null, string? key = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instructions);
        if(!IsAnswer(type))
            throw new ArgumentException("Use a predicate, choice or score answer type.");

        if(type == typeof(DecisionPredicate))
            return new() { Name = name, JsonName = jsonName, Key = key, Kind = "predicate", Instructions = instructions };

        if(type == typeof(DecisionScore))
        {
            var snapshot = levels?.ToArray() ?? throw new ArgumentException("Score questions require ordered levels.");
            if(snapshot.Length < 2 || snapshot.Any(x => x is null || String.IsNullOrWhiteSpace(x.Label)) ||
                snapshot.Select(x => x.Label).Distinct(StringComparer.Ordinal).Count() != snapshot.Length)
                throw new ArgumentException("Supply at least two distinct, nonblank rubric levels.");

            return new() { Name = name, JsonName = jsonName, Key = key, Kind = "score", Instructions = instructions, Levels = Array.AsReadOnly(snapshot) };
        }

        var valueType = type.GetGenericArguments()[0];
        if(valueType != typeof(string) && valueType != typeof(bool) && !valueType.IsEnum)
            throw new ArgumentException("Choice values must be string, bool or an enum.");

        if(valueType.IsEnum && valueType.IsDefined(typeof(FlagsAttribute)))
            throw new ArgumentException("Flags enums are not supported as decision choices.");

        if(valueType.IsDefined(typeof(JsonConverterAttribute)))
            throw new ArgumentException("Custom choice converters are unsupported.");

        var fields = valueType.IsEnum ? valueType.GetFields(BindingFlags.Public | BindingFlags.Static) : [];
        if(fields.Select(x => x.GetValue(null)).Distinct().Count() != fields.Length ||
            fields.Select(EnumName).Distinct(StringComparer.Ordinal).Count() != fields.Length)
            throw new ArgumentException("Enum values and serialized names must be unique.");

        choices ??= valueType == typeof(bool) ? [new(false), new(true)] : valueType.IsEnum
            ? fields.Select(x => new DecisionOption<object>(x.GetValue(null)!, x.GetCustomAttribute<DescriptionAttribute>()?.Description)).ToArray()
            : throw new ArgumentException("String choices must be supplied explicitly.");
        var options = choices.Select(option =>
        {
            if(option is null || option.Value is null || option.Value.GetType() != valueType)
                throw new ArgumentException("Every choice must have the declared value type.");

            object wire = option.Value;
            var description = option.Description;
            if(valueType.IsEnum)
            {
                var field = fields.SingleOrDefault(x => Equals(x.GetValue(null), option.Value))
                    ?? throw new ArgumentException("Choice must be a declared enum value.");
                wire = EnumName(field);
                description ??= field.GetCustomAttribute<DescriptionAttribute>()?.Description;
            }

            if(wire is string text && String.IsNullOrWhiteSpace(text))
                throw new ArgumentException("String choices must be nonblank.");

            return new DecisionOption<object>(wire, description);
        }).ToArray();
        if(options.Length is < 2 or > 255 || options.Select(x => x.Value).Distinct().Count() != options.Length)
            throw new ArgumentException("Supply between 2 and 255 distinct choices.");

        return new() { Name = name, JsonName = jsonName, Key = key, Kind = "choice", Instructions = instructions, Choices = Array.AsReadOnly(options) };
    }

    static string EnumName(FieldInfo field) => field.GetCustomAttribute<JsonStringEnumMemberNameAttribute>()?.Name ?? field.Name;

    public Dictionary<string, object?> Payload()
    {
        var payload = new Dictionary<string, object?> { ["name"] = Name, ["type"] = Kind, ["instructions"] = Instructions };
        if(Kind == "choice")
            payload["choices"] = Choices.Select(x => Describe("value", x.Value, x.Description)).ToArray();
        else if(Kind == "score")
            payload["levels"] = Levels.Select(x => Describe("label", x.Label, x.Description)).ToArray();

        return payload;
    }

    static Dictionary<string, object?> Describe(string key, object value, string? description)
    {
        var result = new Dictionary<string, object?> { [key] = value };
        if(description is not null)
            result["description"] = description;

        return result;
    }

    public bool TryRead(JsonElement answer, out Dictionary<string, object?> value)
    {
        value = [];
        if(Kind == "predicate")
        {
            if(!Probability(answer, "probability", out var probability))
                return false;

            value["Probability"] = probability;
            return true;
        }

        if(!Probability(answer, "confidence", out var confidence) ||
            !answer.TryGetProperty("probabilities", out var distribution) || distribution.ValueKind != JsonValueKind.Array)
            return false;

        var count = Kind == "choice" ? Choices.Count : Levels.Count;
        if(distribution.GetArrayLength() != count)
            return false;

        var ordered = new Dictionary<string, object?>[count];
        var sum = 0d;
        var weighted = 0d;
        foreach(var item in distribution.EnumerateArray())
        {
            if(!Probability(item, "probability", out var probability) || !item.TryGetProperty("value", out var raw))
                return false;

            int index;
            if(Kind == "choice")
            {
                index = ChoiceIndex(raw);
                if(index < 0 || ordered[index] is not null)
                    return false;

                ordered[index] = new() { ["Value"] = Choices[index].Value, ["Probability"] = probability };
            }
            else
            {
                if(raw.ValueKind != JsonValueKind.Number || !raw.TryGetInt32(out index) || index < 0 || index >= count ||
                    ordered[index] is not null || !item.TryGetProperty("label", out var label) || label.ValueKind != JsonValueKind.String ||
                    label.GetString() != Levels[index].Label)
                    return false;

                ordered[index] = new() { ["Value"] = index, ["Label"] = Levels[index].Label, ["Probability"] = probability };
                weighted += index * probability;
            }

            sum += probability;
        }

        const double tolerance = 1e-4;
        if(Math.Abs(sum - 1) > tolerance)
            return false;

        value["Confidence"] = confidence;
        value["Probabilities"] = ordered;
        if(Kind == "choice")
        {
            if(!answer.TryGetProperty("choice", out var choice))
                return false;

            var index = ChoiceIndex(choice);
            if(index < 0)
                return false;

            value["Choice"] = Choices[index].Value;
        }
        else
        {
            if(!Number(answer, "score", out var score) || score < 0 || score > count - 1 || Math.Abs(score - weighted) > tolerance)
                return false;

            value["Score"] = score;
        }

        return true;
    }

    int ChoiceIndex(JsonElement value)
    {
        object? wire = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
        for(var i = 0; i < Choices.Count; i++)
            if(Equals(Choices[i].Value, wire))
                return i;

        return -1;
    }

    static bool Number(JsonElement parent, string name, out double value)
    {
        value = 0;
        return parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var element) &&
            element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out value) && Double.IsFinite(value);
    }

    static bool Probability(JsonElement parent, string name, out double value)
        => Number(parent, name, out value) && value is >= 0 and <= 1;
}
