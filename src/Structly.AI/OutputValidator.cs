using System.Globalization;
using System.Net;
using System.Net.Mail;
using System.Net.Sockets;
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
        if (issues.Count >= 100) return;
        void Issue(string code, string message)
        {
            if (issues.Count < 100) issues.Add(new(path, code, message));
        }
        if (value.ValueKind == JsonValueKind.Null)
        {
            if (!node.Nullable) Issue("Null", "A non-null value is required.");
            return;
        }
        var validToken = node.Kind switch
        {
            SchemaKind.Object => value.ValueKind == JsonValueKind.Object,
            SchemaKind.Array => value.ValueKind == JsonValueKind.Array,
            SchemaKind.String or SchemaKind.Enum => value.ValueKind == JsonValueKind.String,
            SchemaKind.Boolean => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            _ => value.ValueKind == JsonValueKind.Number
        };
        if (!validToken) { Issue("TokenType", "JSON token type must match the declared contract."); return; }
        if (node.Kind == SchemaKind.Object)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (issues.Count >= 100) return;
                var member = node.Members.FirstOrDefault(x => x.Name == property.Name);
                if (!seen.Add(property.Name))
                    issues.Add(new(member is null ? path : path + "." + member.Name, "DuplicateKey", "Duplicate JSON keys are invalid."));
                if (member is null) Issue("AdditionalKey", "Only declared serialized keys are allowed.");
                else Visit(member.Node, property.Value, path + "." + property.Name, vocabularies, serializer, issues);
            }
            foreach (var member in node.Members)
                if (!seen.Contains(member.Name) && issues.Count < 100)
                    issues.Add(new(path + "." + member.Name, "MissingKey", "Every declared key is required, including nullable keys."));
            return;
        }
        if (node.Kind == SchemaKind.Array)
        {
            var count = value.GetArrayLength();
            if (node.MinItems >= 0 && count < node.MinItems || node.MaxItems >= 0 && count > node.MaxItems)
                Issue("ItemCount", "Collection cardinality violates its bounds.");
            var elements = node.IsSet ? new HashSet<object?>() : null;
            foreach (var item in value.EnumerateArray())
            {
                if (issues.Count >= 100) break;
                var before = issues.Count;
                Visit(node.Item!, item, path + "[]", vocabularies, serializer, issues);
                if (elements is not null && before == issues.Count)
                {
                    try
                    {
                        // Compare as the target element type, matching HashSet/ISet equality.
                        var element = JsonSerializer.Deserialize(item, node.Item!.Nullable && node.Item.Type.IsValueType
                            ? typeof(Nullable<>).MakeGenericType(node.Item.Type) : node.Item.Type, serializer);
                        if (!elements.Add(element)) Issue("SetDuplicate", "Set collections cannot contain duplicate typed elements.");
                    }
                    catch (JsonException) { Issue("Deserialization", "Collection items must be constructible as declared."); }
                }
            }
            return;
        }
        if (node.Kind is SchemaKind.String or SchemaKind.Enum)
        {
            var text = value.GetString()!;
            var entries = node.Vocabulary is { } key ? vocabularies[key] : node.EnumValues;
            if (entries is not null && !entries.Contains(text, StringComparer.Ordinal))
                Issue("EnumValue", "Value must exactly match a declared enum or vocabulary wire value.");
            var length = SchemaWriter.ScalarCount(text);
            if (node.MinLength >= 0 && length < node.MinLength || node.MaxLength >= 0 && length > node.MaxLength)
                Issue("StringLength", "Unicode scalar length violates its bounds.");
            try
            {
                if (node.Regex is not null && !node.Regex.IsMatch(text)) Issue("Pattern", "String does not match the required pattern.");
            }
            catch (RegexMatchTimeoutException) { Issue("PatternTimeout", "Pattern validation exceeded its bounded execution time."); }
            if (node.Format is not null && !FormatRules.IsValid(node.Format, text)) Issue("Format", "String does not satisfy its declared format.");
            if (node.Type != typeof(string) && node.Kind == SchemaKind.String)
                CheckRepresentable(node, value, serializer, Issue);
            return;
        }
        if (node.Kind is SchemaKind.Integer or SchemaKind.Number)
        {
            CheckRepresentable(node, value, serializer, Issue);
            if (!Double.IsNaN(node.Minimum) && SchemaNumbers.Compare(value.GetRawText(), node.Minimum) < 0
                || !Double.IsNaN(node.Maximum) && SchemaNumbers.Compare(value.GetRawText(), node.Maximum) > 0)
                Issue("NumberBounds", "Number violates its inclusive bounds.");
        }
    }

    static void CheckRepresentable(SchemaNode node, JsonElement value, JsonSerializerOptions serializer, Action<string, string> issue)
    {
        try
        {
            var result = JsonSerializer.Deserialize(value, node.Type, serializer);
            if (result is double d && !Double.IsFinite(d) || result is float f && !Single.IsFinite(f))
                issue("NumericRange", "Numeric values must be finite and representable in the declared CLR type.");
        }
        catch (JsonException) { issue("ScalarValue", "Scalar must be representable in the declared CLR type without coercion."); }
    }
}

static class SchemaNumbers
{
    // Compare the original JSON number against the emitted bound, without rounding
    // through a CLR numeric type. In particular -1e-30 must not become decimal zero.
    public static int Compare(string json, double bound)
    {
        var left = Normalize(json);
        var right = Normalize(bound.ToString("R", CultureInfo.InvariantCulture));
        if (left.Sign != right.Sign) return left.Sign.CompareTo(right.Sign);
        if (left.Sign == 0) return 0;
        if (left.Magnitude != right.Magnitude) return left.Sign * left.Magnitude.CompareTo(right.Magnitude);
        for (var i = 0; i < Math.Max(left.Digits.Length, right.Digits.Length); i++)
        {
            var a = i < left.Digits.Length ? left.Digits[i] : '0';
            var b = i < right.Digits.Length ? right.Digits[i] : '0';
            if (a != b) return left.Sign * a.CompareTo(b);
        }
        return 0;
    }

    static Number Normalize(string text)
    {
        var sign = text[0] == '-' ? -1 : 1;
        var offset = text[0] is '-' or '+' ? 1 : 0;
        var index = text.IndexOfAny(['e', 'E']);
        long exponent = 0;
        if (index >= 0 && !Int64.TryParse(text.AsSpan(index + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out exponent))
            exponent = text[index + 1] == '-' ? Int64.MinValue / 2 : Int64.MaxValue / 2;
        // Saturate exponents before adding a bounded (<= 16 MiB) significand length.
        exponent = Math.Clamp(exponent, Int64.MinValue / 2, Int64.MaxValue / 2);
        var significand = index >= 0 ? text[offset..index] : text[offset..];
        var point = significand.IndexOf('.');
        var fractionLength = point >= 0 ? significand.Length - point - 1 : 0;
        var digits = significand.Replace(".", "", StringComparison.Ordinal).TrimStart('0');
        return new Number
        {
            Sign = digits.Length == 0 ? 0 : sign,
            Magnitude = exponent - fractionLength + digits.Length,
            Digits = digits.TrimEnd('0')
        };
    }

    sealed record Number
    {
        public required int Sign { get; init; }
        public required long Magnitude { get; init; }
        public required string Digits { get; init; }
    }
}

static class FormatRules
{
    public static readonly HashSet<string> Formats = ["date-time", "time", "date", "duration", "email", "hostname", "ipv4", "ipv6", "uuid"];
    static readonly TimeSpan _regexTimeout = TimeSpan.FromMilliseconds(100);

    public static bool IsValid(string format, string text)
    {
        try
        {
            return format switch
            {
                "uuid" => Guid.TryParseExact(text, "D", out _),
                "date" => DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
                "date-time" => text.Length >= 20 && text[10] is 'T' or 't'
                    && IsValid("date", text[..10]) && IsValid("time", text[11..]),
                "time" => Match(text, @"\A([01][0-9]|2[0-3]):[0-5][0-9]:([0-5][0-9]|60)(\.[0-9]+)?([Zz]|[+-]([01][0-9]|2[0-3]):[0-5][0-9])\z"),
                "duration" => Match(text, @"\AP(([0-9]+Y)?([0-9]+M)?([0-9]+D)?(T([0-9]+H)?([0-9]+M)?([0-9]+(\.[0-9]+)?S)?)?|[0-9]+W)\z")
                    && text != "P" && !text.EndsWith('T'),
                "email" => ValidEmail(text),
                "hostname" => ValidHostname(text),
                "ipv4" => Match(text, @"\A[0-9]{1,3}(\.[0-9]{1,3}){3}\z") && text.Split('.').All(x => (x.Length == 1 || x[0] != '0') && Int32.Parse(x, CultureInfo.InvariantCulture) <= 255),
                "ipv6" => !text.Contains('%') && IPAddress.TryParse(text, out var address) && address.AddressFamily == AddressFamily.InterNetworkV6,
                _ => false
            };
        }
        catch (Exception exception) when (exception is FormatException or OverflowException or RegexMatchTimeoutException) { return false; }
    }

    static bool Match(string text, string pattern) => Regex.IsMatch(text, pattern, RegexOptions.CultureInvariant, _regexTimeout);
    static bool ValidHostname(string text)
        => text.Length is > 0 and <= 253 && (text.EndsWith('.') ? text[..^1] : text).Split('.').All(x => x.Length is > 0 and <= 63 && Match(x, @"\A[A-Za-z0-9]([A-Za-z0-9-]*[A-Za-z0-9])?\z"));
    static bool ValidEmail(string text)
    {
        if (text.Length > 254 || text.Any(x => x > 127 || Char.IsControl(x)) || !MailAddress.TryCreate(text, out var address)
            || address.Address != text || address.User.Length > 64)
            return false;
        if (!address.User.StartsWith('"') && !Match(address.User, @"\A[A-Za-z0-9!#$%&'*+/=?^_`{|}~-]+(\.[A-Za-z0-9!#$%&'*+/=?^_`{|}~-]+)*\z"))
            return false;
        if (address.Host.StartsWith('[') && address.Host.EndsWith(']'))
        {
            var literal = address.Host[1..^1];
            return literal.StartsWith("IPv6:", StringComparison.OrdinalIgnoreCase)
                ? IsValid("ipv6", literal[5..]) : IsValid("ipv4", literal);
        }
        return ValidHostname(address.Host);
    }
}
