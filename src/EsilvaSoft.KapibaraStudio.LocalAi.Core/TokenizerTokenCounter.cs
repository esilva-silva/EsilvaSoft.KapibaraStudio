namespace EsilvaSoft.KapibaraStudio.LocalAi.Core;

/// <summary>
/// Contador exato sobre um <see cref="ITokenizer"/> real.
/// </summary>
/// <remarks>
/// <para>
/// Contar tokens é literalmente <c>Encode(texto).Count</c>: <strong>não existe atalho</strong>. Qualquer razão
/// caracteres/token é estimativa e nunca substitui a tokenização quando o objetivo é autorizar um prompt.
/// </para>
/// <para>
/// O <see cref="TokenizedBlockCache"/> apenas evita repetir esse trabalho para o <em>mesmo</em> bloco de texto;
/// ele nunca serve para deduzir a contagem de uma combinação nova de blocos, porque o BPE não é composicional.
/// </para>
/// </remarks>
/// <param name="tokenizer">Tokenizador do modelo alvo.</param>
public sealed class TokenizerTokenCounter(ITokenizer tokenizer) : ITokenCounter
{
    private readonly ITokenizer _tokenizer = tokenizer ?? throw new ArgumentNullException(nameof(tokenizer));

    /// <summary>Tokeniza o texto inteiro e devolve a contagem exata.</summary>
    public TokenCount Count(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return TokenCount.Exact(_tokenizer.Encode(text).Count);
    }
}
