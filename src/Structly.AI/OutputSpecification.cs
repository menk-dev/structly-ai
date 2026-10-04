using System.Text.Json;
using System.Text.Json.Nodes;

namespace Structly.AI;

public sealed partial class StructuredTask<T>
{
    /// <summary>Creates deterministic guidance from the same contract used by schema and output validation.</summary>
    public string CreateOutputSpecification(OutputSpecificationOptions options,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? vocabularies = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.AdditionalInstructions is { } instructions && String.IsNullOrWhiteSpace(instructions) ||
            !options.IncludeExample && options.ExampleJson is not null)
            throw new ArgumentException("Use nonblank instructions and enable IncludeExample when supplying ExampleJson.", nameof(options));
        var schema = CreateSchema(vocabularies);
        var sections = new List<string>();
        if (options.IncludeFields)
            sections.Add("Return exactly these serialized fields:\n" + String.Join("\n", DescribeFields(schema, "$")) +
                "\n\nResolved output schema (all constraints apply):\n" + schema.GetRawText());
        if (options.IncludeExample)
        {
            var values = SchemaWriter.ResolveVocabularies(_contract, vocabularies);
            var json = options.ExampleJson ?? Sample(_contract, values, new SampleBudget())?.ToJsonString() ?? "null";
            var result = ReadOutput(json, values.ToDictionary(x => x.Key, x => (IReadOnlyList<string>)x.Value));
            if (!result.IsSuccess)
                throw new ArgumentException("ExampleJson must satisfy the output contract. Supply a valid ExampleJson or set IncludeExample=false.", nameof(options));
            using var document = JsonDocument.Parse(json);
            sections.Add("Example of valid output JSON:\n" + JsonSerializer.Serialize(document.RootElement));
        }
        if (options.AdditionalInstructions is { } additional) sections.Add(additional);
        return String.Join("\n\n", sections);
    }

    static IEnumerable<string> DescribeFields(JsonElement schema, string path)
    {
        var nullable = schema.TryGetProperty("anyOf", out var branches);
        if (nullable) schema = branches[0];
        var type = schema.GetProperty("type").GetString();
        var description = schema.TryGetProperty("description", out var text) ? "; " + text.GetString() : "";
        if (path != "$")
        {
            var constraints = schema.EnumerateObject().Where(x => x.Name is not ("type" or "description" or "properties" or "required" or "additionalProperties" or "items"))
                .Select(x => x.Name + "=" + x.Value.GetRawText());
            yield return "- " + path + ": " + type + (nullable ? " or null" : "") + description +
                String.Concat(constraints.Select(x => "; " + x));
        }
        if (type == "object")
            foreach (var member in schema.GetProperty("properties").EnumerateObject())
                foreach (var line in DescribeFields(member.Value, path + "." + member.Name)) yield return line;
        if (type == "array")
            foreach (var line in DescribeFields(schema.GetProperty("items"), path + "[]")) yield return line;
    }

    static JsonNode? Sample(SchemaNode node, Dictionary<string, string[]> values, SampleBudget budget)
    {
        if (++budget.Nodes > 10000) throw SampleRequired();
        if (node.Nullable) return null;
        if (node.Kind == SchemaKind.Object)
        {
            var sample = new JsonObject();
            foreach (var member in node.Members) sample[member.Name] = Sample(member.Node, values, budget);
            return sample;
        }
        if (node.Kind == SchemaKind.Array)
        {
            var count = Math.Max(node.MinItems, 0);
            if (count > 1000 || node.IsSet && count > 1) throw SampleRequired();
            var array = new JsonArray();
            for (var i = 0; i < count; i++) array.Add(Sample(node.Item!, values, budget));
            return array;
        }
        if (node.Kind == SchemaKind.Boolean) return JsonValue.Create(false);
        if (node.Kind is SchemaKind.Integer or SchemaKind.Number)
        {
            var lower = Double.IsNaN(node.Minimum) ? Double.NegativeInfinity : node.Minimum;
            var upper = Double.IsNaN(node.Maximum) ? Double.PositiveInfinity : node.Maximum;
            if (node.Kind == SchemaKind.Integer) { lower = Math.Ceiling(lower); upper = Math.Floor(upper); }
            var number = Math.Clamp(0, lower, upper);
            return JsonValue.Create(number);
        }
        var entries = node.Vocabulary is { } vocabulary ? values[vocabulary] : node.EnumValues;
        if (entries is not null) return SampleText(entries.Order(StringComparer.Ordinal).First(), budget);
        if (node.Pattern is not null) throw SampleRequired();
        string text;
        if (node.Type == typeof(Guid)) text = "00000000-0000-0000-0000-000000000000";
        else if (node.Type == typeof(DateOnly)) text = "2000-01-01";
        else if (node.Type == typeof(DateTime) || node.Type == typeof(DateTimeOffset)) text = "2000-01-01T00:00:00Z";
        else
        {
            if (node.Format is not null || node.MinLength > 100000) throw SampleRequired();
            text = new string('x', Math.Max(node.MinLength, 0));
        }
        return SampleText(text, budget);
    }

    static JsonNode SampleText(string text, SampleBudget budget)
    {
        budget.Characters += text.Length;
        if (budget.Characters > 1024 * 1024) throw SampleRequired();
        return JsonValue.Create(text)!;
    }

    sealed class SampleBudget
    {
        public int Nodes { get; set; }
        public int Characters { get; set; }
    }

    static ArgumentException SampleRequired() => new("Cannot reliably generate this constrained example. Supply ExampleJson or set IncludeExample=false.");
}
