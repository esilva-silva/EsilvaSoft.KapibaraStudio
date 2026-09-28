using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Application;

/// <summary>
/// Precedência das opções de autocompletar aplicada à sugestão automática (ghost), conforme
/// <c>docs/auto-complite/configuration.md</c>. É a única fonte dessa decisão: nenhum consumidor recombina as flags.
/// </summary>
public static class InlineCompletionPolicy
{
    /// <summary>traditional.inline = Enabled &amp;&amp; InlineEnabled &amp;&amp; InlineUseTraditional.</summary>
    public static bool TraditionalInline(AutocompleteSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.Enabled && settings.InlineEnabled && settings.InlineUseTraditional;
    }

    /// <summary>
    /// ai.inline = Enabled &amp;&amp; InlineEnabled &amp;&amp; InlineUseAi &amp;&amp; Mode != Basic. A elegibilidade
    /// restante (LoadedOnly e perfil de latência) é verificada pelo chamador dentro da fila, não aqui.
    /// </summary>
    public static bool AiInline(AutocompleteSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.Enabled && settings.InlineEnabled && settings.InlineUseAi && settings.Mode != AutocompleteMode.Basic;
    }

    /// <summary>Alguma origem automática está habilitada; falso suspende o pedido antes de qualquer computação.</summary>
    public static bool AnyInline(AutocompleteSettings settings) => TraditionalInline(settings) || AiInline(settings);

    /// <summary>
    /// Estado do editor e configuração juntos. O automático nunca disputa com a lista explícita, com uma sessão de
    /// snippet, com composição de IME, com seleção ativa ou com um editor sem foco.
    /// </summary>
    public static bool Allows(AutocompleteSettings settings, InlineCompletionEditorState state)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return AnyInline(settings) && state.AllowsAutomaticSuggestion;
    }
}
