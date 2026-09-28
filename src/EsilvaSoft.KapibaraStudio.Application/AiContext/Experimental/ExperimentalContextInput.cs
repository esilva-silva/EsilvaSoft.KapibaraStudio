using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Facts;

namespace EsilvaSoft.KapibaraStudio.Application.AiContext.Experimental;

/// <summary>
/// Entrada única dos quatro formatos experimentais: os fatos já selecionados e a janela sintática do cursor.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por que uma entrada só.</b> A comparação entre formatos só tem sentido se todos receberem exatamente o mesmo
/// material; qualquer diferença de tamanho medida depois é do formato, nunca da seleção. Por isso a divisão
/// prefixo/sufixo também mora aqui, e não em cada contrato.
/// </para>
/// <para>
/// <b>Privacidade.</b> Nada além do que <see cref="AiFactPayload"/> já permite (nomes, tipos lógicos, duas
/// estatísticas e rótulos curtos) e do texto que o usuário digitou na aba chega aqui. Não há caminho para documentos,
/// amostras ou credenciais.
/// </para>
/// </remarks>
public sealed class ExperimentalContextInput
{
    /// <summary>Monta a entrada; <paramref name="window"/> ausente significa aba sem statement algum.</summary>
    public ExperimentalContextInput(AiFactSet facts, EditorWindow? window = null, string language = "", string? fileName = null)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(language);
        Facts = facts;
        Window = window ?? EditorWindow.Empty;
        Language = language;
        FileName = fileName;
        (Prefix, Suffix) = Split(Window);
        Dictionary = facts.Select(fact => fact.Payload.Name).Distinct(StringComparer.Ordinal).ToArray();
    }

    /// <summary>Fatos em ordem de relevância decrescente, como o seletor os entrega.</summary>
    public AiFactSet Facts { get; }

    /// <summary>Janela sintática ao redor do cursor.</summary>
    public EditorWindow Window { get; }

    /// <summary>Linguagem/dialeto da aba; string vazia quando desconhecida.</summary>
    public string Language { get; }

    /// <summary>Nome do arquivo, quando a aba tem um.</summary>
    public string? FileName { get; }

    /// <summary>Texto da janela até o cursor.</summary>
    public string Prefix { get; }

    /// <summary>Cauda do statement atual depois do cursor; vazia quando o cursor está fora de qualquer statement.</summary>
    public string Suffix { get; }

    /// <summary>Nomes distintos dos fatos, na ordem em que aparecem.</summary>
    public IReadOnlyList<string> Dictionary { get; }

    /// <summary>
    /// Parte <see cref="EditorWindow.Render"/> no cursor, com a mesma regra já usada por <see cref="AiContextPipeline"/>:
    /// vizinhos inteiros em ordem de documento, statement atual cortado no cursor, e nunca no meio de um par
    /// substituto UTF-16.
    /// </summary>
    private static (string Prefix, string Suffix) Split(EditorWindow window)
    {
        var prefix = new System.Text.StringBuilder();
        foreach (var statement in window.Preceding) prefix.Append(statement.Text).Append('\n');
        if (window.Current is not { } current) return (prefix.ToString(), "");
        var cut = Math.Clamp(window.CaretOffset - current.Span.Start, 0, current.Text.Length);
        if (cut > 0 && cut < current.Text.Length && char.IsLowSurrogate(current.Text[cut])) cut--;
        return (prefix.Append(current.Text[..cut]).ToString(), current.Text[cut..]);
    }
}
