using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests.Ai;

/// <summary>
/// Freezes the <c>editor-context-v1</c> contract consumed by SlopCoder training packages:
/// <see cref="AutocompleteContextBuilder.Build"/> and <see cref="AutocompleteContextBuilder.ModelPrefix"/>.
/// <para>
/// <see cref="AutocompleteContextBuilder.Build"/> joins header lines with the frozen CRLF contract, while
/// <see cref="AutocompleteContextBuilder.ModelPrefix"/> wraps that context with a bare <c>"\n"</c> literal. A golden
/// that normalizes <c>\r\n</c> to <c>\n</c> before comparing would hide exactly the regression this file exists to
/// catch (for example swapping the contract CRLF for <c>Append("\n")</c>). Every artifact below is therefore stored
/// as three parts: an LF-placeholder body (<c>text</c>), the origin of every placeholder in order (<c>eol</c>,
/// <c>H</c> = the frozen CRLF contract, <c>L</c> = the bare <c>"\n"</c> literal in <c>ModelPrefix</c>), and
/// two SHA-256 hashes of the two possible byte-exact materializations (all-LF host, all-CRLF host). Both hashes are
/// verified on every platform; the live output on the current host is verified byte-for-byte against whichever
/// materialization matches the frozen CRLF/LF composition.
/// </para>
/// Never regenerate this golden to hide a regression; a legitimate contract change needs explicit justification
/// and, per <c>agents/architecture-agent.md</c>, sign-off from Architecture before consumers change.
/// </summary>
[TestFixture]
public sealed class EditorContextV1GoldenTests
{
    /// <summary>Every branch the corpus mínimo in the task must exercise at least once.</summary>
    public static readonly IReadOnlyList<string> RequiredTags =
    [
        "lang:json", "lang:mongosh", "lang:console", "lang:null",
        "opt:editorContext:on", "opt:editorContext:off",
        "opt:resultPanel:on", "opt:resultPanel:off",
        "opt:inputPanel:on", "opt:inputPanel:off",
        "names:empty", "names:over-limit", "names:sensitive", "names:pipe-token", "names:reserved-token",
        "fields:empty", "fields:nonempty",
        "input:empty", "input:safe", "input:sensitive", "input:overlong",
        "recent:0", "recent:3", "recent:5", "recent:overlong",
        "window:caret-zero", "window:caret-end", "window:text-larger-than-window", "window:mixed-eol", "window:surrogate-split",
        "context:over-8192",
        "modelprefix:nontraining", "modelprefix:training", "modelprefix:contains-star-slash", "modelprefix:commands-last-line-no-break",
        "fixture:predictive", "fixture:local",
    ];

    private static readonly string[] ArtifactNames = ["context", "modelPrefix.false", "modelPrefix.true"];

    private static readonly Lazy<IReadOnlyDictionary<string, GoldenCase>> Golden = new(() => Parse(ReadGoldenFile()));

    public static IEnumerable<string> CaseIds() => EditorContextV1Corpus.Cases.Select(c => c.Id);

    [Test]
    public void CorpusCoversEveryBranch()
    {
        var found = EditorContextV1Corpus.Cases.SelectMany(c => c.Tags).ToHashSet(StringComparer.Ordinal);
        var missing = RequiredTags.Where(tag => !found.Contains(tag)).ToArray();
        Assert.That(missing, Is.Empty, "Tags sem nenhum caso no corpus: " + string.Join(", ", missing));
    }

    [Test]
    public void GoldenCoversExactlyTheCorpus() => Assert.That(Golden.Value.Keys, Is.EquivalentTo(CaseIds()));

    [TestCaseSource(nameof(CaseIds))]
    public void ArtifactsMatchTheFrozenContract(string id) => AssertCaseMatchesGolden(id);

    [Test, SetCulture("tr-TR"), SetUICulture("tr-TR")]
    public void ArtifactsAreCultureInvariant()
    {
        foreach (var id in CaseIds()) AssertCaseMatchesGolden(id);
    }

    [Test]
    public void GoldenFileHasNoByteOrderMark()
    {
        var bytes = EmbeddedTestData.ReadBytes(GoldenResourcePath);
        var bom = new UTF8Encoding(true).GetPreamble();
        Assert.That(bytes.Take(bom.Length).ToArray(), Is.Not.EqualTo(bom), "O golden não pode ter BOM (UTF8Encoding(false) na leitura/escrita).");
    }

    private static void AssertCaseMatchesGolden(string id)
    {
        Assert.That(Golden.Value.TryGetValue(id, out var expected), Is.True, "Caso ausente no golden: " + id);
        var @case = EditorContextV1Corpus.Cases.Single(c => c.Id == id);
        var request = @case.Resolve();
        var live = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["context"] = request.Context,
            ["modelPrefix.false"] = AutocompleteContextBuilder.ModelPrefix(request, false),
            ["modelPrefix.true"] = AutocompleteContextBuilder.ModelPrefix(request, true),
        };
        foreach (var name in ArtifactNames)
        {
            var golden = expected!.Artifacts[name];
            var raw = live[name];
            // modelPrefix.* always end with request.Prefix, byte for byte and unmodified; everything before that
            // point is the wrap + context text that mixes the frozen CRLF header with the literal "\n" from
            // ModelPrefix, so only that head is subject to the H/L reconstruction below.
            string head;
            if (name == "context") head = raw;
            else
            {
                head = raw[..(raw.Length - request.Prefix.Length)];
                var tail = raw[(raw.Length - request.Prefix.Length)..];
                Assert.That(tail, Is.EqualTo(request.Prefix), $"{id}/{name}: o sufixo deveria ser o Prefix inalterado.");
            }
            var (observedText, observedEol) = Decompose(head);
            Assert.That(observedText, Is.EqualTo(golden.Text), $"{id}/{name}: conteúdo divergente do golden.");
            Assert.That(observedEol, Is.EqualTo(golden.Eol), $"{id}/{name}: origem dos terminadores (H=CRLF do contrato, L=literal \\n) divergente.");
            var reconstructedLf = Recompose(golden.Text, golden.Eol, "\n");
            var reconstructedCrLf = Recompose(golden.Text, golden.Eol, "\r\n");
            Assert.That(Sha256(reconstructedLf), Is.EqualTo(golden.ShaLf), $"{id}/{name}: sha.lf não reconstrói a partir de text+eol.");
            Assert.That(Sha256(reconstructedCrLf), Is.EqualTo(golden.ShaCrLf), $"{id}/{name}: sha.crlf não reconstrói a partir de text+eol.");
            Assert.That(head, Is.EqualTo(reconstructedCrLf), $"{id}/{name}: saída ao vivo diverge da materialização CRLF do contrato.");
        }
        Assert.That(EscapeInline(request.Prefix), Is.EqualTo(expected!.Prefix), $"{id}: Prefix divergente do golden.");
        Assert.That(EscapeInline(request.Suffix), Is.EqualTo(expected!.Suffix), $"{id}: Suffix divergente do golden.");
        Assert.That(EscapeInline(string.Join("", request.Dictionary)), Is.EqualTo(expected!.Dictionary), $"{id}: Dictionary divergente do golden.");
    }

    // --- Escaping -----------------------------------------------------------------------------------------------
    // Block text keeps a literal '\n' as the neutral placeholder for every builder-inserted break (its origin is
    // recorded out of band, in "eol"); any other backslash, stray '\r' or lone surrogate is escaped by Decompose so
    // the golden stays representable as clean UTF-8 without a BOM and unambiguous under `text eol=lf` normalization.
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

    // --- Golden file format ---------------------------------------------------------------------------------------
    private sealed record ArtifactGolden(string Text, string Eol, string ShaLf, string ShaCrLf);
    private sealed record GoldenCase(string Prefix, string Suffix, string Dictionary, IReadOnlyDictionary<string, ArtifactGolden> Artifacts);

    private const string GoldenResourcePath = "Ai/Golden/editor-context-v1.golden";

    private static string ReadGoldenFile() => EmbeddedTestData.ReadText(GoldenResourcePath);

    private static Dictionary<string, GoldenCase> Parse(string content)
    {
        var cases = new Dictionary<string, GoldenCase>(StringComparer.Ordinal);
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var i = 0;
        while (i < lines.Length)
        {
            var line = lines[i];
            if (!line.StartsWith("== ", StringComparison.Ordinal)) { i++; continue; }
            var id = line[3..].TrimEnd(' ', '=');
            i++;
            var artifacts = new Dictionary<string, ArtifactGolden>(StringComparer.Ordinal);
            string? prefix = null, suffix = null, dictionary = null;
            while (i < lines.Length && !lines[i].StartsWith("== ", StringComparison.Ordinal))
            {
                if (lines[i].StartsWith('[') && !lines[i].StartsWith("[/", StringComparison.Ordinal))
                {
                    var name = lines[i][1..^1];
                    i++;
                    var body = new StringBuilder();
                    var first = true;
                    while (i < lines.Length && lines[i] != "[/" + name + "]")
                    {
                        if (!first) body.Append('\n');
                        body.Append(lines[i]);
                        first = false;
                        i++;
                    }
                    i++; // skip closing marker
                    var eol = lines[i][(name + ".eol=").Length..]; i++;
                    var shaLf = lines[i][(name + ".sha.lf=").Length..]; i++;
                    var shaCrLf = lines[i][(name + ".sha.crlf=").Length..]; i++;
                    artifacts[name] = new ArtifactGolden(body.ToString(), eol, shaLf, shaCrLf);
                }
                else if (lines[i].StartsWith("prefix=", StringComparison.Ordinal)) { prefix = lines[i]["prefix=".Length..]; i++; }
                else if (lines[i].StartsWith("suffix=", StringComparison.Ordinal)) { suffix = lines[i]["suffix=".Length..]; i++; }
                else if (lines[i].StartsWith("dictionary=", StringComparison.Ordinal)) { dictionary = lines[i]["dictionary=".Length..]; i++; }
                else i++;
            }
            Assert.That(prefix, Is.Not.Null, id + ": prefix ausente."); Assert.That(suffix, Is.Not.Null, id + ": suffix ausente.");
            Assert.That(dictionary, Is.Not.Null, id + ": dictionary ausente.");
            cases[id] = new GoldenCase(prefix!, suffix!, dictionary!, artifacts);
        }
        return cases;
    }

}
