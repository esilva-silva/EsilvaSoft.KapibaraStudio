using System.Globalization;
using System.Text;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.AiContext;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;
using EsilvaSoft.KapibaraStudio.UnitTests.Ai;

namespace EsilvaSoft.KapibaraStudio.UnitTests.AiContext;

/// <summary>
/// Proves that routing through <see cref="EditorContextV1Contract"/> produces the very same bytes as calling
/// <see cref="AutocompleteContextBuilder.Build"/> directly, for every case of the frozen corpus in
/// <see cref="EditorContextV1Corpus"/>.
/// <para>
/// The corpus exposes each case as a <c>Func&lt;AutocompleteRequest&gt;</c>, i.e. the already-built output, and not
/// the snapshot/settings pair that produced it — so a dual-path comparison cannot read the inputs back out of it.
/// Instead of forking a second corpus, <see cref="Inputs"/> mirrors the corpus inputs verbatim and
/// <see cref="MirrorReproducesTheCorpusCaseExactly"/> pins that mirror to the corpus output byte for byte; a corpus
/// case that changes, gains or loses an entry fails <see cref="MirrorCoversEveryBuildCase"/> or the pinning test
/// instead of silently comparing something else.
/// </para>
/// </summary>
[TestFixture]
public sealed class EditorContextV1ContractEquivalenceTests
{
    private static readonly string LongTail = new string('x', 90000) + "db.Customers.find({";

    /// <summary>
    /// Corpus cases built by hand as an <see cref="AutocompleteRequest"/> to reach <c>ModelPrefix</c> branches that
    /// <c>Build</c> can never produce; they have no snapshot/settings input, so no builder-vs-contract pair exists.
    /// </summary>
    private static readonly string[] CasesWithoutBuilderInput =
        ["modelprefix-raw-commands-last-line-has-no-terminator", "modelprefix-raw-blank-context-short-circuits"];

    /// <summary>Inputs of every corpus case that goes through <c>Build</c>, copied verbatim from the corpus.</summary>
    private static readonly IReadOnlyList<(string Id, AutocompleteContextSnapshot Snapshot, AutocompleteSettings Settings)> Inputs =
    [
        ("lang-json-baseline", new("", 0, "json"), new()),
        ("lang-mongosh-with-result-fields",
            new("db.customers.find({na", 22, "JavaScript (mongosh)", ResultFields: ["Name", "AccountId"]), new()),
        ("lang-console-production-shape",
            new("db.Customers.find({})", 22, "Mongo Console JavaScript",
                KnownNames: ["Production", "Customers", "Dev"],
                RecentCommands: ["db.Customers.findOne()", "db.Customers.count()", "db.Customers.drop()"]), new()),
        ("lang-null-defaults-to-shared-commands", new("", 0, null!), new()),
        ("names-overflow-takes-first-128",
            new("", 0, "json", KnownNames: Enumerable.Range(1, 129)
                .Select(i => "Field" + i.ToString("000", CultureInfo.InvariantCulture)).ToArray()), new()),
        ("names-filters-sensitive-and-reserved-tokens",
            new("", 0, "json", KnownNames: ["Safe1", "password: hidden", "Weird<|Field", "Weird<｜Field", "Safe2"]), new()),
        ("result-panel-disabled-hides-fields",
            new("", 0, "json", ResultFields: ["Name", "AccountId"]), new() { UseResultPanelContext = false }),
        ("input-panel-disabled-hides-safe-input",
            new("", 0, "json", "{\"Name\":\"Eduardo\"}"), new() { UseInputPanelContext = false }),
        ("input-overlong-cuts-at-1024", new("", 0, "json", new string('i', 2000)), new()),
        ("input-sensitive-excluded",
            new("db.", 3, "javascript", "{\"password\":\"hidden\"}", ResultFields: [], KnownNames: ["Production"],
                RecentCommands: ["const api_key = 'hidden'"]), new()),
        ("recent-five-truncates-to-three-and-cuts-256",
            new("", 0, "json", RecentCommands: [new string('r', 300), "c2", "c3", "c4", "c5"]), new()),
        ("predictive-bounded-enabled",
            new(LongTail, LongTail.Length, "javascript", "{\"Name\":\"Eduardo\"}", ["Name", "AccountId", "Active"],
                ["Production", "Customers"], ["db.Customers.findOne()"]), new()),
        ("predictive-bounded-disabled",
            new(LongTail, LongTail.Length, "javascript", "{\"Name\":\"Eduardo\"}", ["Name", "AccountId", "Active"],
                ["Production", "Customers"], ["db.Customers.findOne()"]),
            new() { UseInputPanelContext = false, UseResultPanelContext = false, UseEditorContext = false }),
        ("window-caret-zero", new("return value;", 0, "json"), new()),
        ("window-mixed-eol-preserves-raw-bytes",
            new("line1\r\nline2\nline3\r\nCURSOR",
                "line1\r\nline2\nline3\r\nCURSOR".IndexOf("CURSOR", StringComparison.Ordinal), "json"), new()),
        ("window-surrogate-pair-split-by-char-index", new("abc" + '\uD83D' + '\uDE00' + "def", 4, "json"), new()),
        ("context-exceeds-8192-gets-bounded",
            new("", 0, "json", KnownNames: Enumerable.Range(0, 128)
                .Select(i => "Name_" + i.ToString("0000", CultureInfo.InvariantCulture) + new string('a', 70)).ToArray()), new()),
        ("modelprefix-neutralizes-star-slash",
            new("", 0, "JavaScript (mongosh)", "check */ neutralized but safe value"), new()),
    ];

    public static IEnumerable<string> BuildCaseIds() => Inputs.Select(input => input.Id);

    [Test]
    public void MirrorCoversEveryBuildCase()
    {
        var expected = EditorContextV1Corpus.Cases.Select(@case => @case.Id)
            .Where(id => !CasesWithoutBuilderInput.Contains(id, StringComparer.Ordinal));
        Assert.Multiple(() =>
        {
            Assert.That(BuildCaseIds(), Is.EquivalentTo(expected), "O espelho de entradas divergiu do corpus congelado.");
            Assert.That(EditorContextV1Corpus.Cases.Select(@case => @case.Id), Is.SupersetOf(CasesWithoutBuilderInput));
        });
    }

    /// <summary>The mirrored inputs are the corpus inputs: same bytes out, for the context and both prompt forms.</summary>
    [TestCaseSource(nameof(BuildCaseIds))]
    public void MirrorReproducesTheCorpusCaseExactly(string id)
    {
        var mirrored = Build(id);
        var corpus = EditorContextV1Corpus.Cases.Single(@case => @case.Id == id).Resolve();
        AssertSameBytes(corpus, mirrored, id + ": espelho de entrada");
    }

    [TestCaseSource(nameof(BuildCaseIds))]
    public void ContractOutputIsByteIdenticalToTheBuilder(string id)
    {
        var direct = Build(id);
        var throughContract = Contract(id, new EditorContextV1Contract());
        AssertSameBytes(direct, throughContract, id + ": contrato vs builder");
    }

    /// <summary>The same equality through the instance the resolver hands out, and under a hostile culture.</summary>
    [Test, SetCulture("tr-TR"), SetUICulture("tr-TR")]
    public void ResolvedContractOutputIsByteIdenticalToTheBuilderUnderAnyCulture()
    {
        foreach (var id in BuildCaseIds())
            AssertSameBytes(Build(id), Contract(id, AiContextContractResolver.Resolve((LocalModelMetadata?)null)),
                id + ": contrato resolvido vs builder");
    }

    [Test]
    public void ContractIdIsTheFrozenProductionIdentifier()
        => Assert.That(new EditorContextV1Contract().ContractId, Is.EqualTo(LocalModelContextContracts.EditorContextV1));

    private static AutocompleteRequest Build(string id)
    {
        var (_, snapshot, settings) = Inputs.Single(input => input.Id == id);
        return AutocompleteContextBuilder.Build(snapshot, settings);
    }

    private static AutocompleteRequest Contract(string id, IAiContextContract contract)
    {
        var (_, snapshot, settings) = Inputs.Single(input => input.Id == id);
        return contract.Build(snapshot, settings);
    }

    /// <summary>
    /// UTF-8 byte comparison on every field the contract emits, including both <c>ModelPrefix</c> forms: a string
    /// comparison would already be ordinal, but the byte form also rules out any line-ending normalization creeping
    /// in between the two paths.
    /// </summary>
    private static void AssertSameBytes(AutocompleteRequest expected, AutocompleteRequest actual, string because)
    {
        Assert.Multiple(() =>
        {
            Assert.That(Bytes(actual.Context), Is.EqualTo(Bytes(expected.Context)), because + " (Context)");
            Assert.That(Bytes(actual.Prefix), Is.EqualTo(Bytes(expected.Prefix)), because + " (Prefix)");
            Assert.That(Bytes(actual.Suffix), Is.EqualTo(Bytes(expected.Suffix)), because + " (Suffix)");
            Assert.That(Bytes(actual.Language), Is.EqualTo(Bytes(expected.Language)), because + " (Language)");
            Assert.That(actual.FileName, Is.EqualTo(expected.FileName), because + " (FileName)");
            Assert.That(actual.Dictionary, Is.EqualTo(expected.Dictionary).AsCollection, because + " (Dictionary)");
            Assert.That(Bytes(AutocompleteContextBuilder.ModelPrefix(actual, false)),
                Is.EqualTo(Bytes(AutocompleteContextBuilder.ModelPrefix(expected, false))), because + " (ModelPrefix/false)");
            Assert.That(Bytes(AutocompleteContextBuilder.ModelPrefix(actual, true)),
                Is.EqualTo(Bytes(AutocompleteContextBuilder.ModelPrefix(expected, true))), because + " (ModelPrefix/true)");
        });
    }

    private static byte[] Bytes(string? value) => new UTF8Encoding(false).GetBytes(value ?? "");
}
