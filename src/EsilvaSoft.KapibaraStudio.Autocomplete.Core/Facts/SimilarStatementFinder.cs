using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Syntax;

namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core.Facts;

/// <summary>Resultado da busca: o statement inteiro escolhido, sua posição no histórico e a similaridade medida.</summary>
public sealed record SimilarStatement(int Index, string Text, double Similarity);

/// <summary>
/// Encontra, no histórico recente, <b>um</b> statement parecido com o que está sendo escrito — ou nenhum.
/// </summary>
/// <remarks>
/// <para>
/// <b>Inteiro ou nada.</b> Não existe "parcialmente semelhante" como resultado: ou um statement inteiro entra no
/// contexto, ou nada entra. Um statement que já chegou cortado pela janela (<see cref="EditorStatement.Truncated"/>)
/// não é candidato, justamente porque um fragmento não é um exemplo íntegro de uso.
/// </para>
/// <para>
/// <b>Tokens, não palavras.</b> A comparação usa o <see cref="MongoLexer"/> da própria linguagem. Um
/// <c>Split(' ')</c> trataria <c>db.pedidos.find({status:1})</c> como um token só e perderia exatamente a pontuação
/// estrutural (<c>.</c>, <c>(</c>, <c>{</c>) que distingue uma consulta de outra.
/// </para>
/// <para>
/// <b>Literais normalizados.</b> Strings, templates, regex e números viram um marcador da categoria. O que se procura
/// é padrão de uso, não coincidência de valores — e, de quebra, nenhum valor digitado influencia a escolha.
/// Comentários são ignorados: são prosa, não comando.
/// </para>
/// <para>
/// <b>Desempate.</b> Entre candidatos com a mesma similaridade vence o mais recente, isto é, o de maior índice no
/// histórico (que é cronológico, do mais antigo para o mais novo). A comparação de similaridades é feita por
/// multiplicação cruzada de inteiros, então empates são empates de verdade e não acidente de arredondamento.
/// </para>
/// </remarks>
public sealed class SimilarStatementFinder
{
    /// <summary>Limiar de Jaccard abaixo do qual nada é devolvido.</summary>
    public const double DefaultMinimumSimilarity = 0.3;

    private readonly double _minimumSimilarity;

    public SimilarStatementFinder(double minimumSimilarity = DefaultMinimumSimilarity)
    {
        if (!(minimumSimilarity >= 0 && minimumSimilarity <= 1))
            throw new ArgumentOutOfRangeException(nameof(minimumSimilarity), minimumSimilarity, "O limiar de Jaccard está entre 0 e 1.");
        _minimumSimilarity = minimumSimilarity;
    }

    public double MinimumSimilarity => _minimumSimilarity;

    /// <summary>Busca entre os vizinhos da janela; o statement atual é a consulta.</summary>
    public SimilarStatement? Find(EditorWindow window, MongoLexerMode mode = MongoLexerMode.Script,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (window.Current is not { } current) return null;
        var history = new string?[window.Preceding.Count];
        for (var index = 0; index < history.Length; index++)
        {
            var neighbour = window.Preceding[index];
            history[index] = neighbour.Truncated ? null : neighbour.Text;
        }
        return Find(current.Text, history, mode, cancellationToken);
    }

    /// <summary>
    /// Busca em um histórico cronológico (mais antigo primeiro). Entradas nulas ou vazias são posições que existem mas
    /// não concorrem, de modo que o índice devolvido continua sendo o índice do histórico recebido.
    /// </summary>
    public SimilarStatement? Find(string current, IReadOnlyList<string?> history, MongoLexerMode mode = MongoLexerMode.Script,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(history);
        var query = Tokens(current, mode, cancellationToken);
        if (query.Count == 0) return null;

        var bestIndex = -1;
        long bestIntersection = 0, bestUnion = 1;
        for (var index = 0; index < history.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (history[index] is not { Length: > 0 } candidate) continue;
            var tokens = Tokens(candidate, mode, cancellationToken);
            if (tokens.Count == 0) continue;
            long intersection = tokens.Count(query.Contains);
            var union = query.Count + tokens.Count - intersection;
            if (union <= 0 || (double)intersection / union < _minimumSimilarity) continue;
            // Igualdade aqui é o empate; percorrer em ordem crescente e aceitar o igual faz vencer o mais recente.
            if (bestIndex >= 0 && intersection * bestUnion < bestIntersection * union) continue;
            bestIndex = index;
            bestIntersection = intersection;
            bestUnion = union;
        }
        return bestIndex < 0 ? null : new(bestIndex, history[bestIndex]!, (double)bestIntersection / bestUnion);
    }

    /// <summary>Coeficiente de Jaccard sobre os tokens normalizados dos dois statements; 0 quando um deles não tem token.</summary>
    public static double Similarity(string left, string right, MongoLexerMode mode = MongoLexerMode.Script,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        var first = Tokens(left, mode, cancellationToken);
        var second = Tokens(right, mode, cancellationToken);
        if (first.Count == 0 || second.Count == 0) return 0;
        double intersection = second.Count(first.Contains);
        return intersection / (first.Count + second.Count - intersection);
    }

    private static HashSet<string> Tokens(string text, MongoLexerMode mode, CancellationToken cancellationToken)
    {
        var lexed = new List<MongoToken>();
        MongoLexer.Tokenize(text.AsSpan(), lexed, mode: mode, cancellationToken: cancellationToken);
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        foreach (var token in lexed)
        {
            if (token.IsComment) continue;
            var key = token.Kind switch
            {
                MongoTokenKind.String => "«string»",
                MongoTokenKind.Template => "«template»",
                MongoTokenKind.Regex => "«regex»",
                MongoTokenKind.Number => "«number»",
                _ => text.Substring(token.Start, Math.Min(token.Length, text.Length - token.Start))
            };
            if (key.Length > 0) tokens.Add(key);
        }
        return tokens;
    }
}
