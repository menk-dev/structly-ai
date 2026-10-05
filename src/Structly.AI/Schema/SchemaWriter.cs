using System.Text.Json;
using System.Text.Json.Nodes;

namespace Structly.AI;

static class SchemaWriter
{
    public static JsonElement Create(SchemaNode root, string? description,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? vocabularies, bool allowMissingVocabularies = false, VocabularyOrder order = VocabularyOrder.Ordinal)
    {
        var values = ResolveVocabularies(root, vocabularies, allowMissingVocabularies, order);
        var budget = new Budget();
        var schema = Emit(root, "$", 0, values, budget);
        if(description is not null)
            schema["description"] = description;

        using var buffer = new LimitedSchemaStream();
        using(var writer = new Utf8JsonWriter(buffer))
            schema.WriteTo(writer);

        using var document = JsonDocument.Parse(buffer.ToArray());
        return document.RootElement.Clone();
    }

    public static Dictionary<string, string[]> ResolveVocabularies(SchemaNode root,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? supplied, bool allowMissing = false, VocabularyOrder order = VocabularyOrder.Ordinal)
    {
        var values = new Dictionary<string, string[]>(StringComparer.Ordinal);
        Visit(root, "$", values, supplied, allowMissing, order);
        return values;
    }

    static void Visit(SchemaNode node, string path, Dictionary<string, string[]> values,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? supplied, bool allowMissing, VocabularyOrder order)
    {
        if(node.Vocabulary is { } name && !values.ContainsKey(name))
        {
            if(supplied is null || !supplied.TryGetValue(name, out var entries))
            {
                if(!allowMissing)
                    SchemaResolver.Fail(path, "VocabularyMissing", $"Supply the referenced vocabulary '{name}'.");
            }
            else
            {
                if(entries is null || entries.Count == 0)
                    SchemaResolver.Fail(path, "VocabularyEmpty", "Referenced vocabularies must be nonempty.");

                if(entries.Count > 1000)
                    SchemaResolver.Fail(path, "EnumLimit", "A vocabulary cannot exceed 1,000 entries.");

                var snapshot = new string[entries.Count];
                var unique = new HashSet<string>(StringComparer.Ordinal);
                var chars = 0;
                for(var i = 0; i < entries.Count; i++)
                {
                    var value = entries[i];
                    if(String.IsNullOrWhiteSpace(value) || !unique.Add(value))
                        SchemaResolver.Fail(path, "VocabularyValue", "Vocabulary values must be nonblank and ordinally unique; values are not trimmed.");

                    chars += ScalarCount(value, 120000);
                    if(chars > 120000 || entries.Count > 250 && chars > 15000)
                        SchemaResolver.Fail(path, "EnumStringLimit", "Vocabulary strings exceed the strict-schema character limit.");

                    snapshot[i] = value;
                }

                if(order == VocabularyOrder.Ordinal)
                    Array.Sort(snapshot, StringComparer.Ordinal);

                values.Add(name, snapshot);
            }
        }

        if(node.Item is not null)
            Visit(node.Item, path + "[]", values, supplied, allowMissing, order);

        foreach(var member in node.Members)
            Visit(member.Node, path + "." + member.Name, values, supplied, allowMissing, order);
    }

    static JsonObject Emit(SchemaNode node, string path, int depth, Dictionary<string, string[]> values, Budget budget)
    {
        if(node.Kind is SchemaKind.Object or SchemaKind.Array && ++depth > 10)
            SchemaResolver.Fail(path, "DepthLimit", "Strict schemas support at most 10 object/array nesting levels.");

        var schema = new JsonObject();
        if(node.Description is not null)
            schema["description"] = node.Description;

        schema["type"] = node.Kind switch
        {
            SchemaKind.Object => "object",
            SchemaKind.Array => "array",
            SchemaKind.Integer => "integer",
            SchemaKind.Number => "number",
            SchemaKind.Boolean => "boolean",
            _ => "string",
        };
        if(node.Kind == SchemaKind.Object)
        {
            var properties = new JsonObject();
            var required = new JsonArray();
            foreach(var member in node.Members)
            {
                if(++budget.Properties > 5000)
                    SchemaResolver.Fail(path, "PropertyLimit", "Strict schemas support at most 5,000 emitted object properties.");

                budget.CountString(member.Name, path);
                properties[member.Name] = Emit(member.Node, path + "." + member.Name, depth, values, budget);
                required.Add(member.Name);
            }

            schema["properties"] = properties;
            schema["required"] = required;
            schema["additionalProperties"] = false;
        }

        if(node.Kind == SchemaKind.Array)
        {
            schema["items"] = Emit(node.Item!, path + "[]", depth, values, budget);
            if(node.MinItems >= 0)
                schema["minItems"] = node.MinItems;

            if(node.MaxItems >= 0)
                schema["maxItems"] = node.MaxItems;
            // uniqueItems is not in OpenAI's strict subset; set semantics are checked locally.
        }

        var entries = node.Vocabulary is { } vocabulary ? values.GetValueOrDefault(vocabulary) : node.EnumValues;
        if(entries is not null)
        {
            budget.Enums += entries.Length;
            if(budget.Enums > 1000)
                SchemaResolver.Fail(path, "EnumLimit", "Strict schemas support at most 1,000 emitted enum entries.");

            var chars = 0;
            var array = new JsonArray();
            foreach(var entry in entries)
            {
                chars += budget.CountString(entry, path);
                if(entries.Length > 250 && chars > 15000)
                    SchemaResolver.Fail(path, "EnumStringLimit", "Enums with more than 250 values support at most 15,000 string characters.");

                array.Add(entry);
            }

            schema["enum"] = array;
        }

        if(node.Format is not null)
            schema["format"] = node.Format;

        if(node.MinLength >= 0)
            schema["minLength"] = node.MinLength;

        if(node.MaxLength >= 0)
            schema["maxLength"] = node.MaxLength;

        if(node.Pattern is not null)
            schema["pattern"] = node.Pattern;

        if(!Double.IsNaN(node.Minimum))
            schema["minimum"] = node.Minimum;

        if(!Double.IsNaN(node.Maximum))
            schema["maximum"] = node.Maximum;

        return node.Nullable ? new JsonObject { ["anyOf"] = new JsonArray(schema, new JsonObject { ["type"] = "null" }) } : schema;
    }

    public static int ScalarCount(string text, int cap = Int32.MaxValue)
    {
        var count = 0;
        foreach(var rune in text.EnumerateRunes())
            if(++count > cap)
                break;

        return count;
    }

    sealed class Budget
    {
        public int Properties;
        public int Enums;
        int _characters;

        public int CountString(string value, string path)
        {
            var count = ScalarCount(value, 120000);
            _characters += count;
            if(_characters > 120000)
                SchemaResolver.Fail(path, "StringLimit", "Strict schemas support at most 120,000 relevant string characters.");

            return count;
        }
    }

    sealed class LimitedSchemaStream : MemoryStream
    {
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            Check(buffer.Length);
            base.Write(buffer);
        }
        public override void Write(byte[] buffer, int offset, int count)
        {
            Check(count);
            base.Write(buffer, offset, count);
        }
        void Check(int count)
        {
            if(Length + count > 1024 * 1024)
                SchemaResolver.Fail("$", "SchemaSize", "Serialized schemas cannot exceed 1 MiB.");
        }
    }
}
