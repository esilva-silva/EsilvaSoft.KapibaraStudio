using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Completion;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Syntax;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Text;

namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core.Facts;

/// <summary>
/// Tetos da janela sintática. Os números não são novos: são os mesmos do contrato <c>editor-context-v1</c> já em
/// produção em <c>AutocompleteContextBuilder</c> — três comandos recentes, 256 caracteres por comando recente e 1024
/// caracteres de janela por bloco. Reaproveitá-los mantém a janela comparável ao que os pacotes SlopCoder já viram.
/// </summary>
public readonly record struct EditorWindowLimits
{
    public EditorWindowLimits(int maximumPrecedingStatements, int maximumCurrentLength, int maximumPrecedingLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumPrecedingStatements);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCurrentLength);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumPrecedingLength);
        MaximumPrecedingStatements = maximumPrecedingStatements;
        MaximumCurrentLength = maximumCurrentLength;
        MaximumPrecedingLength = maximumPrecedingLength;
    }

    /// <summary>
    /// Três vizinhos. É o mesmo <c>Take(3)</c> de <c>RECENT COMMAND</c> do contrato v1: o bastante para o modelo ver o
    /// padrão de uso (conexão, banco, coleção, estilo de filtro) e pouco o bastante para que a janela continue sendo
    /// contexto e não o arquivo inteiro. Também respeita a mitigação de privacidade da Fase 3, que limita quantos
    /// statements do histórico de uma mesma conexão/banco acompanham o pedido.
    /// </summary>
    public const int DefaultMaximumPrecedingStatements = 3;

    /// <summary>1024 caracteres, o mesmo bloco do sufixo/painel de entrada do contrato v1.</summary>
    public const int DefaultMaximumCurrentLength = 1024;

    /// <summary>256 caracteres, o mesmo corte por comando recente do contrato v1.</summary>
    public const int DefaultMaximumPrecedingLength = 256;

    public static EditorWindowLimits Default { get; } =
        new(DefaultMaximumPrecedingStatements, DefaultMaximumCurrentLength, DefaultMaximumPrecedingLength);

    public int MaximumPrecedingStatements { get; }
    public int MaximumCurrentLength { get; }
    public int MaximumPrecedingLength { get; }
}

/// <summary>
/// Constrói a <see cref="EditorWindow"/> do cursor: determinístico, síncrono e sem I/O.
/// </summary>
/// <remarks>
/// <para>
/// <b>Fronteira.</b> Os statements vêm dos filhos da raiz da árvore tolerante, não de contagem de linhas. Quando a
/// árvore recebida é de outra versão (ou não vem), o snapshot é reanalisado aqui mesmo: a janela precisa corresponder
/// ao texto que o usuário está vendo, e usar uma árvore velha deslocaria todos os recortes.
/// </para>
/// <para>
/// <b>Corte do statement atual.</b> Se o statement não cabe, o que se mantém é o trecho encostado no cursor: o modelo
/// continua a escrita no cursor, então descartar o começo de um pipeline gigante custa menos do que descartar a linha
/// que está sendo digitada. A cauda depois do cursor só entra com o que sobrar do teto.
/// </para>
/// <para>
/// <b>Nada de resultados.</b> A única entrada de texto é <see cref="ITextSnapshot"/>. Campos de resultado, amostras e
/// formas locais do <see cref="CompletionContext"/> não são lidos — não há por onde um valor de execução entrar.
/// </para>
/// </remarks>
public sealed class EditorWindowBuilder
{
    private readonly EditorWindowLimits _limits;

    public EditorWindowBuilder(EditorWindowLimits? limits = null) => _limits = limits ?? EditorWindowLimits.Default;

    public EditorWindowLimits Limits => _limits;

    public EditorWindow Build(CompletionContext context, ITextSnapshot snapshot, MongoSyntaxTree? tree = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(snapshot);
        var caret = Math.Clamp(context.ReplaceSpan.End, 0, snapshot.Length);
        var statements = Statements(context, snapshot, tree, cancellationToken);
        if (statements.Count == 0) return EditorWindow.Empty;

        var currentIndex = IndexOfCurrent(statements, caret);
        var current = currentIndex >= 0 ? Cut(snapshot, statements[currentIndex].Span, caret, cancellationToken) : null;
        var firstPreceding = currentIndex >= 0 ? currentIndex : statements.Count;
        var preceding = new List<EditorStatement>(_limits.MaximumPrecedingStatements);
        for (var index = Math.Max(0, firstPreceding - _limits.MaximumPrecedingStatements); index < firstPreceding; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var neighbour = Cut(snapshot, statements[index].Span, caret: -1, cancellationToken);
            if (neighbour is not null) preceding.Add(neighbour);
        }
        return current is null && preceding.Count == 0 ? EditorWindow.Empty : new(current, preceding, caret);
    }

    private static List<MongoSyntaxNode> Statements(CompletionContext context, ITextSnapshot snapshot,
        MongoSyntaxTree? tree, CancellationToken cancellationToken)
    {
        if (tree is null || tree.Version != snapshot.Version)
        {
            var mode = context.Dialect == EditorDialects.AggregationJson ? MongoLexerMode.Json : MongoLexerMode.Script;
            tree = new TolerantParser().Parse(snapshot, mode, cancellationToken);
        }
        var statements = new List<MongoSyntaxNode>(tree.Root.Children.Count);
        foreach (var node in tree.Root.Children)
            if (node.Kind is MongoSyntaxNodeKind.Statement or MongoSyntaxNodeKind.OpaqueStatement && !node.Span.IsEmpty)
                statements.Add(node);
        return statements;
    }

    /// <summary>
    /// Statement que contém o cursor. Um cursor exatamente na fronteira entre dois statements pertence ao que começa
    /// ali (o usuário já está escrevendo o próximo), e um cursor depois do último <c>;</c> não pertence a nenhum.
    /// </summary>
    private static int IndexOfCurrent(List<MongoSyntaxNode> statements, int caret)
    {
        for (var index = 0; index < statements.Count; index++)
            if (statements[index].Span.Contains(caret)) return index;
        for (var index = statements.Count - 1; index >= 0; index--)
            if (statements[index].Span.End == caret) return index;
        return -1;
    }

    // O parâmetro caret vale -1 para um vizinho: o corte então começa no início do statement.
    private EditorStatement? Cut(ITextSnapshot snapshot, TextSpan span, int caret, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (start, end) = Trim(snapshot, span);
        if (end <= start) return null;
        var maximum = caret >= 0 ? _limits.MaximumCurrentLength : _limits.MaximumPrecedingLength;
        var truncated = end - start > maximum;
        if (truncated)
        {
            if (caret >= 0 && caret - start > maximum)
            {
                // O cursor está longe demais do começo: mantém-se a janela que termina nele.
                start = caret - maximum;
                end = caret;
            }
            else end = start + maximum;
        }
        return new(TextSpan.FromBounds(start, end), snapshot.GetText(start, end - start), truncated);
    }

    /// <summary>Espaços de fronteira não são sintaxe; recortá-los mantém a janela estável quando o usuário reformata.</summary>
    private static (int Start, int End) Trim(ITextSnapshot snapshot, TextSpan span)
    {
        var start = Math.Clamp(span.Start, 0, snapshot.Length);
        var end = Math.Clamp(span.End, start, snapshot.Length);
        while (start < end && char.IsWhiteSpace(snapshot[start])) start++;
        while (end > start && char.IsWhiteSpace(snapshot[end - 1])) end--;
        return (start, end);
    }
}
