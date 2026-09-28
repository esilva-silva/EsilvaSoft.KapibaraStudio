using System.Text;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Facts;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Application.AiContext.Experimental;

/// <summary>
/// <c>experimental-sections-v1</c>: seções nomeadas em estilo Markdown/YAML, uma categoria de fato por seção.
/// </summary>
/// <remarks>
/// <para>
/// <b>Hipótese.</b> É o formato B de <c>docs/auto-complite/ai-context.md</c>: um modelo <em>base</em> (não ajustado
/// ao contrato v1) tende a seguir melhor uma estrutura explícita de seções do que o bloco solto de linhas
/// <c>CHAVE: valor</c> do v1, porque cabeçalhos <c>#</c>/<c>##</c> e listas <c>-</c> são padrão abundante nos dados
/// de pré-treino.
/// </para>
/// <para>
/// <b>Seções vazias são omitidas.</b> Imprimir <c>## campos</c> seguido de nada gastaria tokens para afirmar
/// ausência; a ausência já é lida pela falta da seção. A ordem das seções é fixa, e dentro de cada seção a ordem é a
/// do <see cref="AiFactSet"/> — relevância decrescente, nunca alfabética, porque o corte por orçamento remove do fim.
/// </para>
/// </remarks>
public sealed class SectionsContextContract : ExperimentalContextContract
{
    /// <summary>Instância compartilhada; o contrato não tem estado.</summary>
    public static SectionsContextContract Instance { get; } = new();

    /// <inheritdoc/>
    public override string ContractId => IdentifierPrefix + "sections-v1";

    /// <inheritdoc/>
    public override string Hypothesis =>
        "Seções nomeadas explícitas orientam melhor um modelo base do que o bloco solto de CHAVE: valor do v1.";

    /// <inheritdoc/>
    protected override string RenderContext(ExperimentalContextInput input, AutocompleteSettings settings)
    {
        var builder = new StringBuilder("# contexto-local\n");
        builder.Append("formato: ").Append(ContractId).Append('\n');
        if (input.Language.Length > 0) builder.Append("linguagem: ").Append(Inline(input.Language)).Append('\n');

        Section(builder, "colecoes", input.Facts.OfKind(AiFactKind.Collection));
        Section(builder, "campos", input.Facts.OfKind(AiFactKind.AnyFieldSchema));
        Section(builder, "operadores", input.Facts.OfKind(AiFactKind.Operator));
        Section(builder, "locais", input.Facts.OfKind(AiFactKind.LocalVariable));

        if (!input.Window.IsEmpty)
        {
            builder.Append("## janela\n");
            builder.Append("statements: ").Append(Number(input.Window.Preceding.Count + (input.Window.Current is null ? 0 : 1))).Append('\n');
            if (input.Window.Statements.Any(statement => statement.Truncated)) builder.Append("truncada: sim\n");
        }

        builder.Append("## instrucao\nContinue exatamente no cursor e responda somente a continuacao.\n");
        return Neutralize(builder.ToString());
    }

    /// <summary>Uma seção por categoria; nada é escrito quando a categoria não tem fatos.</summary>
    private static void Section(StringBuilder builder, string name, AiFactSet facts)
    {
        if (facts.Count == 0) return;
        builder.Append("## ").Append(name).Append('\n');
        foreach (var fact in facts)
        {
            builder.Append("- ").Append(Inline(fact.Payload.Name));
            if (!string.IsNullOrEmpty(fact.Payload.LogicalType)) builder.Append(": ").Append(Inline(fact.Payload.LogicalType!));
            if (fact.Payload.Values.Count > 0) builder.Append(" [").Append(string.Join(", ", fact.Payload.Values.Select(Inline))).Append(']');
            if (fact.Payload.Presence is { } presence) builder.Append(" presenca=").Append(Number(presence));
            if (fact.Payload.Confidence is { } confidence) builder.Append(" confianca=").Append(Number(confidence));
            if (fact.Scope.Kind == AiFactScopeKind.Collection && fact.Scope.Database.Length > 0)
                builder.Append(" (").Append(Inline(fact.Scope.Database)).Append(')');
            if (!string.IsNullOrEmpty(fact.Payload.Detail)) builder.Append(" -- ").Append(Inline(fact.Payload.Detail!));
            builder.Append('\n');
        }
    }
}
