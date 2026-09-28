using System.Globalization;
using System.Text;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Facts;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Application.AiContext.Experimental;

/// <summary>
/// Base comum dos formatos de contexto <b>experimentais</b> do lote A34b.
/// </summary>
/// <remarks>
/// <para>
/// <b>Existem, mas são inalcançáveis.</b> Nenhum identificador declarado aqui está em
/// <c>LocalModelContextContracts.Supported</c> e nenhuma destas classes entra em
/// <c>AiContextContractResolver.Implemented</c>. A consequência é deliberada e testada: um
/// <c>kapibarastudio-model.json</c> que declarasse <c>experimental-sections-v1</c> seria reprovado por
/// <c>LocalModelCatalog.Validate</c> e, caso chegasse ao resolver, receberia
/// <c>AiContextContractNotSupportedException</c>. Também não há registro em nenhum
/// <c>ServiceCollectionExtensions</c>. O único caminho até estes formatos é instanciá-los diretamente, em teste ou
/// em harness manual — que é exatamente o estado pretendido enquanto não houver um modelo base para compará-los.
/// </para>
/// <para>
/// <b>Por que existem assim mesmo.</b> O critério 7 da Fase 3 ("ao menos um formato alternativo vence no modelo base
/// disponível") não é atingível hoje: só existem pacotes SlopCoder treinados no contrato v1, e medi-los contra
/// qualquer formato novo enviesaria a comparação a favor do v1. Congelar os quatro formatos agora, com golden byte a
/// byte, deixa a avaliação de A34c reduzida a rodar um modelo base — sem reabrir decisões de formato.
/// </para>
/// <para>
/// <b>Quebras de linha.</b> Todos os quatro formatos escrevem apenas <c>"\n"</c> literal. Eles não herdam a mistura
/// do CRLF congelado do cabeçalho v1 com <c>"\n"</c> que o contrato v1 carrega por motivos históricos, e por isso os goldens destes formatos não precisam da reconstrução H/L de
/// <c>EditorContextV1GoldenTests</c>: a saída é idêntica byte a byte em Windows e Linux.
/// </para>
/// <para>
/// <b>Texto da janela não entra no cabeçalho.</b> Como no v1, o texto do editor viaja em
/// <c>Prefix</c>/<c>Suffix</c> — que é onde o formato FIM o espera — e o cabeçalho serializado por cada contrato
/// carrega apenas fatos e, quando o formato quiser, a <em>forma</em> da janela (quantos statements, se houve corte).
/// Repetir o texto dentro do cabeçalho dobraria o custo em tokens sem acrescentar informação.
/// </para>
/// <para>
/// <b>Entrada uniforme.</b> Todo formato recebe a mesma <see cref="ExperimentalContextInput"/>
/// (<see cref="AiFactSet"/> + <see cref="EditorWindow"/>); a diferença entre eles é só de serialização. O
/// <see cref="Build(AutocompleteContextSnapshot, AutocompleteSettings)"/> da interface continua existindo e apenas
/// projeta a aba capturada nessa forma (<see cref="ExperimentalSnapshotProjection"/>).
/// </para>
/// </remarks>
public abstract class ExperimentalContextContract : IAiContextContract
{
    /// <summary>Prefixo obrigatório de todo identificador experimental; impede confusão com <c>editor-context-v1</c>.</summary>
    public const string IdentifierPrefix = "experimental-";

    /// <summary>Identificador do formato; sempre começa por <see cref="IdentifierPrefix"/> e nunca é suportado.</summary>
    public abstract string ContractId { get; }

    /// <summary>Descrição curta da hipótese que o formato testa; usada em relatório, nunca no prompt.</summary>
    public abstract string Hypothesis { get; }

    /// <summary>Projeta a aba capturada em fatos + janela e serializa no formato do contrato.</summary>
    public AutocompleteRequest Build(AutocompleteContextSnapshot snapshot, AutocompleteSettings settings)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(settings);
        return Build(ExperimentalSnapshotProjection.Project(snapshot, settings), settings);
    }

    /// <summary>
    /// Forma direta: serializa fatos e janela já selecionados. É por aqui que os testes e um harness manual chegam ao
    /// formato, sem passar por <see cref="AutocompleteContextSnapshot"/>.
    /// </summary>
    public AutocompleteRequest Build(ExperimentalContextInput input, AutocompleteSettings settings)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(settings);
        return new AutocompleteRequest(input.Prefix, input.Suffix, input.Language, input.FileName)
        {
            Context = RenderContext(input, settings),
            Dictionary = input.Dictionary
        }.Bounded();
    }

    /// <summary>Serializa o cabeçalho do formato. Determinístico, invariante de cultura e só com <c>"\n"</c>.</summary>
    protected abstract string RenderContext(ExperimentalContextInput input, AutocompleteSettings settings);

    /// <summary>
    /// Neutraliza o fechamento de comentário de bloco. O consumidor de hoje embrulha o contexto com
    /// <c>AutocompleteContextBuilder.ModelPrefix</c>, que já faz essa troca no texto inteiro; repeti-la aqui mantém o
    /// formato estável mesmo quando o cabeçalho é usado cru, sem embrulho.
    /// </summary>
    private protected static string Neutralize(string text) => text.Replace("*/", "* /", StringComparison.Ordinal);

    /// <summary>Número em cultura invariante, no mesmo formato curto usado por <see cref="AiContextPipeline"/>.</summary>
    private protected static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>Inteiro em cultura invariante.</summary>
    private protected static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Deixa um valor de fato seguro para um formato de uma linha por fato. Um <c>Detail</c> pode carregar quebras de
    /// linha (o painel de entrada é o caso real); escapá-las mantém a linha de fato sendo uma linha e impede que o
    /// texto do usuário invente uma seção nova.
    /// </summary>
    private protected static string Inline(string text)
    {
        if (text.AsSpan().IndexOfAny('\\', '\r', '\n') < 0) return text;
        var builder = new StringBuilder(text.Length);
        foreach (var character in text)
            builder.Append(character switch { '\\' => "\\\\", '\r' => "\\r", '\n' => "\\n", _ => character.ToString(CultureInfo.InvariantCulture) });
        return builder.ToString();
    }
}

/// <summary>
/// Catálogo dos formatos experimentais implementados por este build.
/// </summary>
/// <remarks>
/// Existe para teste e relatório, não para produção: é uma lista de instâncias, não um registro. Nenhum consumidor de
/// produção a lê, e nada aqui é injetado por DI — ver <see cref="ExperimentalContextContract"/>.
/// </remarks>
public static class ExperimentalContextContracts
{
    /// <summary>Os quatro formatos do lote A34b, em ordem ordinal de <see cref="IAiContextContract.ContractId"/>.</summary>
    public static IReadOnlyList<ExperimentalContextContract> All { get; } =
    [
        FewShotContextContract.Instance,
        JsonContextContract.Instance,
        MinimalContextContract.Instance,
        SectionsContextContract.Instance
    ];
}
