using System.Text.Json;
using System.Text.RegularExpressions;

namespace Structly.AI;

static class OutputValidator
{
    public static IReadOnlyList<StructuredIssue> Validate(SchemaNode root, JsonElement json,
        Dictionary<string, string[]> vocabularies, JsonSerializerOptions serializer)
    {
        var issues = new List<StructuredIssue>();
        Visit(root, json, "$", vocabularies, serializer, issues);
        return issues.AsReadOnly();
    }

    static void Visit(SchemaNode node, JsonElement value, string path, Dictionary<string, string[]> vocabularies,
        JsonSerializerOptions serializer, List<StructuredIssue> issues)
    {
        if(issues.Count >= 100)
            return;

        void Issue(string code, string message)
        {
            if(issues.Count < 100)
                issues.Add(new(path, code, message));
        }
        if(value.ValueKind == JsonValueKind.Null)
        {
            if(!node.Nullable)
                Issue("Null", "A non-null value is required.");

            return;
        }

        var validToken = node.Kind switch
        {
            SchemaKind.Object => value.ValueKind == JsonValueKind.Object,
            SchemaKind.Array => value.ValueKind == JsonValueKind.Array,
            SchemaKind.String or SchemaKind.Enum => value.ValueKind == JsonValueKind.String,
            SchemaKind.Boolean => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            _ => value.ValueKind == JsonValueKind.Number,
        };
        if(!validToken)
        {
            Issue("TokenType", "JSON token type must match the declared contract.");
            return;
        }

        if(node.Kind == SchemaKind.Object)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach(var property in value.EnumerateObject())
            {
                if(issues.Count >= 100)
                    return;

                var member = node.Members.FirstOrDefault(x => x.Name == property.Name);
                if(!seen.Add(property.Name))
                    issues.Add(new(member is null ? path : path + "." + member.Name, "DuplicateKey", "Duplicate JSON keys are invalid."));

                if(member is null)
                    Issue("AdditionalKey", "Only declared serialized keys are allowed.");
                else
                    Visit(member.Node, property.Value, path + "." + property.Name, vocabularies, serializer, issues);
            }

            foreach(var member in node.Members)
                if(!seen.Contains(member.Name) && issues.Count < 100)
                    issues.Add(new(path + "." + member.Name, "MissingKey", "Every declared key is required, including nullable keys."));

            return;
        }

        if(node.Kind == SchemaKind.Array)
        {
            var count = value.GetArrayLength();
            if(node.MinItems >= 0 && count < node.MinItems || node.MaxItems >= 0 && count > node.MaxItems)
                Issue("ItemCount", "Collection cardinality violates its bounds.");

            var elements = node.IsSet ? new HashSet<object?>() : null;
            foreach(var item in value.EnumerateArray())
            {
                if(issues.Count >= 100)
                    break;

                var before = issues.Count;
                Visit(node.Item!, item, path + "[]", vocabularies, serializer, issues);
                if(elements is not null && before == issues.Count)
                {
                    try
                    {
                        // Compare as the target element type, matching HashSet/ISet equality.
                        var element = JsonSerializer.Deserialize(item, node.Item!.Nullable && node.Item.Type.IsValueType
                            ? typeof(Nullable<>).MakeGenericType(node.Item.Type) : node.Item.Type, serializer);
                        if(!elements.Add(element))
                            Issue("SetDuplicate", "Set collections cannot contain duplicate typed elements.");
                    }
                    catch(JsonException)
                    {
                        Issue("Deserialization", "Collection items must be constructible as declared.");
                    }
                }
            }

            return;
        }

        if(node.Kind is SchemaKind.String or SchemaKind.Enum)
        {
            var text = value.GetString()!;
            var entries = node.Vocabulary is { } key ? vocabularies[key] : node.EnumValues;
            if(entries is not null && !entries.Contains(text, StringComparer.Ordinal))
                Issue("EnumValue", "Value must exactly match a declared enum or vocabulary wire value.");

            var length = SchemaWriter.ScalarCount(text);
            if(node.MinLength >= 0 && length < node.MinLength || node.MaxLength >= 0 && length > node.MaxLength)
                Issue("StringLength", "Unicode scalar length violates its bounds.");

            try
            {
                if(node.Regex is not null && !node.Regex.IsMatch(text))
                    Issue("Pattern", "String does not match the required pattern.");
            }
            catch(RegexMatchTimeoutException)
            {
                Issue("PatternTimeout", "Pattern validation exceeded its bounded execution time.");
            }

            if(node.Format is not null && !FormatRules.IsValid(node.Format, text))
                Issue("Format", "String does not satisfy its declared format.");

            if(node.Type != typeof(string) && node.Kind == SchemaKind.String)
                CheckRepresentable(node, value, serializer, Issue);

            return;
        }

        if(node.Kind is SchemaKind.Integer or SchemaKind.Number)
        {
            CheckRepresentable(node, value, serializer, Issue);
            if(!Double.IsNaN(node.Minimum) && SchemaNumbers.Compare(value.GetRawText(), node.Minimum) < 0
                || !Double.IsNaN(node.Maximum) && SchemaNumbers.Compare(value.GetRawText(), node.Maximum) > 0)
                Issue("NumberBounds", "Number violates its inclusive bounds.");
        }
    }

    static void CheckRepresentable(SchemaNode node, JsonElement value, JsonSerializerOptions serializer, Action<string, string> issue)
    {
        try
        {
            var result = JsonSerializer.Deserialize(value, node.Type, serializer);
            if(result is double d && !Double.IsFinite(d) || result is float f && !Single.IsFinite(f))
                issue("NumericRange", "Numeric values must be finite and representable in the declared CLR type.");
        }
        catch(JsonException)
        {
            issue("ScalarValue", "Scalar must be representable in the declared CLR type without coercion.");
        }
    }
}
