using System.Text;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Facts;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Application.AiContext.Experimental;

/// <summary>
/// <c>experimental-minimal-v1</c>: só os fatos de maior prioridade, um por linha, sem cabeçalho nem instrução.
/// </summary>
/// <remarks>
/// <para>
/// <b>Hipótese.</b> Em janela pequena (o padrão do produto é 2048 tokens), quase todo o cabeçalho do v1 é decoração:
/// a lista fixa de comandos e a frase de instrução ocupam espaço que poderia ser do código real do usuário. Este
/// formato testa o extremo oposto — o mínimo que ainda transmite nomes válidos — e serve de piso de comparação para
/// os outros três.
/// </para>
/// <para>
/// <b>Prioridade é categoria, não ordenação.</b> Ficam apenas coleções e campos (os nomes que o modelo precisa
/// acertar), no máximo <see cref="MaximumFacts"/>, na ordem de relevância que o seletor já entregou. Operadores saem
/// porque o modelo base já os conhece do pré-treino, e o painel de entrada sai porque é texto longo. Reordenar aqui
/// seria decidir relevância duas vezes, em dois lugares.
/// </para>
/// <para>
/// <b>Sem instrução.</b> A tarefa é comunicada pelo próprio formato FIM (prefixo/sufixo), não por prosa; escrever
/// "continue no cursor" custaria tokens para repetir o que os marcadores já dizem.
/// </para>
/// </remarks>
public sealed class MinimalContextContract : ExperimentalContextContract
{
    /// <summary>Teto de fatos do formato; o resto é cortado antes de qualquer orçamento.</summary>
    public const int MaximumFacts = 12;

    /// <summary>Instância compartilhada; o contrato não tem estado.</summary>
    public static MinimalContextContract Instance { get; } = new();

    /// <inheritdoc/>
    public override string ContractId => IdentifierPrefix + "minimal-v1";

    /// <inheritdoc/>
    public override string Hypothesis =>
        "Em janela pequena, doze nomes válidos valem mais do que um cabeçalho completo com lista fixa de comandos.";

    /// <inheritdoc/>
    protected override string RenderContext(ExperimentalContextInput input, AutocompleteSettings settings)
    {
        var facts = input.Facts.OfKind(AiFactKind.Collection | AiFactKind.AnyFieldSchema);
        if (facts.Count == 0) return "";
        var builder = new StringBuilder();
        foreach (var fact in facts.Take(MaximumFacts))
        {
            builder.Append(Inline(fact.Payload.Name));
            if (!string.IsNullOrEmpty(fact.Payload.LogicalType)) builder.Append(':').Append(Inline(fact.Payload.LogicalType));
            builder.Append('\n');
        }

        return Neutralize(builder.ToString());
    }
}
