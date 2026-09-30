namespace EsilvaSoft.KapibaraStudio.UnitTests.Language.Cases;

/// <summary>
/// Parser of the independent <c>.case</c> corpus. It deliberately knows only the file contract: product behaviour
/// is asserted by the corpus tests, so no fixture expectation is derived from the implementation under test.
/// </summary>
public sealed class LanguageCaseFixture
{
    private static readonly HashSet<string> HeaderKeys = new(StringComparer.Ordinal)
    {
        "case", "mode", "target", "connected", "metadata", "schema", "trigger", "eol", "caret", "group", "spec", "note"
    };

    private LanguageCaseFixture(string sourcePath, IReadOnlyDictionary<string, IReadOnlyList<string>> headers,
        IReadOnlyDictionary<string, IReadOnlyList<string>> expectations, string text, int caret)
    {
        SourcePath = sourcePath;
        Headers = headers;
        Expectations = expectations;
        Text = text;
        Caret = caret;
    }

    public string SourcePath { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Headers { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Expectations { get; }
    public string Text { get; }
    public int Caret { get; }
    public string Id => Header("case");
    public string Mode => Header("mode");
    public string Target => Header("target");

    public static IReadOnlyList<LanguageCaseFixture> LoadAll()
    {
        var cases = EmbeddedTestData.Paths
            .Where(path => path.StartsWith("Language/Cases/", StringComparison.Ordinal) && path.EndsWith(".case", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .Select(path => Parse(path, EmbeddedTestData.ReadText(path)))
            .ToArray();
        if (cases.Length == 0) throw new InvalidDataException("Corpus de fixtures não embutido no assembly de testes.");
        return cases;
    }

    public static LanguageCaseFixture Parse(string path, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(content);
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
        // A physical final newline terminates the last line; it is not a blank line in the logical fixture format.
        if (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        var headers = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var firstBodyLine = 0;
        while (firstBodyLine < lines.Count && TryDirective(lines[firstBodyLine], out var key, out var value) && HeaderKeys.Contains(key))
        {
            Add(headers, key, value);
            firstBodyLine++;
        }

        var firstExpectationLine = lines.Count;
        while (firstExpectationLine > firstBodyLine && TryExpectation(lines[firstExpectationLine - 1], out _, out _)) firstExpectationLine--;
        var expectations = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        for (var index = firstExpectationLine; index < lines.Count; index++)
        {
            if (!TryExpectation(lines[index], out var key, out var value))
                throw new InvalidDataException($"Expectativa inválida em {path}, linha {index + 1}.");
            Add(expectations, key, value);
        }

        ValidateHeaders(path, headers);
        var body = ExpandFill(path, lines.GetRange(firstBodyLine, firstExpectationLine - firstBodyLine));
        var eol = headers.TryGetValue("eol", out var eolValues) ? Single(path, "eol", eolValues) : "LF";
        if (eol == "CRLF") body = body.Replace("\n", "\r\n", StringComparison.Ordinal);
        else if (eol != "LF") throw new InvalidDataException($"eol inválido em {path}: {eol}.");

        var marker = headers.TryGetValue("caret", out var markerValues) ? Single(path, "caret", markerValues) : "|";
        if (marker.Length != 1) throw new InvalidDataException($"O marcador de cursor deve ter um caractere em {path}.");
        var caret = body.IndexOf(marker, StringComparison.Ordinal);
        if (caret < 0 || body.IndexOf(marker, caret + marker.Length, StringComparison.Ordinal) >= 0)
            throw new InvalidDataException($"O corpo de {path} deve conter exatamente um marcador de cursor.");
        body = body.Remove(caret, marker.Length);

        return new(path, Freeze(headers), Freeze(expectations), body, caret);
    }

    public bool HasExpectation(string key) => Expectations.ContainsKey(key);
    public string Expectation(string key) => Single(SourcePath, "expect." + key, Expectations.GetValueOrDefault(key) ?? []);
    public IReadOnlyList<string> ExpectationValues(string key) => Expectations.GetValueOrDefault(key) ?? [];
    public string Header(string key) => Single(SourcePath, key, Headers.GetValueOrDefault(key) ?? []);
    public IReadOnlyList<string> HeaderValues(string key) => Headers.GetValueOrDefault(key) ?? [];
    public override string ToString() => Id;

    private static void ValidateHeaders(string path, Dictionary<string, List<string>> headers)
    {
        foreach (var required in new[] { "case", "mode", "target" })
            _ = Single(path, required, headers.GetValueOrDefault(required) ?? []);
        var fileName = Path.GetFileNameWithoutExtension(path);
        if (!string.Equals(Single(path, "case", headers["case"]), fileName, StringComparison.Ordinal))
            throw new InvalidDataException($"O cabeçalho case de {path} deve corresponder ao nome do arquivo.");
        var mode = Single(path, "mode", headers["mode"]);
        if (mode is not ("Console" or "Script" or "Agregação")) throw new InvalidDataException($"Modo inválido em {path}: {mode}.");
    }

    private static string ExpandFill(string path, List<string> lines)
    {
        var expanded = new List<string>(lines.Count);
        foreach (var line in lines)
        {
            const string fillPrefix = "// @fill: ";
            if (!line.StartsWith(fillPrefix, StringComparison.Ordinal)) { expanded.Add(line); continue; }
            if (!int.TryParse(line[fillPrefix.Length..], out var requested) || requested < 0)
                throw new InvalidDataException($"@fill inválido em {path}: {line}.");
            var copies = (requested + 39) / 40;
            for (var index = 0; index < copies; index++) expanded.Add("db.Pedidos.find({ total: { $gt: 0 } });");
        }
        var body = string.Join("\n", expanded);
        if (body.Length == 0) throw new InvalidDataException($"O corpo de {path} não pode ser vazio.");
        return body;
    }

    private static bool TryDirective(string line, out string key, out string value)
    {
        key = value = "";
        if (!line.StartsWith("// ", StringComparison.Ordinal)) return false;
        var separator = line.IndexOf(": ", 3, StringComparison.Ordinal);
        if (separator < 0) return false;
        key = line[3..separator];
        value = line[(separator + 2)..];
        return key.Length > 0;
    }

    private static bool TryExpectation(string line, out string key, out string value)
    {
        key = value = "";
        const string prefix = "// expect.";
        if (!line.StartsWith(prefix, StringComparison.Ordinal)) return false;
        var separator = line.IndexOf(": ", prefix.Length, StringComparison.Ordinal);
        if (separator < 0) return false;
        key = line[prefix.Length..separator];
        value = line[(separator + 2)..];
        return key.Length > 0;
    }

    private static void Add(Dictionary<string, List<string>> values, string key, string value)
    {
        if (!values.TryGetValue(key, out var list)) { list = []; values.Add(key, list); }
        list.Add(value);
    }

    private static Dictionary<string, IReadOnlyList<string>> Freeze(Dictionary<string, List<string>> values) =>
        values.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<string>)pair.Value.AsReadOnly(), StringComparer.Ordinal);

    private static string Single(string path, string key, IReadOnlyList<string> values)
    {
        if (values.Count != 1) throw new InvalidDataException($"{key} deve aparecer exatamente uma vez em {path}.");
        return values[0];
    }
}
