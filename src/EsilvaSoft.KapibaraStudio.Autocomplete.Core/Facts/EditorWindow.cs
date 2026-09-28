using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Text;

namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core.Facts;

/// <summary>
/// Um statement da janela do editor, já recortado do snapshot. <see cref="Text"/> é sempre sintaxe digitada pelo
/// usuário — nunca um valor vindo de execução, porque a única fonte é o texto do documento.
/// </summary>
/// <remarks>
/// <see cref="Truncated"/> não é um detalhe de exibição: um statement cortado deixa de ser um statement inteiro e, por
/// isso, deixa de ser candidato a "statement semelhante" (ver <see cref="SimilarStatementFinder"/>).
/// </remarks>
public sealed record EditorStatement
{
    public EditorStatement(TextSpan span, string text, bool truncated = false)
    {
        ArgumentNullException.ThrowIfNull(text);
        Span = span;
        Text = text;
        Truncated = truncated;
    }

    /// <summary>Posição original no documento; o recorte preserva a origem para diagnóstico e reuso.</summary>
    public TextSpan Span { get; }

    /// <summary>Texto do statement já cortado pelos limites da janela.</summary>
    public string Text { get; }

    /// <summary>Verdadeiro quando o limite de caracteres descartou parte do statement.</summary>
    public bool Truncated { get; }
}

/// <summary>
/// Janela sintática ao redor do cursor: o statement atual mais um punhado de statements anteriores do mesmo documento.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por que não linhas.</b> O recorte é por statement (fronteira estrutural da árvore tolerante) e não por número de
/// linhas: um <c>aggregate([...])</c> formatado ocupa dezenas de linhas e um console inteiro cabe em uma só; contar
/// linhas produziria janelas arbitrárias nos dois casos.
/// </para>
/// <para>
/// <b>Só sintaxe.</b> A janela nasce exclusivamente do texto do documento. Resultados de execução, amostras e painéis
/// de saída não têm caminho até aqui — não é um filtro aplicado depois, é a ausência de entrada.
/// </para>
/// <para>
/// <b>Igualdade por conteúdo.</b> Como em <see cref="AiFactSet"/>, determinismo de prompt é determinismo de sequência:
/// a igualdade compara o statement atual, a ordem dos vizinhos e a posição do cursor.
/// </para>
/// </remarks>
public sealed class EditorWindow : IEquatable<EditorWindow>
{
    private readonly EditorStatement[] _preceding;

    public EditorWindow(EditorStatement? current, IEnumerable<EditorStatement>? preceding = null, int caretOffset = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(caretOffset);
        _preceding = preceding?.ToArray() ?? [];
        if (Array.IndexOf(_preceding, null) >= 0) throw new ArgumentException("Um statement vizinho não pode ser nulo.", nameof(preceding));
        Current = current;
        CaretOffset = caretOffset;
    }

    /// <summary>Janela vazia: documento sem statement algum antes do cursor.</summary>
    public static EditorWindow Empty { get; } = new(null);

    /// <summary>Statement que contém o cursor; ausente quando o cursor não está sobre nenhum statement.</summary>
    public EditorStatement? Current { get; }

    /// <summary>Vizinhos anteriores em ordem de documento (o mais antigo primeiro, o mais próximo do cursor por último).</summary>
    public IReadOnlyList<EditorStatement> Preceding => _preceding;

    /// <summary>Posição do cursor no documento de origem, em unidades UTF-16.</summary>
    public int CaretOffset { get; }

    public bool IsEmpty => Current is null && _preceding.Length == 0;

    /// <summary>Todos os statements em ordem de documento, vizinhos antes do atual.</summary>
    public IEnumerable<EditorStatement> Statements => Current is null ? _preceding : _preceding.Append(Current);

    /// <summary>
    /// Texto da janela, vizinhos primeiro e statement atual por último, separados por uma quebra de linha. É a forma
    /// canônica usada por quem monta o prompt; a mesma janela sempre produz a mesma string.
    /// </summary>
    public string Render() => string.Join("\n", Statements.Select(statement => statement.Text));

    public bool Equals(EditorWindow? other) => other is not null && (ReferenceEquals(this, other)
        || (CaretOffset == other.CaretOffset && Current == other.Current && _preceding.SequenceEqual(other._preceding)));

    public override bool Equals(object? obj) => Equals(obj as EditorWindow);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(CaretOffset);
        hash.Add(Current);
        foreach (var statement in _preceding) hash.Add(statement);
        return hash.ToHashCode();
    }
}
