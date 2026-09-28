using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.SyntaxHighlighting;
using EsilvaSoft.KapibaraStudio.Desktop.SyntaxHighlighting;

namespace EsilvaSoft.KapibaraStudio.Desktop;

/// <summary>A visual projection only: surrounding text keeps its color and the insertion is secondary.</summary>
public sealed class InlineCompletionTextBlock : TextBlock
{
    public string Suggestion { get; private set; } = "";
    private (string Before, string Suggestion, string After, IBrush Primary, IBrush Secondary, SyntaxSnapshot? Snapshot)? _previous;
    public void Show(string before, string suggestion, string after, IBrush primary, IBrush secondary, SyntaxSnapshot? snapshot = null, int sourceStart = 0)
    {
        var current = (before, suggestion, after, primary, secondary, snapshot);
        if (_previous == current) return;
        _previous = current;
        Suggestion = suggestion;
        Inlines!.Clear();
        if (snapshot is null) { snapshot = new SyntaxHighlightingService().Highlight(before + after, SyntaxLanguage.MongoScript); sourceStart = 0; }
        AddHighlighted(before, primary, snapshot, sourceStart);
        Inlines.Add(new Run(suggestion) { Foreground = secondary });
        AddHighlighted(after, primary, snapshot, sourceStart + before.Length);
    }
    private void AddHighlighted(string text, IBrush primary, SyntaxSnapshot snapshot, int sourceStart)
    {
        var position = 0;
        var low = 0; var high = snapshot.Tokens.Count;
        while (low < high)
        {
            var middle = low + (high - low) / 2; var token = snapshot.Tokens[middle];
            if (token.Start + token.Length <= sourceStart) low = middle + 1; else high = middle;
        }
        for (var index = low; index < snapshot.Tokens.Count; index++)
        {
            var token = snapshot.Tokens[index];
            if (token.Start + token.Length <= sourceStart) continue;
            if (token.Start >= sourceStart + text.Length) break;
            var start = Math.Max(0, token.Start - sourceStart); var end = Math.Min(text.Length, token.Start + token.Length - sourceStart);
            if (start > position) Inlines!.Add(new Run(text[position..start]) { Foreground = primary });
            Inlines!.Add(new Run(text[start..end]) { Foreground = SyntaxStyles.Brush(this, token.Type) });
            position = end;
        }
        if (position < text.Length) Inlines!.Add(new Run(text[position..]) { Foreground = primary });
    }
}
