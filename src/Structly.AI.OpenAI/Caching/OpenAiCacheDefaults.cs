using System.Text.RegularExpressions;

namespace Structly.AI.OpenAI;

static class OpenAiCacheDefaults
{
    static readonly Regex _modelPattern = new(@"\Agpt-(?<major>[0-9]+)(?:\.[0-9]+)*(?:-[a-z0-9]+)*\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public static bool TryGetCompatibility(string model, out OpenAiCacheCompatibility compatibility)
    {
        compatibility = OpenAiCacheCompatibility.Modern;
        var match = _modelPattern.Match(model);
        return match.Success && Int32.TryParse(match.Groups["major"].Value, out var major) && major >= 6;
    }
}
