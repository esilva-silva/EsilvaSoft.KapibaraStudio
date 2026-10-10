using System.Globalization;

namespace EsilvaSoft.KapibaraStudio.Core;

/// <summary>Normalizes the editable token budgets without accepting culture-specific grouping.</summary>
public static class AutocompleteTokenBudget
{
    /// <summary>Fixed Qwen FIM markers included in the model window before editor content.</summary>
    public const int PromptOverheadTokens = 3;

    /// <summary>Calculates the same effective prompt budget used by autocomplete inference and KapiLab capture.</summary>
    public static int EffectiveContextTokens(int requestedContextTokens, int requestedCompletionTokens,
        int effectiveContextLength, int effectiveAutocompleteMaximumTokens)
    {
        if (requestedContextTokens < 64 || requestedCompletionTokens < 1 || effectiveContextLength < 1 ||
            effectiveAutocompleteMaximumTokens < 1)
            throw new ArgumentOutOfRangeException(nameof(requestedContextTokens), "Orçamento do modelo ou da solicitação inválido.");

        var completionTokens = Math.Min(requestedCompletionTokens, effectiveAutocompleteMaximumTokens);
        return Math.Min(requestedContextTokens,
            Math.Max(64, effectiveContextLength - completionTokens - PromptOverheadTokens));
    }

    public static bool TryParse(string? text, out int value)
    {
        value = 0;
        if (string.IsNullOrEmpty(text)) return false;
        foreach (var character in text)
            if (character is < '0' or > '9') return false;
        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    public static string Format(int value) => value.ToString(CultureInfo.InvariantCulture);
}
