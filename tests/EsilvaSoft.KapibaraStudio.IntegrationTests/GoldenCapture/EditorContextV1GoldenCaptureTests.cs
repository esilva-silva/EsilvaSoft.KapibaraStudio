using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.UnitTests.Ai;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests.GoldenCapture;

[TestFixture]
[Category("Integration")]
public sealed class EditorContextV1GoldenCaptureTests
{
    /// <summary>
    /// Captures the golden from the current implementation. Its output must be inspected by hand before committing.
    /// Never rerun to make a failing
    /// <c>ArtifactsMatchTheFrozenContract</c> pass without an explicit, reviewed justification for the behavior
    /// change — see the discipline documented on <c>HighlightingGoldenTests.CaptureGolden</c>.
    /// </summary>
    [Test, Explicit("Captura o golden de editor-context-v1; requer inspeção manual do diff antes do commit."), Category("GoldenCapture")]
    public void CaptureGolden()
    {
        var path = Environment.GetEnvironmentVariable("SLOP_EDITOR_CONTEXT_GOLDEN_OUT");
        if (string.IsNullOrWhiteSpace(path)) Assert.Ignore("Defina SLOP_EDITOR_CONTEXT_GOLDEN_OUT.");
        var builder = new StringBuilder(
            "# Golden do contrato editor-context-v1 (AutocompleteContextBuilder.Build/ModelPrefix). Capturado no lote A31a. Não regenerar para esconder regressão.\n");
        foreach (var @case in EditorContextV1Corpus.Cases)
        {
            var request = @case.Resolve();
            builder.Append("== ").Append(@case.Id).Append(" ==\n");
            var live = new (string Name, string Raw)[]
            {
                ("context", request.Context),
                ("modelPrefix.false", AutocompleteContextBuilder.ModelPrefix(request, false)),
                ("modelPrefix.true", AutocompleteContextBuilder.ModelPrefix(request, true)),
            };
            foreach (var (name, raw) in live)
            {
                var head = name == "context" ? raw : raw[..(raw.Length - request.Prefix.Length)];
                var (text, eol) = Decompose(head);
                var reconstructedLf = Recompose(text, eol, "\n");
                var reconstructedCrLf = Recompose(text, eol, "\r\n");
                builder.Append('[').Append(name).Append("]\n");
                // Each segment of the escaped body (split on the '\n' placeholder) becomes its own physical file
                // line; this is exactly the inverse of how EditorContextV1GoldenTests.Parse rejoins them, so a
                // body that ends with a placeholder (Build's Context always does) round-trips without inventing or
                // swallowing a trailing blank line.
                foreach (var segment in text.Split('\n')) builder.Append(segment).Append('\n');
                builder.Append("[/").Append(name).Append("]\n");
                builder.Append(name).Append(".eol=").Append(eol).Append('\n');
                builder.Append(name).Append(".sha.lf=").Append(Sha256(reconstructedLf)).Append('\n');
                builder.Append(name).Append(".sha.crlf=").Append(Sha256(reconstructedCrLf)).Append('\n');
            }
            builder.Append("prefix=").Append(EscapeInline(request.Prefix)).Append('\n');
            builder.Append("suffix=").Append(EscapeInline(request.Suffix)).Append('\n');
            builder.Append("dictionary=").Append(EscapeInline(string.Join("", request.Dictionary))).Append('\n');
        }
        File.WriteAllText(path, builder.ToString(), new UTF8Encoding(false));
    }

    private static string EscapeInline(string raw)
    {
        var sb = new StringBuilder(raw.Length);
        foreach (var c in raw)
        {
            if (c == '\\') sb.Append("\\\\");
            else if (c == '\r') sb.Append("\\r");
            else if (c == '\n') sb.Append("\\n");
            else if (char.IsSurrogate(c)) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
            else sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Splits <paramref name="raw"/> into an LF-placeholder body plus the per-break origin: <c>H</c> when the break
    /// is exactly the contract's <c>"\r\n"</c>, <c>L</c> for a bare <c>"\n"</c>. This sniffing is only trustworthy
    /// because no header field value in the corpus embeds a raw newline of its own (checked implicitly: any
    /// content-level newline would corrupt the H/L count and fail <c>ArtifactsMatchTheFrozenContract</c>
    /// immediately after capture).
    /// </summary>
    private static (string Text, string Eol) Decompose(string raw)
    {
        var text = new StringBuilder();
        var eol = new StringBuilder();
        for (var i = 0; i < raw.Length; i++)
        {
            var c = raw[i];
            if (c == '\r' && i + 1 < raw.Length && raw[i + 1] == '\n') { text.Append('\n'); eol.Append('H'); i++; }
            else if (c == '\n') { text.Append('\n'); eol.Append('L'); }
            else if (c == '\\') text.Append("\\\\");
            else if (c == '\r') text.Append("\\r");
            else if (char.IsSurrogate(c)) text.Append('\\').Append('u').Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
            else text.Append(c);
        }
        return (text.ToString(), eol.ToString());
    }

    private static string Recompose(string text, string eol, string hostNewLine)
    {
        var raw = new StringBuilder();
        var eolIndex = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\\' && i + 1 < text.Length)
            {
                var next = text[i + 1];
                if (next == '\\') { raw.Append('\\'); i++; continue; }
                if (next == 'r') { raw.Append('\r'); i++; continue; }
                if (next == 'u' && i + 5 < text.Length)
                {
                    var code = int.Parse(text.AsSpan(i + 2, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    raw.Append((char)code); i += 5; continue;
                }
            }
            if (c == '\n') { raw.Append(eolIndex < eol.Length && eol[eolIndex] == 'H' ? hostNewLine : "\n"); eolIndex++; }
            else raw.Append(c);
        }
        return raw.ToString();
    }

    private static string Sha256(string value) => Convert.ToHexStringLower(SHA256.HashData(new UTF8Encoding(false).GetBytes(value)));

}
