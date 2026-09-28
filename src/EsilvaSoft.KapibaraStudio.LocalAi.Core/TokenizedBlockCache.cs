namespace EsilvaSoft.KapibaraStudio.LocalAi.Core;

/// <summary>
/// Cache de contagens de blocos de texto já tokenizados para um modelo.
/// </summary>
/// <remarks>
/// <para><strong>Decisão de arquitetura da fase: este cache tem dois usos permitidos e só dois.</strong></para>
/// <list type="number">
///   <item><description><em>Contar-para-cortar</em>: decidir rapidamente se um candidato de contexto provavelmente
///     estoura o orçamento, sem tokenizar tudo de novo a cada candidato descartado. Combinações de blocos consultam o
///     <see cref="ITokenBoundaryOracle"/> e ficam <see cref="TokenCount.IsExact"/> falso quando a fronteira não é
///     aprovada.</description></item>
///   <item><description><em>Contar-para-autorizar</em>: via <see cref="CountAuthoritative(string)"/>, que sempre
///     tokeniza o texto final inteiro, uma vez. O cache nunca é a fonte de verdade do limite.</description></item>
/// </list>
/// <para><strong>O cache nunca concatena identificadores de tokens para produzir o prompt.</strong> Por construção ele
/// descarta os identificadores logo após contar e armazena apenas números; não existe — e não pode passar a existir —
/// membro público que devolva identificadores de tokens. O prompt final é montado pelo construtor de prompt do modelo
/// (por exemplo <c>QwenFimPromptBuilder</c>) a partir de uma tokenização própria, nunca a partir daqui.</para>
/// <para>Instância não é segura para uso concorrente; use uma por operação de montagem de contexto.</para>
/// </remarks>
public sealed class TokenizedBlockCache
{
    private readonly ITokenizer _tokenizer;
    private readonly ITokenBoundaryOracle _oracle;
    private readonly Dictionary<string, int> _blocks = new(StringComparer.Ordinal);
    private readonly Queue<string> _order = new();
    private readonly int _capacity;

    /// <summary>Cria o cache para um tokenizador e um oráculo de fronteira do mesmo modelo.</summary>
    /// <param name="tokenizer">Tokenizador do modelo alvo.</param>
    /// <param name="oracle">Oráculo que aprova (ou não) fronteiras entre blocos.</param>
    /// <param name="capacity">Número máximo de blocos retidos; o mais antigo é descartado ao exceder.</param>
    public TokenizedBlockCache(ITokenizer tokenizer, ITokenBoundaryOracle oracle, int capacity = 512)
    {
        ArgumentNullException.ThrowIfNull(tokenizer);
        ArgumentNullException.ThrowIfNull(oracle);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _tokenizer = tokenizer;
        _oracle = oracle;
        _capacity = capacity;
    }

    /// <summary>Quantas vezes o tokenizador foi realmente chamado por este cache; diagnóstico de reaproveitamento.</summary>
    public int TokenizerCalls { get; private set; }

    /// <summary>Blocos distintos retidos no momento.</summary>
    public int CachedBlocks => _blocks.Count;

    /// <summary>Contagem exata de um bloco isolado, reaproveitando o resultado de uma tokenização anterior do mesmo texto.</summary>
    public TokenCount CountBlock(string block)
    {
        ArgumentNullException.ThrowIfNull(block);
        return TokenCount.Exact(CountCached(block));
    }

    /// <summary>
    /// Contar-para-cortar: soma as contagens dos blocos na ordem dada, consultando o oráculo em cada fronteira.
    /// O resultado só é exato quando todas as fronteiras foram aprovadas.
    /// </summary>
    /// <param name="blocks">Blocos na ordem em que apareceriam no texto concatenado.</param>
    public TokenCount CountCombined(IReadOnlyList<string> blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        if (blocks.Count == 0) return TokenCount.Exact(0);
        var total = CountBlock(blocks[0]);
        for (var i = 1; i < blocks.Count; i++)
        {
            var approved = _oracle.IsStableBoundary(blocks[i - 1], blocks[i]);
            total = total.Add(CountBlock(blocks[i]), approved);
        }
        return total;
    }

    /// <summary>
    /// Contar-para-cortar: verdadeiro quando a combinação cabe no orçamento. Uma contagem não exata só aprova se ficar
    /// dentro do orçamento, porque a soma de blocos nunca subestima o texto concatenado em um BPE guloso.
    /// </summary>
    /// <param name="blocks">Candidato de contexto.</param>
    /// <param name="budgetTokens">Orçamento em tokens.</param>
    /// <param name="count">Contagem obtida, com o indicador de exatidão.</param>
    public bool FitsWithinBudget(IReadOnlyList<string> blocks, int budgetTokens, out TokenCount count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(budgetTokens);
        count = CountCombined(blocks);
        return count.Tokens <= budgetTokens;
    }

    /// <summary>
    /// Contar-para-autorizar: tokeniza o texto final inteiro, sempre, e devolve a contagem exata. Nenhum valor em
    /// cache é reaproveitado aqui — esta é a única contagem que pode autorizar um prompt.
    /// </summary>
    /// <param name="finalText">Texto final, já concatenado.</param>
    public TokenCount CountAuthoritative(string finalText)
    {
        ArgumentNullException.ThrowIfNull(finalText);
        TokenizerCalls++;
        return TokenCount.Exact(_tokenizer.Encode(finalText).Count);
    }

    /// <summary>Esvazia o cache mantendo o contador de diagnóstico.</summary>
    public void Clear()
    {
        _blocks.Clear();
        _order.Clear();
    }

    private int CountCached(string block)
    {
        if (_blocks.TryGetValue(block, out var cached)) return cached;
        TokenizerCalls++;
        // Os identificadores morrem aqui, de propósito: só a contagem sobrevive ao escopo deste método.
        var count = _tokenizer.Encode(block).Count;
        _blocks[block] = count;
        _order.Enqueue(block);
        while (_order.Count > _capacity) _blocks.Remove(_order.Dequeue());
        return count;
    }
}
