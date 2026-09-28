using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Utilities;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.SyntaxHighlighting;

namespace EsilvaSoft.KapibaraStudio.Desktop.SyntaxHighlighting;

public static class SyntaxStyles
{
    public static string ResourceKey(SyntaxTokenType type) => "Syntax." + (type == SyntaxTokenType.PropertyName ? "Property" : type.ToString());
    public static IBrush Brush(Control control, SyntaxTokenType type) =>
        control.TryFindResource(ResourceKey(type), control.ActualThemeVariant, out var value) && value is IBrush brush
            ? brush : throw new InvalidOperationException("Recurso de sintaxe ausente: " + ResourceKey(type));

    public static IReadOnlyList<ValueSpan<TextRunProperties>> Build(Control control, SyntaxSnapshot snapshot, Typeface typeface,
        double fontSize, int selectionStart = 0, int selectionEnd = 0, IBrush? selectionBrush = null,
        int caret = -1, int visibleStart = 0, int visibleEnd = int.MaxValue)
    {
        var result = new List<ValueSpan<TextRunProperties>>();
        var start = Math.Min(selectionStart, selectionEnd); var end = Math.Max(selectionStart, selectionEnd);
        var bracket = snapshot.Brackets.ContainsKey(caret) ? caret : snapshot.Brackets.ContainsKey(caret - 1) ? caret - 1 : -1;
        var partner = bracket >= 0 ? snapshot.Brackets[bracket] : -1;
        var cache = new Dictionary<SyntaxTokenType, TextRunProperties>();
        var low = 0; var high = snapshot.Tokens.Count;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            var token = snapshot.Tokens[middle];
            if (token.Start + token.Length < visibleStart) low = middle + 1; else high = middle;
        }
        for (var index = low; index < snapshot.Tokens.Count; index++)
        {
            var token = snapshot.Tokens[index];
            if (token.Start + token.Length < visibleStart) continue;
            if (token.Start > visibleEnd || result.Count >= SyntaxHighlightingOptions.MaximumStyledTokens) break;
            var type = token.Type;
            if (token.Start == bracket || token.Start == partner)
                type = partner < 0 ? SyntaxTokenType.UnmatchedBracket : SyntaxTokenType.MatchingBracket;
            if (!cache.TryGetValue(type, out var properties))
            {
                properties = new GenericTextRunProperties(typeface, fontSize,
                    textDecorations: type is SyntaxTokenType.MatchingBracket or SyntaxTokenType.UnmatchedBracket
                        ? new TextDecorationCollection { new TextDecoration { Location = TextDecorationLocation.Underline } } : null,
                    foregroundBrush: Brush(control, type));
                cache.Add(type, properties);
            }
            var a = token.Start; var b = a + token.Length;
            if (selectionBrush is null || end <= a || start >= b) result.Add(new(a, token.Length, properties));
            else
            {
                if (a < start) result.Add(new(a, start - a, properties));
                var selectedStart = Math.Max(start, a); var selectedEnd = Math.Min(end, b);
                result.Add(new(selectedStart, selectedEnd - selectedStart, new GenericTextRunProperties(typeface, fontSize, foregroundBrush: selectionBrush)));
                if (end < b) result.Add(new(end, b - end, properties));
            }
        }
        return result;
    }
}
