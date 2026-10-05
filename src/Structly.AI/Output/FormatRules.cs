using System.Globalization;
using System.Net;
using System.Net.Mail;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace Structly.AI;

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
                _ => false,
            };
        }
        catch(Exception exception) when(exception is FormatException or OverflowException or RegexMatchTimeoutException)
        {
            return false;
        }
    }

    static bool Match(string text, string pattern) => Regex.IsMatch(text, pattern, RegexOptions.CultureInvariant, _regexTimeout);
    static bool ValidHostname(string text)
        => text.Length is > 0 and <= 253 && (text.EndsWith('.') ? text[..^1] : text).Split('.').All(x => x.Length is > 0 and <= 63 && Match(x, @"\A[A-Za-z0-9]([A-Za-z0-9-]*[A-Za-z0-9])?\z"));
    static bool ValidEmail(string text)
    {
        if(text.Length > 254 || text.Any(x => x > 127 || Char.IsControl(x)) || !MailAddress.TryCreate(text, out var address)
            || address.Address != text || address.User.Length > 64)
            return false;

        if(!address.User.StartsWith('"') && !Match(address.User, @"\A[A-Za-z0-9!#$%&'*+/=?^_`{|}~-]+(\.[A-Za-z0-9!#$%&'*+/=?^_`{|}~-]+)*\z"))
            return false;

        if(address.Host.StartsWith('[') && address.Host.EndsWith(']'))
        {
            var literal = address.Host[1..^1];
            return literal.StartsWith("IPv6:", StringComparison.OrdinalIgnoreCase)
                ? IsValid("ipv6", literal[5..]) : IsValid("ipv4", literal);
        }

        return ValidHostname(address.Host);
    }
}
