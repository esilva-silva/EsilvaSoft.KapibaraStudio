namespace EsilvaSoft.KapibaraStudio.LocalAi.Core;

/// <summary>
/// Decodificador incremental de uma única sequência de geração: cada token entra uma vez e sai só o texto novo.
/// Existe para que o custo de decodificação de um passo não dependa de quantos tokens já foram gerados — o padrão
/// anterior (decodificar a sequência inteira a cada token) é quadrático no total da geração.
/// </summary>
/// <remarks>
/// Um decodificador é de uso exclusivo de uma geração: guarda estado (bytes pendentes, estado do stream nativo) e
/// nunca deve ser compartilhado entre pedidos nem entre threads.
/// </remarks>
public interface IIncrementalDecoder : IDisposable
{
    /// <summary>
    /// Aceita o próximo token e devolve apenas o texto novo já completo. Devolve vazio quando o token só contribui
    /// com parte de um caractere (sequência UTF-8 multibyte partida entre tokens), caso em que o texto sai junto com
    /// o token que a fecha.
    /// </summary>
    string Append(int token);

    /// <summary>
    /// Encerra a sequência e devolve o que restou pendente. Usado no fim da geração para que a concatenação dos
    /// pedaços seja idêntica à decodificação em lote, inclusive quando a saída termina em bytes incompletos.
    /// </summary>
    string Flush();
}
