namespace EsilvaSoft.KapibaraStudio.Application;

/// <summary>Accepts identifiers with a following dot, or a short expression through a delimiter/line.</summary>
public static class IncrementalCompletion
{
    public static int NextLength(string text)
    {
        if (text.Length == 0) return 0;
        var i = 0;
        while (i < text.Length && text[i] is ' ' or '\t') i++;
        if (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] is '_' or '$'))
        {
            while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] is '_' or '$')) i++;
            if (i < text.Length && text[i] == '.')
            {
                var next = i + 1;
                while (next < text.Length && (char.IsLetterOrDigit(text[next]) || text[next] is '_' or '$')) next++;
                if (next == text.Length || text[next] != '(') i++;
            }
            return i;
        }
        char quote = '\0';
        for (; i < text.Length; i++)
        {
            var c = text[i];
            if (quote != '\0')
            {
                if (c == '\\') { i++; continue; }
                if (c == quote) quote = '\0';
                continue;
            }
            if (c is '\'' or '"' or '`') { quote = c; continue; }
            if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') return i + 2;
            if (c is '\n' or ';' or ',' or '{' or '}' || i >= 79) return i + 1;
        }
        return text.Length;
    }
}
