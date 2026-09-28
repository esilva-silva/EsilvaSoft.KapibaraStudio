using System.Globalization;

namespace EsilvaSoft.KapibaraStudio.LocalAi.Core;

/// <summary>
/// Decide se a concatenação de dois blocos já tokenizados separadamente pode ser aprovada como equivalente a
/// tokenizar o texto concatenado.
/// </summary>
/// <remarks>
/// Regra de ouro da fase: BPE não é composicional, logo <c>Encode(A) + Encode(B) ≠ Encode(A + B)</c> em geral.
/// Quando a fronteira não é aprovada, toda contagem obtida somando contagens de blocos deve ficar
/// <see cref="TokenCount.IsExact"/> falso.
/// </remarks>
public interface ITokenBoundaryOracle
{
    /// <summary>Verdadeiro somente quando a fronteira entre <paramref name="before"/> e <paramref name="after"/> é comprovadamente estável.</summary>
    /// <param name="before">Bloco à esquerda da fronteira (pode ser apenas a cauda dele).</param>
    /// <param name="after">Bloco à direita da fronteira (pode ser apenas a cabeça dele).</param>
    bool IsStableBoundary(ReadOnlySpan<char> before, ReadOnlySpan<char> after);
}

/// <summary>
/// Oráculo conservador em duas etapas, parametrizado pelo tokenizador real do modelo.
/// </summary>
/// <remarks>
/// <para><strong>Etapa 1 — porta estrutural (barata).</strong> A fronteira é descartada de imediato a menos que:</para>
/// <list type="bullet">
///   <item><description>um dos lados esteja vazio (concatenar com vazio nunca muda a tokenização);</description></item>
///   <item><description>o último caractere de <c>before</c> seja espaço em branco ASCII (<c>' '</c>, <c>'\t'</c>, <c>'\r'</c>, <c>'\n'</c>);</description></item>
///   <item><description>nenhum lado termine/comece no meio de um par substituto UTF-16;</description></item>
///   <item><description>o primeiro caractere de <c>after</c> não seja marca combinante nem caractere de largura zero
///     (<c>U+200B</c>–<c>U+200D</c>, <c>U+FEFF</c>), que se ligariam ao grafema anterior.</description></item>
/// </list>
/// <para><strong>Etapa 2 — verificação diferencial (exata na janela).</strong> Com a cauda de <c>before</c> e a cabeça
/// de <c>after</c> limitadas a <see cref="WindowCharacters"/> caracteres (nunca cortando par substituto), compara-se
/// <c>Encode(cauda)</c> seguido de <c>Encode(cabeça)</c> com <c>Encode(cauda + cabeça)</c> <em>identificador a
/// identificador</em>, não apenas por contagem. Só então a fronteira é aprovada.</para>
/// <para><strong>Falsos negativos aceitos</strong> (fronteiras na verdade estáveis, mas rejeitadas): qualquer fronteira
/// que não caia depois de espaço em branco ASCII — por exemplo depois de <c>;</c>, <c>)</c>, <c>}</c> ou de um espaço
/// ideográfico <c>U+3000</c>. O custo é contar um bloco a mais como estimativa, o que só torna o corte mais cauteloso.</para>
/// <para><strong>Falsos positivos</strong> são inaceitáveis e por isso a etapa 2 nunca é dispensada. O resíduo teórico
/// é um merge cujo alcance ultrapasse a janela; ele é contido porque (a) a janela só se fecha depois de espaço em
/// branco e (b) arquiteturalmente uma aprovação indevida afetaria apenas uma contagem de corte — o prompt final é
/// sempre tokenizado inteiro, uma única vez, e nunca montado por concatenação de identificadores em cache.</para>
/// </remarks>
/// <param name="tokenizer">Tokenizador do modelo alvo, usado na verificação diferencial.</param>
/// <param name="windowCharacters">Tamanho máximo, em caracteres, de cada lado da janela comparada.</param>
public sealed class TokenizerBoundaryOracle(ITokenizer tokenizer, int windowCharacters = 32) : ITokenBoundaryOracle
{
    private readonly ITokenizer _tokenizer = tokenizer ?? throw new ArgumentNullException(nameof(tokenizer));
    private readonly int _window = windowCharacters > 0 ? windowCharacters : throw new ArgumentOutOfRangeException(nameof(windowCharacters));

    /// <summary>Quantidade de caracteres considerada de cada lado na verificação diferencial.</summary>
    public int WindowCharacters => _window;

    /// <inheritdoc />
    public bool IsStableBoundary(ReadOnlySpan<char> before, ReadOnlySpan<char> after)
    {
        if (before.IsEmpty || after.IsEmpty) return true;
        if (!PassesStructuralGate(before, after)) return false;
        var tail = Tail(before);
        var head = Head(after);
        var separate = _tokenizer.Encode(tail);
        var separateAfter = _tokenizer.Encode(head);
        var together = _tokenizer.Encode(tail + head);
        if (together.Count != separate.Count + separateAfter.Count) return false;
        for (var i = 0; i < separate.Count; i++) if (together[i] != separate[i]) return false;
        for (var i = 0; i < separateAfter.Count; i++) if (together[separate.Count + i] != separateAfter[i]) return false;
        return true;
    }

    private static bool PassesStructuralGate(ReadOnlySpan<char> before, ReadOnlySpan<char> after)
    {
        var last = before[^1];
        var first = after[0];
        if (char.IsHighSurrogate(last) || char.IsLowSurrogate(first)) return false;
        if (last is not (' ' or '\t' or '\r' or '\n')) return false;
        if (first is '\u200B' or '\u200C' or '\u200D' or '\uFEFF') return false;
        return CharUnicodeInfo.GetUnicodeCategory(first) is not
            (UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark);
    }

    private string Tail(ReadOnlySpan<char> text)
    {
        var start = text.Length <= _window ? 0 : text.Length - _window;
        if (start > 0 && char.IsLowSurrogate(text[start])) start++;
        return text[start..].ToString();
    }

    private string Head(ReadOnlySpan<char> text)
    {
        var end = Math.Min(_window, text.Length);
        if (end < text.Length && char.IsHighSurrogate(text[end - 1])) end--;
        return text[..end].ToString();
    }
}
