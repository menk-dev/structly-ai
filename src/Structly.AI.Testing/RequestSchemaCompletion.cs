using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Structly.AI.Testing;

sealed class RequestSchemaCompletion
{
    int _nodes;
    int _characters;

    public static JsonElement Complete(JsonElement schema, string partialJson)
    {
        var completion = new RequestSchemaCompletion();
        var value = completion.Visit(schema, JsonNode.Parse(partialJson), false, 0);
        var json = value?.ToJsonString() ?? "null";
        if(System.Text.Encoding.UTF8.GetByteCount(json) > 1024 * 1024)
            throw Invalid();

        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    JsonNode? Visit(JsonElement schema, JsonNode? value, bool missing, int depth)
    {
        if(++_nodes > 10000 || depth > 32 || schema.ValueKind != JsonValueKind.Object)
            throw Invalid();

        foreach(var keyword in schema.EnumerateObject())
            if(keyword.Name is not ("type" or "description" or "properties" or "required" or "additionalProperties" or
                "items" or "minItems" or "maxItems" or "enum" or "format" or "minLength" or "maxLength" or
                "pattern" or "minimum" or "maximum" or "anyOf"))
                throw Invalid();

        if(schema.TryGetProperty("anyOf", out var branches))
        {
            if(branches.GetArrayLength() != 2 || branches[1].GetProperty("type").GetString() != "null")
                throw Invalid();

            if(missing || value is null)
                return null;

            return Visit(branches[0], value, false, depth + 1);
        }

        var type = schema.GetProperty("type").GetString();
        if(missing)
        {
            value = type switch
            {
                "object" => new JsonObject(),
                "array" => new JsonArray(),
                "boolean" => JsonValue.Create(false),
                "integer" or "number" => JsonValue.Create(NumberExample(schema, type == "integer")),
                "string" => JsonValue.Create(StringExample(schema)),
                _ => throw Invalid(),
            };
            if(value is JsonArray array)
            {
                var count = Bound(schema, "minItems", 0);
                if(count > 1000)
                    throw Invalid();

                for(var i = 0; i < count; i++)
                    array.Add(Visit(schema.GetProperty("items"), null, true, depth + 1));
            }
        }

        if(value is null)
            throw Invalid();

        if(type == "object" && value is JsonObject target)
        {
            var properties = schema.GetProperty("properties");
            if(target.Any(x => !properties.TryGetProperty(x.Key, out _)))
                throw Invalid();

            foreach(var property in properties.EnumerateObject())
            {
                var absent = !target.TryGetPropertyValue(property.Name, out var supplied);
                var completed = Visit(property.Value, supplied, absent, depth + 1);
                if(absent)
                    target.Add(property.Name, completed);
            }

            return target;
        }

        if(type == "array" && value is JsonArray items)
        {
            if(items.Count < Bound(schema, "minItems", 0) || items.Count > Bound(schema, "maxItems", Int32.MaxValue))
                throw Invalid();

            foreach(var item in items)
                Visit(schema.GetProperty("items"), item, false, depth + 1);

            return items;
        }

        var token = JsonSerializer.SerializeToElement(value);
        if(type == "string" && token.ValueKind == JsonValueKind.String)
        {
            var text = token.GetString()!;
            _characters += text.Length;
            var length = text.EnumerateRunes().Count();
            if(_characters > 1024 * 1024 || length < Bound(schema, "minLength", 0) || length > Bound(schema, "maxLength", Int32.MaxValue) ||
                schema.TryGetProperty("enum", out var entries) && !entries.EnumerateArray().Any(x => x.GetString() == text) ||
                schema.TryGetProperty("format", out var format) && !FormatRules.IsValid(format.GetString()!, text))
                throw Invalid();

            if(schema.TryGetProperty("pattern", out var pattern))
            {
                try
                {
                    if(!Regex.IsMatch(text, pattern.GetString()!, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
                        throw Invalid();
                }
                catch(RegexMatchTimeoutException)
                {
                    throw Invalid();
                }
            }

            return value;
        }

        if(type is "integer" or "number" && token.ValueKind == JsonValueKind.Number && token.TryGetDouble(out var number) && Double.IsFinite(number))
        {
            if(type == "integer" && !SchemaNumbers.IsInteger(token.GetRawText()) ||
                schema.TryGetProperty("minimum", out var min) && SchemaNumbers.Compare(token.GetRawText(), min.GetDouble()) < 0 ||
                schema.TryGetProperty("maximum", out var max) && SchemaNumbers.Compare(token.GetRawText(), max.GetDouble()) > 0)
                throw Invalid();

            return value;
        }

        if(type == "boolean" && token.ValueKind is JsonValueKind.True or JsonValueKind.False)
            return value;

        throw Invalid();
    }

    static int Bound(JsonElement schema, string name, int fallback)
        => schema.TryGetProperty(name, out var bound) ? bound.GetInt32() : fallback;

    static double NumberExample(JsonElement schema, bool integer)
    {
        var min = schema.TryGetProperty("minimum", out var lower) ? lower.GetDouble() : Double.NegativeInfinity;
        var max = schema.TryGetProperty("maximum", out var upper) ? upper.GetDouble() : Double.PositiveInfinity;
        if(integer)
        {
            min = Math.Ceiling(min);
            max = Math.Floor(max);
        }

        if(min > max)
            throw Invalid();

        return Math.Clamp(0, min, max);
    }

    static string StringExample(JsonElement schema)
    {
        if(schema.TryGetProperty("enum", out var entries))
            return entries[0].GetString()!;

        if(schema.TryGetProperty("pattern", out _))
            throw Invalid();

        if(schema.TryGetProperty("format", out var format))
            return format.GetString() switch
            {
                "uuid" => "00000000-0000-0000-0000-000000000000",
                "date" => "2000-01-01",
                "date-time" => "2000-01-01T00:00:00Z",
                "time" => "00:00:00Z",
                "duration" => "P1D",
                "email" => "test@example.com",
                "hostname" => "example.com",
                "ipv4" => "127.0.0.1",
                "ipv6" => "::1",
                _ => throw Invalid(),
            };

        var length = Bound(schema, "minLength", 0);
        if(length > 100000)
            throw Invalid();

        return new string('x', length);
    }

    static ArgumentException Invalid() => new("Cannot complete valid output for this request schema. Supply explicit values for unsupported constraints.");
}
