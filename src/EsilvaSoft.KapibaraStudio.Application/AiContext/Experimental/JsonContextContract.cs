using System.Text;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Facts;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Application.AiContext.Experimental;

/// <summary>
/// <c>experimental-json-v1</c>: um único objeto JSON compacto, em uma linha, com os fatos já tipados.
/// </summary>
/// <remarks>
/// <para>
/// <b>Hipótese.</b> É o formato E de <c>docs/auto-complite/ai-context.md</c>: modelos base recentes viram muito JSON
/// estruturado (chamadas de ferramenta, schemas, respostas de API) e tendem a tratar um objeto bem formado como dado,
/// não como prosa a continuar — o que reduz a chance de o modelo "completar o cabeçalho" em vez do código.
/// </para>
/// <para>
/// <b>Serialização à mão, de propósito.</b> Nada de <c>JsonSerializer</c>: a ordem das chaves, a omissão de campos
/// nulos e o formato dos números são o contrato, e não podem depender de opções padrão de uma biblioteca que uma
/// atualização futura pode mudar. Os números saem sempre em cultura invariante, com no máximo duas casas.
/// </para>
/// <para>
/// <b>Barra escapada.</b> Toda <c>/</c> dentro de string sai como <c>\/</c> (escape válido de JSON). O motivo é
/// concreto: o consumidor de hoje embrulha o cabeçalho num comentário de bloco, e um <c>*/</c> vindo de um nome de
/// campo arruinaria tanto o comentário quanto a validade do JSON se fosse neutralizado depois.
/// </para>
/// </remarks>
public sealed class JsonContextContract : ExperimentalContextContract
{
    /// <summary>Instância compartilhada; o contrato não tem estado.</summary>
    public static JsonContextContract Instance { get; } = new();

    /// <inheritdoc/>
    public override string ContractId => IdentifierPrefix + "json-v1";

    /// <inheritdoc/>
    public override string Hypothesis =>
        "Um objeto JSON bem formado é lido como dado por modelos base expostos a ferramentas e schemas, não como prosa a continuar.";

    /// <inheritdoc/>
    protected override string RenderContext(ExperimentalContextInput input, AutocompleteSettings settings)
    {
        var json = new StringBuilder("{");
        Member(json, "contract", ContractId, first: true);
        if (input.Language.Length > 0) Member(json, "language", input.Language, first: false);
        Names(json, "collections", input.Facts.OfKind(AiFactKind.Collection));
        Fields(json, "fields", input.Facts.OfKind(AiFactKind.AnyFieldSchema));
        Names(json, "operators", input.Facts.OfKind(AiFactKind.Operator));
        Fields(json, "locals", input.Facts.OfKind(AiFactKind.LocalVariable));
        if (!input.Window.IsEmpty)
        {
            json.Append(",\"window\":{\"statements\":")
                .Append(Number(input.Window.Preceding.Count + (input.Window.Current is null ? 0 : 1)))
                .Append(",\"truncated\":")
                .Append(input.Window.Statements.Any(statement => statement.Truncated) ? "true" : "false")
                .Append('}');
        }

        json.Append(",\"task\":\"continue at the caret; output only the continuation\"}\n");
        return json.ToString();
    }

    /// <summary>Array de nomes simples; omitido quando vazio, como todo membro deste formato.</summary>
    private static void Names(StringBuilder json, string member, AiFactSet facts)
    {
        if (facts.Count == 0) return;
        json.Append(",\"").Append(member).Append("\":[");
        for (var index = 0; index < facts.Count; index++)
        {
            if (index > 0) json.Append(',');
            String(json, facts[index].Payload.Name);
        }

        json.Append(']');
    }

    /// <summary>Array de objetos; só os campos presentes no payload são escritos.</summary>
    private static void Fields(StringBuilder json, string member, AiFactSet facts)
    {
        if (facts.Count == 0) return;
        json.Append(",\"").Append(member).Append("\":[");
        for (var index = 0; index < facts.Count; index++)
        {
            if (index > 0) json.Append(',');
            var payload = facts[index].Payload;
            json.Append("{\"name\":");
            String(json, payload.Name);
            if (!string.IsNullOrEmpty(payload.LogicalType)) { json.Append(",\"type\":"); String(json, payload.LogicalType); }
            if (payload.Presence is { } presence) json.Append(",\"presence\":").Append(Number(presence));
            if (payload.Confidence is { } confidence) json.Append(",\"confidence\":").Append(Number(confidence));
            if (payload.Values.Count > 0)
            {
                json.Append(",\"values\":[");
                for (var value = 0; value < payload.Values.Count; value++)
                {
                    if (value > 0) json.Append(',');
                    String(json, payload.Values[value]);
                }

                json.Append(']');
            }

            if (!string.IsNullOrEmpty(payload.Detail)) { json.Append(",\"detail\":"); String(json, payload.Detail); }
            json.Append('}');
        }

        json.Append(']');
    }

    private static void Member(StringBuilder json, string name, string value, bool first)
    {
        if (!first) json.Append(',');
        json.Append('"').Append(name).Append("\":");
        String(json, value);
    }

    /// <summary>Escape mínimo de JSON, mais a barra, e sempre o mesmo para a mesma entrada.</summary>
    private static void String(StringBuilder json, string value)
    {
        json.Append('"');
        foreach (var character in value)
            switch (character)
            {
                case '"': json.Append("\\\""); break;
                case '\\': json.Append("\\\\"); break;
                case '/': json.Append("\\/"); break;
                case '\b': json.Append("\\b"); break;
                case '\f': json.Append("\\f"); break;
                case '\n': json.Append("\\n"); break;
                case '\r': json.Append("\\r"); break;
                case '\t': json.Append("\\t"); break;
                default:
                    if (character < ' ' || char.IsSurrogate(character)) json.Append("\\u").Append(((int)character).ToString("x4", System.Globalization.CultureInfo.InvariantCulture));
                    else json.Append(character);
                    break;
            }

        json.Append('"');
    }
}
