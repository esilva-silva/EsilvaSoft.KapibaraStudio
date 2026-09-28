using System.Globalization;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.SyntaxHighlighting;

namespace EsilvaSoft.KapibaraStudio.Desktop.SyntaxHighlighting;

/// <summary>Bound the native shaper for a pathological single line without changing document offsets or text.</summary>
internal sealed class LongLineElementGenerator(MongoTextEditor editor) : IVisualLineTransformer
{
    internal static (int Start, int End) VisibleRange(DocumentLine line, int caret)
    {
        if (line.Length <= SyntaxHighlightingOptions.LongLineThreshold) return (line.Offset, line.EndOffset);
        var relative = Math.Clamp(caret - line.Offset, 0, line.Length);
        var size = SyntaxHighlightingOptions.LongLineWindowCharacters;
        var start = Math.Clamp(relative - size / 2, 0, line.Length - size);
        return (line.Offset + start, line.Offset + start + size);
    }
    public void Transform(ITextRunConstructionContext context, IList<VisualLineElement> elements)
    {
        var line = context.VisualLine.FirstDocumentLine;
        if (line.Length <= SyntaxHighlightingOptions.LongLineThreshold) return;
        var (start, end) = VisibleRange(line, editor.CaretIndex);
        Hide(end, line.EndOffset, Math.Min(line.EndOffset, end + SyntaxHighlightingOptions.LongLineWindowCharacters / 2));
        Hide(line.Offset, start, Math.Max(line.Offset, start - SyntaxHighlightingOptions.LongLineWindowCharacters / 2));

        void Hide(int from, int to, int target)
        {
            if (from >= to) return;
            SplitAt(to); SplitAt(from);
            var first = -1; var count = 0;
            for (var i = 0; i < elements.Count; i++)
            {
                var offset = line.Offset + elements[i].RelativeTextOffset;
                if (offset >= from && offset < to) { if (first < 0) first = i; count++; }
            }
            if (first >= 0) context.VisualLine.ReplaceElement(first, count, new ElidedText(editor, to - from, target));
        }
        void SplitAt(int offset)
        {
            for (var i = 0; i < elements.Count; i++)
            {
                var element = elements[i];
                var startOffset = line.Offset + element.RelativeTextOffset;
                if (offset > startOffset && offset < startOffset + element.DocumentLength && element.CanSplit)
                { element.Split(element.VisualColumn + offset - startOffset, elements, i); return; }
            }
        }
    }
    private sealed class ElidedText(MongoTextEditor editor, int length, int target)
        : FormattedTextElement(" ⟪" + length.ToString("N0", CultureInfo.GetCultureInfo("pt-BR")) + " caracteres · continuar⟫ ", length)
    {
        public override TextRun CreateTextRun(int startVisualColumn, ITextRunConstructionContext context)
        {
            var properties = context.GlobalTextRunProperties;
            var label = new FormattedText(" ⟪" + DocumentLength.ToString("N0", CultureInfo.GetCultureInfo("pt-BR")) + " caracteres · continuar⟫ ",
                CultureInfo.GetCultureInfo("pt-BR"), FlowDirection.LeftToRight, properties.Typeface,
                properties.FontRenderingEmSize, SyntaxStyles.Brush(editor, SyntaxTokenType.Warning));
            return new FormattedTextRun(new FormattedTextElement(label, DocumentLength), properties);
        }
        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(editor).Properties.IsLeftButtonPressed) return;
            e.Handled = true; editor.CaretIndex = target; editor.SelectionStart = editor.SelectionEnd = target; editor.Focus();
        }
    }
}
