namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core;

/// <summary>Minimal structural check of LSP snippet syntax.</summary>
internal static class SnippetSyntax
{
    public static bool IsValid(string body)
    {
        var depth = 0;
        for (var index = 0; index < body.Length; index++)
        {
            var character = body[index];
            if (character == '\\')
            {
                if (++index >= body.Length) return false;
                continue;
            }
            if (character == '}' && depth > 0) { depth--; continue; }
            if (character != '$') continue;
            if (index + 1 >= body.Length) return false;
            if (char.IsAsciiDigit(body[index + 1]))
            {
                while (index + 1 < body.Length && char.IsAsciiDigit(body[index + 1])) index++;
                continue;
            }
            if (body[index + 1] != '{' || index + 2 >= body.Length || !char.IsAsciiDigit(body[index + 2])) return false;
            index += 2;
            while (index + 1 < body.Length && char.IsAsciiDigit(body[index + 1])) index++;
            if (index + 1 >= body.Length) return false;
            switch (body[index + 1])
            {
                case '}': index++; break;
                case ':': index++; depth++; break;
                case '|':
                    var end = body.IndexOf("|}", index + 2, StringComparison.Ordinal);
                    if (end < 0) return false;
                    index = end + 1;
                    break;
                default: return false;
            }
        }
        return depth == 0;
    }
}
