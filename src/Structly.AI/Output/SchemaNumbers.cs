using System.Globalization;

namespace Structly.AI;

static class SchemaNumbers
{
    public static bool IsInteger(string json)
    {
        var number = Normalize(json);
        return number.Sign == 0 || number.Magnitude >= number.Digits.Length;
    }

    // Compare the original JSON number against the emitted bound, without rounding
    // through a CLR numeric type. In particular -1e-30 must not become decimal zero.
    public static int Compare(string json, double bound)
    {
        var left = Normalize(json);
        var right = Normalize(bound.ToString("R", CultureInfo.InvariantCulture));
        if(left.Sign != right.Sign)
            return left.Sign.CompareTo(right.Sign);

        if(left.Sign == 0)
            return 0;

        if(left.Magnitude != right.Magnitude)
            return left.Sign * left.Magnitude.CompareTo(right.Magnitude);

        for(var i = 0; i < Math.Max(left.Digits.Length, right.Digits.Length); i++)
        {
            var a = i < left.Digits.Length ? left.Digits[i] : '0';
            var b = i < right.Digits.Length ? right.Digits[i] : '0';
            if(a != b)
                return left.Sign * a.CompareTo(b);
        }

        return 0;
    }

    static Number Normalize(string text)
    {
        var sign = text[0] == '-' ? -1 : 1;
        var offset = text[0] is '-' or '+' ? 1 : 0;
        var index = text.IndexOfAny(['e', 'E']);
        long exponent = 0;
        if(index >= 0 && !Int64.TryParse(text.AsSpan(index + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out exponent))
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
            Digits = digits.TrimEnd('0'),
        };
    }

    sealed record Number
    {
        public required int Sign { get; init; }
        public required long Magnitude { get; init; }
        public required string Digits { get; init; }
    }
}
