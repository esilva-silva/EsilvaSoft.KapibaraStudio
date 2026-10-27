using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Application;

/// <summary>
/// Separação entre o autocomplete normal e o autocomplete com IA, conforme <c>docs/auto-complite/configuration.md</c>.
/// É a única fonte dessa decisão: nenhum consumidor recombina as flags. O normal (determinístico) mostra o ghost
/// enquanto o usuário digita e abre a lista com <c>Ctrl+Espaço</c>; a IA nunca participa da digitação e só roda pelo
/// atalho explícito (<c>Ctrl+;</c>).
/// </summary>
public static class InlineCompletionPolicy
{
    /// <summary>traditional.inline = Enabled. As flags legadas <c>Inline*</c> não têm mais efeito.</summary>
    public static bool TraditionalInline(AutocompleteSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.Enabled;
    }

    /// <summary>
    /// ai.explicit = AiCompletionEnabled. Independente de <see cref="AutocompleteSettings.Enabled"/>; só o atalho
    /// explícito consulta esta decisão, e é o único caminho do editor que pode carregar o modelo.
    /// </summary>
    public static bool AiCompletion(AutocompleteSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.AiCompletionEnabled;
    }

    /// <summary>Alguma origem automática está habilitada; falso suspende o pedido antes de qualquer computação.</summary>
    public static bool AnyInline(AutocompleteSettings settings) => TraditionalInline(settings);

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
