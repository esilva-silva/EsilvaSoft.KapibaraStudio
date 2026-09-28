using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.AiContext.Experimental;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Facts;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Text;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests.AiContext.Experimental;

/// <summary>
/// Um caso do corpus experimental. Quando <see cref="Snapshot"/> existe, a entrada vem da projeção da aba capturada
/// e o mesmo caso também prova que os dois caminhos de <c>Build</c> (aba e entrada direta) coincidem; quando não
/// existe, a entrada é montada à mão para alcançar formas que a projeção não produz (fato aprendido, detalhe com
/// quebra de linha, janela truncada).
/// </summary>
public sealed record ExperimentalContextCase(string Id, AutocompleteSettings Settings, Func<ExperimentalContextInput> Resolve,
    AutocompleteContextSnapshot? Snapshot = null)
{
    public override string ToString() => Id;
}

/// <summary>
/// Corpus compartilhado pelos quatro formatos experimentais.
/// </summary>
/// <remarks>
/// É deliberadamente o <b>mesmo</b> corpus para todos: só assim a comparação de tamanho entre formatos (e contra o
/// v1) mede a serialização, e não a entrada. Nove casos bastam aqui — este lote congela formatos que nenhuma rota de
/// produção alcança, e não o contrato de treino de um pacote real, que é o que justifica os ~20 casos do v1.
/// </remarks>
public static class ExperimentalContextCorpus
{
    /// <summary>Aba típica usada também pela comparação de tamanho contra o v1.</summary>
    public static AutocompleteContextSnapshot RichSnapshot { get; } = new(
        "db.Projetos.find({ \"Cliente.", 28, "json",
        Input: "{ \"ativo\": true }",
        ResultFields: ["Id", "NomeProjeto", "Cliente.Id", "Cliente.Nome"],
        KnownNames: ["servidor-alfa", "Projetos", "Clientes"],
        RecentCommands: ["db.Clientes.find({}).limit(10)", "db.Projetos.countDocuments({})"],
        FileName: "console.js");

    public static IReadOnlyList<ExperimentalContextCase> Cases { get; } =
    [
        Snapshot("aba-vazia", new(), new("", 0, "json")),
        Snapshot("json-completo", new(), RichSnapshot),
        Snapshot("mongosh-sem-paineis",
            new() { UseEditorContext = false, UseInputPanelContext = false, UseResultPanelContext = false },
            RichSnapshot with { Language = "JavaScript (mongosh)" }),
        Snapshot("console-com-texto-sensivel", new(),
            new("getConnection(\"alfa\")", 21, "Mongo Console JavaScript",
                Input: "mongodb://user:senha@host/db",
                KnownNames: ["mongodb://user:senha@host/db", "prefixo<|fim|>", "Clientes"],
                RecentCommands: ["ENV.get(\"SENHA\")"])),

        Direct("fatos-ricos", () => new(AiFactSet.From(
        [
            new(AiFactKind.Collection, AiFactOrigin.CatalogSchema, AiFactScope.ForCollection("loja", "Pedidos"), new("Pedidos")),
            new(AiFactKind.FieldSchema, AiFactOrigin.CatalogSchema, AiFactScope.ForCollection("loja", "Pedidos"),
                new("total") { LogicalType = "double", Presence = 1, Values = ["double", "int"] }),
            new(AiFactKind.LearnedFieldSchema, AiFactOrigin.LearnedSchema, AiFactScope.ForCollection("loja", "Pedidos"),
                new("cliente.nome") { LogicalType = "string", Presence = 0.25, Confidence = 0.5, Detail = "aprendido de 400 documentos" }),
            new(AiFactKind.Operator, AiFactOrigin.EditorSyntax, AiFactScope.ForDocument("aba-1"), new("$match") { LogicalType = "stage" }),
            new(AiFactKind.LocalVariable, AiFactOrigin.EditorSyntax, AiFactScope.ForDocument("aba-1"), new("cursor") { LogicalType = "Cursor" })
        ]), Window("db.Pedidos.find({ ", "})", truncated: false), "json", "pipeline.json")),

        Direct("detalhe-hostil", () => new(AiFactSet.From(
        [
            new(AiFactKind.FieldSchema, AiFactOrigin.EditorSyntax, AiFactScope.ForDocument("aba-2"),
                new("caminho/estranho") { LogicalType = "string", Detail = "linha 1\nlinha 2 */ fim \"aspas\" \\ barra" })
        ]), Window("db.x.find({ ", "", truncated: false), "json")),

        Direct("janela-truncada", () => new(AiFactSet.From(
        [
            new(AiFactKind.Collection, AiFactOrigin.CatalogSchema, AiFactScope.ForCollection("", "Eventos"), new("Eventos"))
        ]), new EditorWindow(new(TextSpan.FromBounds(100, 130), "db.Eventos.aggregate([{ $matc", true),
            [new EditorStatement(default, "db.Eventos.countDocuments({})", true)], 129), "json")),

        Direct("acima-do-teto-minimo", () => new(AiFactSet.From(
            Enumerable.Range(1, 20).Select(index => new AiFact(AiFactKind.FieldSchema, AiFactOrigin.CatalogSchema,
                AiFactScope.ForCollection("loja", "Pedidos"), new("campo" + index.ToString(System.Globalization.CultureInfo.InvariantCulture)) { LogicalType = "string" }))),
            Window("db.Pedidos.find({ ", "", truncated: false), "json")),

        Direct("sem-fatos-com-janela", () => new(AiFactSet.Empty, Window("db.Pedidos.find({ ", "})", truncated: false), "json"))
    ];

    private static ExperimentalContextCase Snapshot(string id, AutocompleteSettings settings, AutocompleteContextSnapshot snapshot)
        => new(id, settings, () => ExperimentalSnapshotProjection.Project(snapshot, settings), snapshot);

    private static ExperimentalContextCase Direct(string id, Func<ExperimentalContextInput> resolve)
        => new(id, new(), resolve);

    /// <summary>Janela de um statement só, com o cursor entre <paramref name="before"/> e <paramref name="after"/>.</summary>
    private static EditorWindow Window(string before, string after, bool truncated)
    {
        var text = before + after;
        return new(new EditorStatement(TextSpan.FromBounds(0, text.Length), text, truncated), null, before.Length);
    }
}
