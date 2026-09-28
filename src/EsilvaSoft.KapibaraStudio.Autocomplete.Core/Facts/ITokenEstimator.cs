namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core.Facts;

/// <summary>
/// Estimativa barata do custo em tokens de um texto, sem carregar tokenizer nem modelo. Existe para <b>cortar
/// candidatos antes de contar de verdade</b>; a autoridade final sobre o orçamento é a contagem exata do tokenizer do
/// modelo, que vive fora deste projeto.
/// </summary>
public interface ITokenEstimator
{
    /// <summary>Estimativa não negativa, síncrona e offline.</summary>
    int Estimate(string text);
}
