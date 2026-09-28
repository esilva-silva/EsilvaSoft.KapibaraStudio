using EsilvaSoft.KapibaraStudio.LocalAi.Core;

namespace EsilvaSoft.KapibaraStudio.Benchmarks.Ai;

/// <summary>
/// Contador de tokens determinístico e sem modelo, usado para que o harness rode em qualquer máquina sem baixar pesos
/// ONNX nem carregar um tokenizer real.
/// </summary>
/// <remarks>
/// <para>
/// <b>O que esta métrica é e o que não é.</b> A contagem aqui é a de um segmentador próprio (palavra, número e
/// pontuação), não a de um BPE treinado. Ela serve para comparar <em>formatos de contexto entre si</em> sob uma régua
/// estável — que é exatamente o que a infraestrutura de avaliação precisa — e <b>não</b> para afirmar quantos tokens um
/// modelo real gastaria. A contagem em tokens reais, com o tokenizer do pacote, é A34c e fica fora deste lote.
/// </para>
/// <para>
/// A contagem é declarada <see cref="TokenCount.Exact"/> porque o texto inteiro é percorrido de uma vez: é exata para a
/// definição deste contador, no mesmo sentido em que <c>Encode(texto).Count</c> é exata para a de um BPE. Nenhuma soma
/// de blocos acontece aqui, então a distinção de fronteira de <see cref="TokenCount.Add"/> não se aplica.
/// </para>
/// <para>Alocação zero: o texto é varrido como <see cref="ReadOnlySpan{T}"/>, sem <c>Split</c> nem strings intermediárias.</para>
/// </remarks>
public sealed class DeterministicTokenCounter : ITokenCounter
{
    /// <summary>Instância compartilhada; o contador não tem estado.</summary>
    public static DeterministicTokenCounter Instance { get; } = new();

    public TokenCount Count(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return TokenCount.Exact(CountTokens(text));
    }

    /// <summary>Contagem crua, sem envelope; existe para os benchmarks que medem só o custo da varredura.</summary>
    public static int CountTokens(ReadOnlySpan<char> text)
    {
        var tokens = 0;
        var inWord = false;
        foreach (var character in text)
        {
            // Letras, dígitos, '_' e '$' formam uma palavra; qualquer outro glifo visível é um token só dele; espaço
            // apenas encerra a palavra corrente. É a aproximação mais simples que ainda distingue prosa de JSON denso.
            if (char.IsLetterOrDigit(character) || character == '_' || character == '$')
            {
                if (!inWord) tokens++;
                inWord = true;
                continue;
            }
            inWord = false;
            if (!char.IsWhiteSpace(character)) tokens++;
        }
        return tokens;
    }
}
