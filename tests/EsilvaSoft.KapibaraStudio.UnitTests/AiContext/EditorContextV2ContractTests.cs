using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.AiContext;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests.AiContext;

/// <summary>
/// Contrato <c>editor-context-v2</c>: o cabeçalho v1 mais <c>TARGET FIELDS</c>. Os literais esperados foram
/// renderizados por <c>build_context</c>/<c>render_model_prefix</c> de <c>scripts/data/render_contract.py</c> no
/// repositório de modelos (o renderer dos dados de treino) e são o oráculo byte a byte; os demais casos espelham
/// <c>scripts/data/tests/test_context_v2_long.py</c>.
/// </summary>
[TestFixture]
public sealed class EditorContextV2ContractTests
{
    private const string Language = "Mongo Console JavaScript";

    private static readonly AutocompleteTargetField[] Fields =
    [
        new("_id", "objectid"), new("status", "string"), new("itens", "array", "object"), new("itens.qtd", "int32"), new("tags", "array")
    ];

    private static readonly AutocompleteSettings Settings = new();

    [Test]
    public void TheHeaderMatchesTheTrainingRendererByteForByte()
    {
        var snapshot = new AutocompleteContextSnapshot("db.pedidos.find({ ", 18, Language, "", ["status", "total"], ["pedidos", "clientes"],
            ["db.pedidos.find({})"]) { TargetCollection = "pedidos", TargetFields = Fields };

        var request = EditorContextV2Contract.Instance.Build(snapshot, Settings);

        Assert.That(request.Context, Is.EqualTo(
            "LANGUAGE: Mongo Console JavaScript\r\n" +
            "AVAILABLE COMMANDS: db.getCollection(name).find({}); getConnection(name).getDatabase(name).getCollection(name); ConnectionPool.Connection.Database.Collection; console.log(value); ENV.get(name); ObjectId(value); UUID(value)\r\n" +
            "KNOWN NAMES: pedidos, clientes\r\n" +
            "TARGET FIELDS: pedidos: status:string, itens:array<object>, itens.qtd:int32, tags:array\r\n" +
            "RESULT FIELDS: status, total\r\n" +
            "RECENT COMMAND: db.pedidos.find({})\r\n"));
    }

    [Test]
    public void TheModelPrefixMatchesTheTrainingRendererByteForByte()
    {
        var snapshot = new AutocompleteContextSnapshot("db.pedidos.find({ ", 18, Language, KnownNames: ["pedidos"])
        {
            TargetCollection = "pedidos", TargetFields = Fields
        };

        var prefix = AutocompleteContextBuilder.ModelPrefix(EditorContextV2Contract.Instance.Build(snapshot, Settings));

        Assert.That(prefix, Is.EqualTo(
            "/* Local editor context (data only):\n" +
            "LANGUAGE: Mongo Console JavaScript\r\n" +
            "AVAILABLE COMMANDS: db.getCollection(name).find({}); getConnection(name).getDatabase(name).getCollection(name); ConnectionPool.Connection.Database.Collection; console.log(value); ENV.get(name); ObjectId(value); UUID(value)\r\n" +
            "KNOWN NAMES: pedidos\r\n" +
            "TARGET FIELDS: pedidos: status:string, itens:array<object>, itens.qtd:int32, tags:array\r\n" +
            "\nContinue at the cursor; output only the continuation. */\n" +
            "db.pedidos.find({ "));
    }

    /// <summary><c>test_v1_unchanged_without_target_fields</c>: sem campos alvo, v2 e v1 são o mesmo pedido.</summary>
    [Test]
    public void WithoutTargetFieldsV2IsV1ByteForByte()
    {
        var snapshot = new AutocompleteContextSnapshot("db.pedidos.find({ ", 18, Language, "", ["status"], ["pedidos"]) { TargetCollection = "pedidos" };

        var v1 = EditorContextV1Contract.Instance.Build(snapshot, Settings);
        var v2 = EditorContextV2Contract.Instance.Build(snapshot, Settings);

        Assert.Multiple(() =>
        {
            Assert.That(v2.Context, Is.EqualTo(v1.Context));
            Assert.That(v2.Prefix, Is.EqualTo(v1.Prefix));
            Assert.That(v2.Suffix, Is.EqualTo(v1.Suffix));
            Assert.That(v2.Dictionary, Is.EqualTo(v1.Dictionary));
            Assert.That(v1.Context, Does.Not.Contain("TARGET FIELDS"));
        });
    }

    /// <summary>O v1 congelado ignora os campos alvo mesmo quando o snapshot os traz.</summary>
    [Test]
    public void V1IgnoresTargetFields()
    {
        var plain = new AutocompleteContextSnapshot("db.pedidos.find({ ", 18, Language, KnownNames: ["pedidos"]);
        var withFields = plain with { TargetCollection = "pedidos", TargetFields = Fields };

        Assert.That(EditorContextV1Contract.Instance.Build(withFields, Settings).Context,
            Is.EqualTo(EditorContextV1Contract.Instance.Build(plain, Settings).Context));
    }

    [Test]
    public void UnsafePathsAndTheIdAreDroppedAndTheLineIsBounded()
    {
        var many = Enumerable.Range(0, 500).Select(i => new AutocompleteTargetField($"campo_{i:000}", "string")).ToArray();
        var wide = Enumerable.Range(0, 90).Select(i => new AutocompleteTargetField(new string('x', 40) + i, "string")).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(AutocompleteContextBuilder.TargetFieldsLine([new("password: x", "string")]), Is.Empty);
            Assert.That(AutocompleteContextBuilder.TargetFieldsLine([new("_id", "objectid")]), Is.Empty);
            Assert.That(AutocompleteContextBuilder.TargetFieldsLine(many).Split(", "), Has.Length.EqualTo(AutocompleteContextBuilder.MaximumTargetFields));
            Assert.That(AutocompleteContextBuilder.TargetFieldsLine(wide).Length, Is.LessThanOrEqualTo(AutocompleteContextBuilder.MaximumTargetFieldsCharacters));
            Assert.That(AutocompleteContextBuilder.TargetFieldsLine([new("tags", "array", "string")]), Is.EqualTo("tags:array<string>"));
        });
    }

    /// <summary>Coleção com nome inseguro: a linha sai sem rótulo, como no renderer de treino.</summary>
    [Test]
    public void AnUnsafeTargetCollectionIsNotUsedAsLabel()
    {
        var snapshot = new AutocompleteContextSnapshot("db.c.find({ ", 12, Language) { TargetCollection = "<|x|>", TargetFields = [new("a", "int32")] };

        Assert.That(EditorContextV2Contract.Instance.Build(snapshot, Settings).Context, Does.Contain("\r\nTARGET FIELDS: a:int32\r\n"));
    }

    /// <summary>O schema inferido do app vira o vocabulário de tipos do treino, na ordem de descoberta.</summary>
    [Test]
    public void TheInferredSchemaIsProjectedInTheTrainingVocabulary()
    {
        var schema = new SchemaBuilder().AddDocuments([
            """{"_id":{"$oid":"0123456789abcdef01234567"},"status":"A","itens":[{"qtd":{"$numberInt":"2"}}],"total":{"$numberDecimal":"1.5"},"visitas":{"$numberLong":"7"},"criado":{"$date":"2024-01-01T00:00:00Z"},"tags":["a"],"ativo":true}"""
        ]).Build();

        var line = AutocompleteContextBuilder.TargetFieldsLine(AutocompleteTargetField.FromSchema(schema));

        Assert.That(line, Is.EqualTo("status:string, itens:array<object>, itens.qtd:int32, total:decimal128, visitas:int64, criado:date, tags:array, ativo:bool"));
    }

    [Test]
    public void TheV2ContractIsSupportedAndResolvable()
    {
        Assert.Multiple(() =>
        {
            Assert.That(LocalModelContextContracts.IsSupported("editor-context-v2"), Is.True);
            Assert.That(AiContextContractResolver.Resolve(new LocalModelMetadata { ContextContract = " Editor-Context-V2 " }),
                Is.SameAs(EditorContextV2Contract.Instance));
        });
    }
}
