namespace EsilvaSoft.KapibaraStudio.LocalAi.Core;

public interface ITokenizer
{
    IReadOnlyList<int> Encode(string text);
    string Decode(IEnumerable<int> tokens);

    /// <summary>
    /// Cria um decodificador de uma geração, para transformar tokens em texto conforme saem, sem redecodificar o que
    /// já saiu. A implementação padrão apenas redecodifica tudo, de modo que tokenizadores antigos (e falsos de teste)
    /// continuam corretos; quem tem streaming nativo sobrescreve.
    /// </summary>
    IIncrementalDecoder CreateIncrementalDecoder() => new BatchFallbackIncrementalDecoder(this);
}
