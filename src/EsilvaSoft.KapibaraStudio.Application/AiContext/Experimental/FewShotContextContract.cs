using System.Text;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Facts;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Application.AiContext.Experimental;

/// <summary>
/// <c>experimental-fewshot-v1</c>: dois exemplos sintéticos de contexto→continuação antes do contexto real.
/// </summary>
/// <remarks>
/// <para>
/// <b>Hipótese.</b> Um modelo base não sabe o que fazer com uma lista de campos; sabe imitar. Dois exemplos curtos
/// ensinam por analogia a relação "estes são os nomes disponíveis → continue o código usando exatamente estes nomes",
/// que é justamente a falha típica do v1 em modelo não ajustado (nomes inventados).
/// </para>
/// <para>
/// <b>Os exemplos são constantes e sintéticos.</b> Os nomes (<c>pedidos</c>, <c>cliente.nome</c>) são inventados neste
/// arquivo e nunca derivam do workspace: um exemplo derivado dos dados do usuário gastaria orçamento repetindo o que
/// o contexto real já diz e poderia vazar um nome a mais do que a seleção autorizou. São também o motivo de este ser
/// o formato mais caro dos quatro — o custo fixo é a aposta.
/// </para>
/// <para>
/// <b>Aviso explícito de que são exemplos.</b> A primeira linha do bloco declara que aquilo não é o workspace; sem
/// isso, o modelo pode completar usando <c>pedidos</c> em vez das coleções reais.
/// </para>
/// </remarks>
public sealed class FewShotContextContract : ExperimentalContextContract
{
    /// <summary>Instância compartilhada; o contrato não tem estado.</summary>
    public static FewShotContextContract Instance { get; } = new();

    /// <summary>
    /// Bloco fixo de exemplos, sempre com quebras de linha LF. A normalização existe porque um literal bruto herda as
    /// quebras de linha do arquivo-fonte como ele foi extraído do git; sem ela, um checkout com CRLF mudaria o prompt.
    /// </summary>
    public static string Examples { get; } = RawExamples.Replace("\r\n", "\n", StringComparison.Ordinal);

    private const string RawExamples = """
        EXEMPLOS SINTETICOS (nomes inventados, nao sao do workspace atual):
        exemplo 1
          nomes: pedidos, clientes
          campos: cliente.nome: string, total: double
          entrada: db.pedidos.find({ "cliente.
          continuacao: nome": "Ana" })
        exemplo 2
          nomes: eventos
          campos: criadoEm: date, tipo: string
          entrada: db.eventos.aggregate([{ $match: { ti
          continuacao: po: "login" } }])
        FIM DOS EXEMPLOS

        """;

    /// <inheritdoc/>
    public override string ContractId => IdentifierPrefix + "fewshot-v1";

    /// <inheritdoc/>
    public override string Hypothesis =>
        "Dois exemplos sintéticos ensinam por analogia a usar só os nomes oferecidos, que é a falha típica de um modelo base com o v1.";

    /// <inheritdoc/>
    protected override string RenderContext(ExperimentalContextInput input, AutocompleteSettings settings)
    {
        var builder = new StringBuilder(Examples);
        builder.Append("CONTEXTO REAL\n");
        if (input.Language.Length > 0) builder.Append("  linguagem: ").Append(Inline(input.Language)).Append('\n');
        Line(builder, "nomes", input.Facts.OfKind(AiFactKind.Collection), typed: false);
        Line(builder, "campos", input.Facts.OfKind(AiFactKind.AnyFieldSchema), typed: true);
        Line(builder, "operadores", input.Facts.OfKind(AiFactKind.Operator), typed: false);
        Line(builder, "locais", input.Facts.OfKind(AiFactKind.LocalVariable), typed: true);
        builder.Append("  continuacao: complete no cursor, usando somente os nomes acima.\n");
        return Neutralize(builder.ToString());
    }

    /// <summary>
    /// Uma linha por categoria, com os fatos separados por vírgula — a mesma forma usada dentro dos exemplos, para
    /// que a analogia seja literal.
    /// </summary>
    private static void Line(StringBuilder builder, string name, AiFactSet facts, bool typed)
    {
        if (facts.Count == 0) return;
        builder.Append("  ").Append(name).Append(": ");
        for (var index = 0; index < facts.Count; index++)
        {
            if (index > 0) builder.Append(", ");
            var payload = facts[index].Payload;
            builder.Append(Inline(payload.Name));
            if (typed && !string.IsNullOrEmpty(payload.LogicalType)) builder.Append(": ").Append(Inline(payload.LogicalType));
        }

        builder.Append('\n');
    }
}
