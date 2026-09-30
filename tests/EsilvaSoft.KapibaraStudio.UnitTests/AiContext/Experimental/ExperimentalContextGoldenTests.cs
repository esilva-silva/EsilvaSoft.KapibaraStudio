using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using EsilvaSoft.KapibaraStudio.Application.AiContext.Experimental;

namespace EsilvaSoft.KapibaraStudio.UnitTests.AiContext.Experimental;

/// <summary>
/// Congela os quatro formatos experimentais do lote A34b, um golden por formato.
/// </summary>
/// <remarks>
/// <para>
/// <b>Golden simplificado, e por quê.</b> O golden do v1 precisa registrar a origem de cada quebra de linha
/// (<c>H</c>/<c>L</c>) e dois SHA porque aquele contrato mistura <see cref="Environment.NewLine"/> com <c>"\n"</c>
/// literal. Os formatos deste lote nasceram escrevendo só <c>"\n"</c>, então a saída é a mesma em qualquer host e um
/// único SHA por artefato basta. O restante da disciplina de A31a continua: UTF-8 sem BOM, comparação
/// <see cref="StringComparison.Ordinal"/>, um caso sob <c>tr-TR</c> e captura explícita com inspeção manual.
/// </para>
/// <para>
/// O corpus é o mesmo para os quatro formatos, de modo que cada golden é diretamente comparável com os outros.
/// </para>
/// </remarks>
[TestFixture]
public sealed class ExperimentalContextGoldenTests
{
    public static IEnumerable<TestCaseData> Combinations() =>
        from contract in ExperimentalContextContracts.All
        from @case in ExperimentalContextCorpus.Cases
        // Sem SetName: a mesma fonte alimenta vários métodos, e um nome fixo faria todos aparecerem com o mesmo
        // rótulo no relatório. O nome padrão já inclui método + argumentos.
        select new TestCaseData(contract.ContractId, @case.Id);

    public static IEnumerable<string> ContractIds() => ExperimentalContextContracts.All.Select(contract => contract.ContractId);

    [TestCaseSource(nameof(ContractIds))]
    public void GoldenCoversExactlyTheCorpus(string contractId)
        => Assert.That(Load(contractId).Keys, Is.EquivalentTo(ExperimentalContextCorpus.Cases.Select(@case => @case.Id)));

    [TestCaseSource(nameof(Combinations))]
    public void ArtifactsMatchTheFrozenFormat(string contractId, string caseId) => AssertMatchesGolden(contractId, caseId);

    /// <summary>Um formato que dependesse da cultura corrente não poderia ser congelado; <c>tr-TR</c> é o caso limite usual.</summary>
    [TestCaseSource(nameof(ContractIds)), SetCulture("tr-TR"), SetUICulture("tr-TR")]
    public void ArtifactsAreCultureInvariant(string contractId)
    {
        foreach (var @case in ExperimentalContextCorpus.Cases) AssertMatchesGolden(contractId, @case.Id);
    }

    /// <summary>Determinismo: a mesma entrada, serializada duas vezes, produz os mesmos bytes.</summary>
    [TestCaseSource(nameof(Combinations))]
    public void SerializationIsRepeatable(string contractId, string caseId)
    {
        var contract = Contract(contractId);
        var @case = Case(caseId);
        var first = contract.Build(@case.Resolve(), @case.Settings);
        var second = contract.Build(@case.Resolve(), @case.Settings);
        Assert.Multiple(() =>
        {
            Assert.That(second.Context, Is.EqualTo(first.Context).Using(StringComparer.Ordinal));
            Assert.That(second.Prefix, Is.EqualTo(first.Prefix).Using(StringComparer.Ordinal));
            Assert.That(second.Suffix, Is.EqualTo(first.Suffix).Using(StringComparer.Ordinal));
        });
    }

    /// <summary>Nenhum formato deste lote pode herdar <see cref="Environment.NewLine"/>: só <c>"\n"</c> literal.</summary>
    [TestCaseSource(nameof(Combinations))]
    public void ContextNeverContainsCarriageReturn(string contractId, string caseId)
    {
        var @case = Case(caseId);
        Assert.That(Contract(contractId).Build(@case.Resolve(), @case.Settings).Context, Does.Not.Contain("\r"));
    }

    /// <summary>Os dois caminhos de <c>Build</c> (aba capturada e entrada direta) precisam coincidir byte a byte.</summary>
    [TestCaseSource(nameof(Combinations))]
    public void SnapshotPathMatchesTheDirectPath(string contractId, string caseId)
    {
        var @case = Case(caseId);
        if (@case.Snapshot is null) Assert.Ignore("Caso montado à mão; não existe aba equivalente.");
        var contract = Contract(contractId);
        Assert.That(contract.Build(@case.Snapshot!, @case.Settings).Context,
            Is.EqualTo(contract.Build(@case.Resolve(), @case.Settings).Context).Using(StringComparer.Ordinal));
    }

    [TestCaseSource(nameof(ContractIds))]
    public void GoldenFileHasNoByteOrderMark(string contractId)
    {
        var bytes = EmbeddedTestData.ReadBytes(GoldenResourcePath(contractId));
        var bom = new UTF8Encoding(true).GetPreamble();
        Assert.That(bytes.Take(bom.Length).ToArray(), Is.Not.EqualTo(bom), "O golden não pode ter BOM.");
    }

    private static void AssertMatchesGolden(string contractId, string caseId)
    {
        var expected = Load(contractId);
        Assert.That(expected.TryGetValue(caseId, out var golden), Is.True, "Caso ausente no golden: " + caseId);
        var @case = Case(caseId);
        var request = Contract(contractId).Build(@case.Resolve(), @case.Settings);
        Assert.Multiple(() =>
        {
            Assert.That(Escape(request.Context), Is.EqualTo(golden!.Context).Using(StringComparer.Ordinal),
                $"{contractId}/{caseId}: contexto divergente do golden.");
            Assert.That(Sha256(request.Context), Is.EqualTo(golden.Sha), $"{contractId}/{caseId}: sha do contexto divergente.");
            Assert.That(Escape(request.Prefix), Is.EqualTo(golden.Prefix).Using(StringComparer.Ordinal), $"{contractId}/{caseId}: prefixo divergente.");
            Assert.That(Escape(request.Suffix), Is.EqualTo(golden.Suffix).Using(StringComparer.Ordinal), $"{contractId}/{caseId}: sufixo divergente.");
            Assert.That(Escape(string.Join("|", request.Dictionary)), Is.EqualTo(golden.Dictionary).Using(StringComparer.Ordinal),
                $"{contractId}/{caseId}: dicionário divergente.");
        });
    }

    private static ExperimentalContextContract Contract(string contractId)
        => ExperimentalContextContracts.All.Single(contract => contract.ContractId == contractId);

    private static ExperimentalContextCase Case(string caseId)
        => ExperimentalContextCorpus.Cases.Single(@case => @case.Id == caseId);

    // --- Golden file ------------------------------------------------------------------------------------------
    private sealed record GoldenCase(string Context, string Sha, string Prefix, string Suffix, string Dictionary);

    private static readonly Dictionary<string, IReadOnlyDictionary<string, GoldenCase>> Cache = new(StringComparer.Ordinal);

    private static IReadOnlyDictionary<string, GoldenCase> Load(string contractId)
    {
        lock (Cache)
        {
            if (!Cache.TryGetValue(contractId, out var parsed))
            {
                parsed = Parse(EmbeddedTestData.ReadText(GoldenResourcePath(contractId)));
                Cache[contractId] = parsed;
            }

            return parsed;
        }
    }

    private static string GoldenResourcePath(string contractId)
        => "AiContext/Experimental/Golden/" + contractId + ".golden";

    /// <summary>
    /// Escapa para uma única linha lógica: a quebra de linha real vira o marcador <c>\n</c> (o arquivo é
    /// <c>text eol=lf</c>, então um <c>\r</c> só pode vir do conteúdo e é escapado como tal).
    /// </summary>
    private static string Escape(string raw)
    {
        var builder = new StringBuilder(raw.Length);
        foreach (var character in raw)
            builder.Append(character switch
            {
                '\\' => "\\\\",
                '\r' => "\\r",
                '\n' => "\\n",
                _ => char.IsSurrogate(character)
                    ? "\\u" + ((int)character).ToString("x4", CultureInfo.InvariantCulture)
                    : character.ToString(CultureInfo.InvariantCulture)
            });
        return builder.ToString();
    }

    private static string Sha256(string value) => Convert.ToHexStringLower(SHA256.HashData(new UTF8Encoding(false).GetBytes(value)));

    private static Dictionary<string, GoldenCase> Parse(string content)
    {
        var cases = new Dictionary<string, GoldenCase>(StringComparer.Ordinal);
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        string? id = null, context = null, sha = null, prefix = null, suffix = null, dictionary = null;
        foreach (var line in lines)
        {
            if (line.StartsWith("== ", StringComparison.Ordinal))
            {
                Flush(cases, ref id, ref context, ref sha, ref prefix, ref suffix, ref dictionary);
                id = line[3..].TrimEnd(' ', '=');
            }
            else if (line.StartsWith("context=", StringComparison.Ordinal)) context = line["context=".Length..];
            else if (line.StartsWith("sha=", StringComparison.Ordinal)) sha = line["sha=".Length..];
            else if (line.StartsWith("prefix=", StringComparison.Ordinal)) prefix = line["prefix=".Length..];
            else if (line.StartsWith("suffix=", StringComparison.Ordinal)) suffix = line["suffix=".Length..];
            else if (line.StartsWith("dictionary=", StringComparison.Ordinal)) dictionary = line["dictionary=".Length..];
        }

        Flush(cases, ref id, ref context, ref sha, ref prefix, ref suffix, ref dictionary);
        return cases;
    }

    private static void Flush(Dictionary<string, GoldenCase> cases, ref string? id, ref string? context, ref string? sha,
        ref string? prefix, ref string? suffix, ref string? dictionary)
    {
        if (id is null) return;
        // Os campos vêm por ref porque o laço de Parse os zera a cada caso; copiá-los antes de asserir mantém as
        // asserções fora de qualquer closure sobre parâmetro ref (CS1628).
        var (currentId, currentContext, currentSha) = (id, context, sha);
        var (currentPrefix, currentSuffix, currentDictionary) = (prefix, suffix, dictionary);
        Assert.Multiple(() =>
        {
            Assert.That(currentContext, Is.Not.Null, currentId + ": context ausente.");
            Assert.That(currentSha, Is.Not.Null, currentId + ": sha ausente.");
            Assert.That(currentPrefix, Is.Not.Null, currentId + ": prefix ausente.");
            Assert.That(currentSuffix, Is.Not.Null, currentId + ": suffix ausente.");
            Assert.That(currentDictionary, Is.Not.Null, currentId + ": dictionary ausente.");
        });
        cases[id] = new(context!, sha!, prefix!, suffix!, dictionary!);
        id = context = sha = prefix = suffix = dictionary = null;
    }

}
