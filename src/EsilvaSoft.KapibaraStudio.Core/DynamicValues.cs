using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EsilvaSoft.KapibaraStudio.Core;

public static class DynamicValues
{
    private static readonly Regex Call = new("""ENV\.get\(\s*(?<quote>["'])(?<key>[^"'\r\n]+)\k<quote>\s*\)""", RegexOptions.CultureInvariant);
    private static readonly Regex Template = new("""\$\{ENV\.get\(\s*(?<quote>["'])(?<key>[^"'\r\n]+)\k<quote>\s*\)\}""", RegexOptions.CultureInvariant);

    public static string ResolveText(string text, Func<string, string> get, bool uriEncode = false) =>
        Template.Replace(text, m => uriEncode ? Uri.EscapeDataString(get(m.Groups["key"].Value)) : get(m.Groups["key"].Value));

    /// <summary>Replaces standalone calls with JSON string literals, leaving quoted text and BSON types intact.</summary>
    public static string ResolveJson(string text, Func<string, string> get)
    {
        var result = new StringBuilder();
        for (var i = 0; i < text.Length;)
        {
            if (text[i] is '"' or '\'')
            {
                var quote = text[i];
                result.Append(text[i++]);
                while (i < text.Length)
                {
                    var ch = text[i++]; result.Append(ch);
                    if (ch == '\\' && i < text.Length) result.Append(text[i++]);
                    else if (ch == quote) break;
                }
                continue;
            }
            var match = Call.Match(text, i);
            if (match.Success && match.Index == i && (i == 0 || !(char.IsLetterOrDigit(text[i - 1]) || text[i - 1] is '_' or '.')))
            {
                result.Append(JsonSerializer.Serialize(get(match.Groups["key"].Value)));
                i += match.Length;
            }
            else result.Append(text[i++]);
        }
        return result.ToString();
    }
}
