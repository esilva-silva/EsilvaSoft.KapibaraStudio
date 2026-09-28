namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core.Facts;

/// <summary>
/// Envelope de orçamento de um pedido à IA: quanto cabe na janela, quanto fica reservado para a geração e quanto o
/// formato do prompt consome antes de qualquer fato. É só o envelope — a contagem exata de tokens pertence ao
/// tokenizer real, fora deste projeto.
/// </summary>
public readonly record struct AiBudget
{
    public AiBudget(int contextTokens, int generationTokens = 0, int overheadTokens = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(contextTokens);
        ArgumentOutOfRangeException.ThrowIfNegative(generationTokens);
        ArgumentOutOfRangeException.ThrowIfNegative(overheadTokens);
        if (generationTokens + (long)overheadTokens > contextTokens)
            throw new ArgumentOutOfRangeException(nameof(generationTokens), generationTokens, "Reserva de geração e overhead não cabem na janela de contexto.");
        ContextTokens = contextTokens;
        GenerationTokens = generationTokens;
        OverheadTokens = overheadTokens;
    }

    /// <summary>Janela total do modelo.</summary>
    public int ContextTokens { get; }
    /// <summary>Reserva para a resposta; nunca é ocupada por contexto.</summary>
    public int GenerationTokens { get; }
    /// <summary>Custo fixo do formato (marcadores, cabeçalho) descontado antes dos fatos.</summary>
    public int OverheadTokens { get; }

    /// <summary>O que sobra de fato para os fatos e a janela do editor.</summary>
    public int AvailableTokens => ContextTokens - GenerationTokens - OverheadTokens;

    public bool Fits(int tokens) => tokens >= 0 && tokens <= AvailableTokens;

    /// <summary>Saldo depois de gastar <paramref name="usedTokens"/>; negativo significa estouro.</summary>
    public int Remaining(int usedTokens) => AvailableTokens - usedTokens;
}
