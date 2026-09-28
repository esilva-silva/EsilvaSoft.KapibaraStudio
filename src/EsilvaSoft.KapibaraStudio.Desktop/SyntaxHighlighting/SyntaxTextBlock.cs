using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.SyntaxHighlighting;

namespace EsilvaSoft.KapibaraStudio.Desktop.SyntaxHighlighting;

/// <summary>Bounded scalar values in the existing lazy document tree.</summary>
public sealed class SyntaxTextBlock : TextBlock
{
    private readonly SyntaxHighlightingService _service = new();
    private SyntaxSnapshot? _snapshot;
    protected override Type StyleKeyOverride => typeof(TextBlock);
    protected override TextLayout CreateTextLayout(string? text)
    {
        var source = text ?? "";
        if (source.Length > SyntaxHighlightingOptions.FullHighlightCharacters) source = source[..SyntaxHighlightingOptions.FullHighlightCharacters];
        _snapshot = _service.Highlight(source, SyntaxLanguage.Json, previous: _snapshot);
        return new TextLayout(text, new Typeface(FontFamily, FontStyle, FontWeight, FontStretch), FontSize, Foreground,
            TextAlignment, TextWrapping, TextTrimming, flowDirection: FlowDirection,
            maxWidth: MaxWidth, lineHeight: LineHeight,
            textStyleOverrides: SyntaxStyles.Build(this, _snapshot, new Typeface(FontFamily, FontStyle, FontWeight, FontStretch), FontSize));
    }
}
