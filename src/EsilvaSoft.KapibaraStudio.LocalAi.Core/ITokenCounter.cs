namespace EsilvaSoft.KapibaraStudio.LocalAi.Core;

/// <summary>Conta tokens de um texto para um modelo específico, sem produzir nem expor identificadores de tokens.</summary>
/// <remarks>
/// A interface devolve apenas contagens de propósito: quem precisa dos identificadores usa <see cref="ITokenizer"/>
/// diretamente. Assim nenhum consumidor consegue montar um prompt concatenando resultados de contagem.
/// </remarks>
public interface ITokenCounter
{
    /// <summary>Conta os tokens de <paramref name="text"/>.</summary>
    TokenCount Count(string text);
}
