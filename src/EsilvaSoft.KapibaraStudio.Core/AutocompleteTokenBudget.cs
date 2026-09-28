using System.Globalization;

namespace EsilvaSoft.KapibaraStudio.Core;

/// <summary>Normalizes the editable token budgets without accepting culture-specific grouping.</summary>
public static class AutocompleteTokenBudget
{
    /// <summary>Fixed Qwen FIM markers included in the model window before editor content.</summary>
    public const int PromptOverheadTokens = 3;

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
