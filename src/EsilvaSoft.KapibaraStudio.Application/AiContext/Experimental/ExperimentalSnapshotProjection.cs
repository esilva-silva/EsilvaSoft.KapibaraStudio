using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Facts;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Text;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Application.AiContext.Experimental;

/// <summary>
/// Projeta a aba capturada (<see cref="AutocompleteContextSnapshot"/>) na forma que os formatos experimentais
/// consomem: <see cref="AiFactSet"/> + <see cref="EditorWindow"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Mesmos tetos do v1.</b> 4096/128 caracteres de prefixo, 1024 de sufixo, 128 nomes, 128 campos de resultado,
/// 3 comandos recentes de 256 caracteres, 1024 do painel de entrada. Copiar os limites do contrato congelado é o que
/// torna a comparação de tamanho entre formatos honesta: o que muda entre v1 e os experimentos é a serialização, não
/// a quantidade de material.
/// </para>
/// <para>
/// <b>Mesma privacidade do v1.</b> Todo texto vindo do usuário passa pelo mesmo filtro (sensível ou portador de
/// marcador de modelo é descartado inteiro, nunca mascarado). O opt-out por painel (<c>UseEditorContext</c>,
/// <c>UseResultPanelContext</c>, <c>UseInputPanelContext</c>) é respeitado antes de qualquer fato existir.
/// </para>
/// <para>
/// <b>Decisões próprias do lote.</b> Duas coisas que no v1 são linhas de texto viram fatos aqui, para que todo
/// formato receba uma entrada uniforme: (1) a lista de comandos do dialeto vira fatos
/// <see cref="AiFactKind.Operator"/> com origem <see cref="AiFactOrigin.EditorSyntax"/> — é conhecimento da
/// linguagem do editor, não do catálogo; (2) o painel de entrada vira um único fato
/// <see cref="AiFactKind.LocalVariable"/> de escopo de documento, chamado <c>entrada</c>. Comandos recentes viram
/// statements vizinhos da janela, com <see cref="TextSpan"/> sintético: eles vêm do histórico e não têm posição no
/// documento atual (só o statement corrente precisa de span real, porque é nele que o cursor é cortado).
/// </para>
/// </remarks>
public static class ExperimentalSnapshotProjection
{
    /// <summary>Teto de fatos projetados; igual ao padrão de <c>AiFactRequest.MaximumFacts</c>.</summary>
    public const int MaximumFacts = 200;

    /// <summary>Identidade usada quando a aba não tem nome de arquivo; um escopo de documento nunca é vazio.</summary>
    public const string UnnamedDocumentId = "aba-sem-nome";

    /// <summary>Nome do fato que carrega o painel de entrada.</summary>
    public const string InputPanelFactName = "entrada";

    private static readonly string[] JsonOperators =
        ["$match", "$project", "$group", "$sort", "$limit", "$lookup", "$unwind",
         "$eq", "$ne", "$gt", "$gte", "$lt", "$lte", "$in", "$and", "$or"];

    private static readonly string[] MongoshOperators =
        ["db.getCollection", "db.getSiblingDB", "find", "findOne", "aggregate", "sort", "limit",
         "countDocuments", "print", "ObjectId", "UUID"];

    private static readonly string[] ConsoleOperators =
        ["db.getCollection", "getConnection", "ConnectionPool", "console.log", "ENV.get", "ObjectId", "UUID"];

    /// <summary>Projeta a aba respeitando as preferências de contexto do usuário.</summary>
    public static ExperimentalContextInput Project(AutocompleteContextSnapshot snapshot, AutocompleteSettings settings)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(settings);
        var documentId = string.IsNullOrEmpty(snapshot.FileName) ? UnnamedDocumentId : snapshot.FileName;
        var document = AiFactScope.ForDocument(documentId);
        var facts = new List<AiFact>();

        foreach (var name in (snapshot.KnownNames ?? []).Where(IsSafe).Where(name => name.Length > 0).Take(128))
            facts.Add(new(AiFactKind.Collection, AiFactOrigin.CatalogSchema, AiFactScope.ForCollection("", name), new(name)));

        if (settings.UseResultPanelContext)
            foreach (var field in (snapshot.ResultFields ?? []).Where(IsSafe).Where(field => field.Length > 0).Take(128))
                facts.Add(new(AiFactKind.FieldSchema, AiFactOrigin.EditorSyntax, document, new(field)));

        foreach (var name in Operators(snapshot.Language))
            facts.Add(new(AiFactKind.Operator, AiFactOrigin.EditorSyntax, document, new(name) { LogicalType = "operador" }));

        if (settings.UseInputPanelContext && snapshot.Input.Length > 0 && IsSafe(snapshot.Input))
            facts.Add(new(AiFactKind.LocalVariable, AiFactOrigin.EditorSyntax, document,
                new(InputPanelFactName) { LogicalType = "painel", Detail = snapshot.Input[..Math.Min(1024, snapshot.Input.Length)] }));

        return new(AiFactSet.From(facts.Take(MaximumFacts)), Window(snapshot, settings), snapshot.Language, snapshot.FileName);
    }

    /// <summary>Comandos do dialeto, na mesma seleção que o v1 escreve em <c>AVAILABLE COMMANDS</c>.</summary>
    public static IReadOnlyList<string> Operators(string? language) => language switch
    {
        "json" => JsonOperators,
        "JavaScript (mongosh)" => MongoshOperators,
        _ => ConsoleOperators
    };

    private static EditorWindow Window(AutocompleteContextSnapshot snapshot, AutocompleteSettings settings)
    {
        var caret = Math.Clamp(snapshot.Caret, 0, snapshot.Text.Length);
        var span = settings.UseEditorContext ? 4096 : 128;
        var start = Math.Max(0, caret - span);
        var end = settings.UseEditorContext ? Math.Min(snapshot.Text.Length, caret + 1024) : caret;
        var text = snapshot.Text[start..end];
        var current = text.Length == 0 ? null : new EditorStatement(TextSpan.FromBounds(start, end), text, start > 0 || end < snapshot.Text.Length);
        var preceding = settings.UseEditorContext
            ? (snapshot.RecentCommands ?? []).Where(IsSafe).Take(3)
                .Select(command => new EditorStatement(default, command[..Math.Min(command.Length, 256)], command.Length > 256)).ToArray()
            : [];
        return current is null && preceding.Length == 0 ? EditorWindow.Empty : new(current, preceding, caret);
    }

    /// <summary>
    /// Mesmo critério do contrato congelado (<c>AutocompleteContextBuilder.IsSafe</c>, que é privado): texto sensível
    /// ou com marcador de modelo é descartado inteiro. Reimplementado aqui, e não exposto lá, porque nada de
    /// experimental pode exigir mudança no arquivo congelado.
    /// </summary>
    private static bool IsSafe(string text) => !CompletionPrivacy.ContainsSensitiveText(text)
        && !text.Contains("<|", StringComparison.Ordinal) && !text.Contains("<｜", StringComparison.Ordinal);
}
