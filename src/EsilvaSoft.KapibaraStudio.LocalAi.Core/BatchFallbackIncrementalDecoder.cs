namespace EsilvaSoft.KapibaraStudio.LocalAi.Core;

/// <summary>
/// Decodificador incremental para tokenizadores sem streaming nativo: redecodifica a sequência inteira e devolve só o
/// sufixo ainda não entregue.
/// </summary>
/// <remarks>
/// É o comportamento padrão de <see cref="ITokenizer.CreateIncrementalDecoder"/>, para que nenhum tokenizador quebre
/// ao ganharmos streaming; é correto, mas continua custando O(n) por token (O(n²) na geração). Os tokenizadores reais
/// do produto sobrescrevem com decodificação de verdade incremental, e é neles que vale o critério de custo constante.
/// Um par substituto UTF-16 ou um caractere de substituição no fim do texto ficam retidos até o token seguinte: são
/// exatamente as marcas de um caractere que ainda não fechou, e entregá-los poluiria a prévia com lixo transitório.
/// </remarks>
public sealed class BatchFallbackIncrementalDecoder(ITokenizer tokenizer) : IIncrementalDecoder
{
    private readonly ITokenizer _tokenizer = tokenizer ?? throw new ArgumentNullException(nameof(tokenizer));
    private readonly List<int> _tokens = [];
    private int _emitted;

    public string Append(int token)
    {
        _tokens.Add(token);
        var text = _tokenizer.Decode(_tokens);
        var available = text.Length;
        // Cauda provisória: um caractere de substituição ou um substituto alto no fim é o sinal de que a sequência
        // ainda não fechou. Entregá-lo agora mostraria lixo onde o token seguinte traz o caractere de verdade.
        while (available > 0 && (text[available - 1] == '�' || char.IsHighSurrogate(text[available - 1]))) available--;
        if (available <= _emitted) return "";
        var piece = text[_emitted..available];
        _emitted = available;
        return piece;
    }

    public string Flush()
    {
        if (_tokens.Count == 0) return "";
        var text = _tokenizer.Decode(_tokens);
        if (text.Length <= _emitted) return "";
        var piece = text[_emitted..];
        _emitted = text.Length;
        return piece;
    }

    public void Dispose() { _tokens.Clear(); _emitted = 0; }
}
