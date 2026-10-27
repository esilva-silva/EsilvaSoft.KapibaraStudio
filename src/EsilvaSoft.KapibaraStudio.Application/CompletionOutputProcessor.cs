using EsilvaSoft.KapibaraStudio.Autocomplete.Core;

namespace EsilvaSoft.KapibaraStudio.Application;

/// <summary>
/// Limpeza e validação da saída de um modelo local, compartilhada por todas as modalidades de IA.
/// </summary>
/// <remarks>
/// <para>Este tipo é a extração literal do que vivia dentro de <see cref="AiAutocompleteProvider"/>: o filtro de
/// texto reservado/sensível e a limpeza que remove o eco do sufixo do documento. <see cref="Clean"/> reproduz aquele
/// comportamento byte a byte — o ghost automático da Fase 5.1 continua vendo exatamente o mesmo resultado — e
/// <see cref="CleanStructured"/> acrescenta a parada estrutural de <see cref="StructuralStopDetector"/>, usada pelo
/// caminho explícito (<c>Ctrl+;</c>), onde o candidato é inserido inteiro e um texto sintaticamente quebrado custa
/// caro ao usuário.</para>
/// <para><strong>Privacidade.</strong> Nada aqui persiste, registra ou agrega texto: cada chamada recebe uma
/// <c>string</c>, devolve outra e esquece as duas. O texto reprovado não vira mensagem nem diagnóstico.</para>
/// <para><strong>O candidato é texto.</strong> Nenhum método interpreta, executa ou aciona o que foi gerado; a saída
/// é sempre material para o editor.</para>
/// </remarks>
public static class CompletionOutputProcessor
{
    private static readonly string[] AgentMarkers =
    [
        "<tool_call>", "</tool_call>", "<tool_response>", "</tool_response>",
        "<think>", "</think>", "<tools>", "</tools>"
    ];
    /// <summary>Acima disto a saída não é uma sugestão de editor e é descartada inteira.</summary>
    public const int MaximumCandidateLength = 8192;

    /// <summary>
    /// Texto que nunca pode atravessar a fronteira do modelo, em nenhuma direção: segredos reconhecíveis
    /// (<see cref="CompletionPrivacy"/>) e marcadores especiais de prompt, que um modelo pode ecoar e um contexto
    /// pode tentar injetar.
    /// </summary>
    public static bool ContainsReservedOrSensitiveText(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return CompletionPrivacy.ContainsSensitiveText(source)
            || source.Contains("<|", StringComparison.Ordinal) || source.Contains("<｜", StringComparison.Ordinal)
            || AgentMarkers.Any(marker => source.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Limpeza tradicional: remove o eco do sufixo do documento e devolve <see langword="null"/> para qualquer saída
    /// que não pode chegar ao editor. Comportamento congelado — é o que o ghost automático já usava.
    /// </summary>
    /// <param name="text">Texto gerado pelo modelo.</param>
    /// <param name="suffix">Sufixo real do documento, que o formato FIM costuma repetir antes de divagar.</param>
    public static string? Clean(string text, string suffix)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(suffix);
        // FIM exports can echo the existing suffix and then keep generating unrelated functions.
        // Preserve the actual document suffix instead of inserting a duplicate.
        if (suffix.Length >= 2 && text.IndexOf(suffix, StringComparison.Ordinal) is var suffixIndex && suffixIndex >= 0)
            text = text[..suffixIndex];
        return string.IsNullOrWhiteSpace(text) || text.Length > MaximumCandidateLength || text.Contains('\0')
            || text.Contains("```", StringComparison.Ordinal) || ContainsReservedOrSensitiveText(text) ? null : text;
    }

    /// <summary>
    /// <see cref="Clean"/> seguido da parada estrutural. A ordem importa: as reprovações continuam sendo avaliadas
    /// sobre o texto inteiro, para que um bloco de código cercado ou um segredo no fim da geração reprove a saída em
    /// vez de ser apagado pelo corte.
    /// </summary>
    /// <param name="text">Texto gerado pelo modelo.</param>
    /// <param name="suffix">Sufixo real do documento.</param>
    /// <param name="truncated">Verdadeiro quando a parada estrutural encurtou o candidato.</param>
    /// <returns>O candidato final, ou <see langword="null"/> quando nada sobrou de utilizável.</returns>
    public static string? CleanStructured(string text, string suffix, out bool truncated)
    {
        truncated = false;
        if (Clean(text, suffix) is not { } cleaned) return null;
        var stopped = StructuralStopDetector.Truncate(cleaned);
        truncated = stopped.Length != cleaned.Length;
        return string.IsNullOrWhiteSpace(stopped) ? null : stopped;
    }
}

/// <summary>
/// Onde a geração atravessa uma fronteira estrutural que não deveria.
/// </summary>
/// <remarks>
/// <para>O detector é deliberadamente sintático e local: ele não entende MQL, apenas acompanha aspas, comentários e
/// pares de delimitadores <em>abertos dentro do candidato</em>. Um fechamento sem abertura correspondente não é
/// erro — é o caso normal de completar <c>db.c.find({ </c> com <c>name: 1 })</c>, em que o candidato fecha o que o
/// documento abriu.</para>
/// <para>Três paradas, nesta ordem de descoberta:</para>
/// <list type="number">
/// <item><description><strong>Fim de statement.</strong> Um <c>;</c> fora de texto/comentário e sem delimitador
/// aberto termina o statement; o que vem depois é um statement novo que o modelo inventou, e o corte preserva o
/// próprio <c>;</c>.</description></item>
/// <item><description><strong>Abertura sem fechamento.</strong> Ao fim do candidato, qualquer <c>{</c>, <c>[</c> ou
/// <c>(</c> aberto e não fechado corta o texto no primeiro deles: o que sobra é o maior prefixo estruturalmente
/// íntegro.</description></item>
/// <item><description><strong>Texto ou comentário aberto.</strong> Uma aspa ou um <c>/*</c> que nunca fecha corta no
/// ponto em que começou, pela mesma razão.</description></item>
/// </list>
/// <para>Um fechamento que não casa com a abertura mais recente (<c>{ ... )</c>) é tratado como abertura sem
/// fechamento: o corte acontece na abertura pendente.</para>
/// </remarks>
public static class StructuralStopDetector
{
    /// <summary>Índice em que o candidato deve ser cortado; <c>text.Length</c> quando nada precisa ser cortado.</summary>
    public static int FindStop(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var open = new List<(char Delimiter, int Index)>();
        for (var i = 0; i < text.Length; i++)
        {
            var current = text[i];
            switch (current)
            {
                case '"' or '\'' or '`':
                    var end = SkipString(text, i, current);
                    if (end < 0) return i;
                    i = end;
                    continue;
                case '/' when i + 1 < text.Length && text[i + 1] == '/':
                    var newline = text.IndexOf('\n', i + 2);
                    if (newline < 0) return text.Length;
                    i = newline;
                    continue;
                case '/' when i + 1 < text.Length && text[i + 1] == '*':
                    var close = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    if (close < 0) return i;
                    i = close + 1;
                    continue;
                case '{' or '[' or '(':
                    open.Add((current, i));
                    continue;
                case '}' or ']' or ')':
                    if (open.Count == 0) continue; // Fecha algo que o documento abriu antes do cursor: legítimo.
                    if (open[^1].Delimiter != Opening(current)) return open[0].Index;
                    open.RemoveAt(open.Count - 1);
                    continue;
                case ';' when open.Count == 0:
                    return i + 1;
                default:
                    continue;
            }
        }

        return open.Count == 0 ? text.Length : open[0].Index;
    }

    /// <summary>Candidato cortado na primeira fronteira estrutural violada, sem espaço à direita.</summary>
    public static string Truncate(string text)
    {
        var stop = FindStop(text);
        return (stop >= text.Length ? text : text[..stop]).TrimEnd();
    }

    private static char Opening(char closing) => closing switch { '}' => '{', ']' => '[', _ => '(' };

    /// <summary>Índice da aspa de fechamento, ou negativo quando o texto termina com a sequência aberta.</summary>
    private static int SkipString(string text, int start, char quote)
    {
        for (var i = start + 1; i < text.Length; i++)
        {
            if (text[i] == '\\') { i++; continue; }
            if (text[i] == quote) return i;
            // Uma quebra de linha fecha um literal de aspas simples/duplas em JavaScript apenas por erro de sintaxe;
            // tratar como não fechado é o que o corte precisa saber.
            if (text[i] == '\n' && quote != '`') return -1;
        }

        return -1;
    }
}
