using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Interactivity;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.SyntaxHighlighting;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

namespace EsilvaSoft.KapibaraStudio.Desktop.SyntaxHighlighting;

/// <summary>MongoDB editor adapter: bindable text and selection, with native virtualized visual lines.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "Avalonia detach cancels/disposes work; each editor owns its own cancellation.")]
public sealed class MongoTextEditor : TextEditor
{
    public static readonly StyledProperty<string> TextProperty = AvaloniaProperty.Register<MongoTextEditor, string>(nameof(Text), "", defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<int> CaretIndexProperty = AvaloniaProperty.Register<MongoTextEditor, int>(nameof(CaretIndex));
    public static readonly StyledProperty<int> SelectionStartProperty = AvaloniaProperty.Register<MongoTextEditor, int>(nameof(SelectionStart));
    public static readonly StyledProperty<int> SelectionEndProperty = AvaloniaProperty.Register<MongoTextEditor, int>(nameof(SelectionEnd));
    public static readonly StyledProperty<double> LineHeightProperty = AvaloniaProperty.Register<MongoTextEditor, double>(nameof(LineHeight), 21);
    public static readonly StyledProperty<bool> AcceptsReturnProperty = AvaloniaProperty.Register<MongoTextEditor, bool>(nameof(AcceptsReturn));
    public static readonly StyledProperty<bool> AcceptsTabProperty = AvaloniaProperty.Register<MongoTextEditor, bool>(nameof(AcceptsTab));
    public static readonly StyledProperty<TextWrapping> TextWrappingProperty = AvaloniaProperty.Register<MongoTextEditor, TextWrapping>(nameof(TextWrapping));
    public static readonly StyledProperty<string> PlaceholderTextProperty = AvaloniaProperty.Register<MongoTextEditor, string>(nameof(PlaceholderText), "");
    private static readonly SyntaxHighlightingService Service = new();
    private bool _initialized, _updatingText, _updatingSelection, _attached;
    private CancellationTokenSource? _pending;
    private INotifyPropertyChanged? _viewModel;
    public new string Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value ?? ""); }
    public int CaretIndex { get => GetValue(CaretIndexProperty); set => SetValue(CaretIndexProperty, Math.Clamp(value, 0, Document.TextLength)); }
    public new int SelectionStart { get => GetValue(SelectionStartProperty); set => SetValue(SelectionStartProperty, Math.Clamp(value, 0, Document.TextLength)); }
    public int SelectionEnd { get => GetValue(SelectionEndProperty); set => SetValue(SelectionEndProperty, Math.Clamp(value, 0, Document.TextLength)); }
    public double LineHeight { get => GetValue(LineHeightProperty); set => SetValue(LineHeightProperty, value); }
    public bool AcceptsReturn { get => GetValue(AcceptsReturnProperty); set => SetValue(AcceptsReturnProperty, value); }
    public bool AcceptsTab { get => GetValue(AcceptsTabProperty); set => SetValue(AcceptsTabProperty, value); }
    public TextWrapping TextWrapping { get => GetValue(TextWrappingProperty); set => SetValue(TextWrappingProperty, value); }
    public string PlaceholderText { get => GetValue(PlaceholderTextProperty); set => SetValue(PlaceholderTextProperty, value); }
    public SyntaxSnapshot? Snapshot { get; private set; }
    public Task HighlightingTask { get; private set; } = Task.CompletedTask;
    /// <summary>Raised when the platform starts, updates, or ends an IME preedit composition.</summary>
    public event EventHandler<bool>? ImeCompositionChanged;
    private TrackingTextInputMethodClient? _imeClient;
    protected override Type StyleKeyOverride => typeof(TextEditor);

    public MongoTextEditor()
    {
        _initialized = true;
        Focusable = true;
        Options.ConvertTabsToSpaces = false;
        Options.AcceptsTab = AcceptsTab;
        Options.EnableHyperlinks = false;
        Options.EnableEmailHyperlinks = false;
        TextArea.TextView.LineTransformers.Add(new MongoColorizer(this));
        TextArea.TextView.LineTransformers.Add(new LongLineElementGenerator(this));
        TextArea.Caret.PositionChanged += (_, _) =>
        {
            _updatingSelection = true;
            try { SetCurrentValue(CaretIndexProperty, CaretOffset); }
            finally { _updatingSelection = false; }
            TextArea.TextView.Redraw();
        };
        TextArea.SelectionChanged += (_, _) =>
        {
            if (_updatingSelection) return;
            _updatingSelection = true;
            try
            {
                var segment = TextArea.Selection.SurroundingSegment;
                SetCurrentValue(SelectionStartProperty, segment?.Offset ?? CaretOffset);
                SetCurrentValue(SelectionEndProperty, segment?.EndOffset ?? CaretOffset);
            }
            finally { _updatingSelection = false; }
        };
        ActualThemeVariantChanged += (_, _) => { ApplyTheme(); TextArea.TextView.Redraw(); };
        DataContextChanged += (_, _) => { BindContext(); RefreshHighlighting(); };
        AttachedToVisualTree += (_, _) => { _attached = true; BindContext(); ApplyTheme(); UpdateLineHeight(); RefreshHighlighting(); };
        DetachedFromVisualTree += (_, _) =>
        {
            _attached = false; _pending?.Cancel(); _pending?.Dispose(); _pending = null;
            if (_viewModel is not null) _viewModel.PropertyChanged -= ContextChanged;
            _viewModel = null; Snapshot = null;
        };
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Enter && !AcceptsReturn) e.Handled = true;
            if (e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Alt) && e.Key is Key.Left or Key.Right)
            {
                var line = Document.GetLineByOffset(CaretIndex);
                if (line.Length > SyntaxHighlightingOptions.LongLineThreshold)
                {
                    e.Handled = true;
                    CaretIndex = Math.Clamp(CaretIndex + (e.Key == Key.Right ? 1 : -1) * SyntaxHighlightingOptions.LongLineWindowCharacters / 2, line.Offset, line.EndOffset);
                    SelectionStart = SelectionEnd = CaretIndex;
                }
            }
        }, RoutingStrategies.Tunnel);
        // AvaloniaEdit provides the platform text-input client. Wrap only its preedit calls so the editor can publish
        // composition state without changing how text, selection, or candidate-window geometry are handled.
        AddHandler(InputElement.TextInputMethodClientRequestedEvent, (_, e) =>
        {
            if (e.Client is null) return;
            if (_imeClient is null || !ReferenceEquals(_imeClient.Inner, e.Client))
                _imeClient = new TrackingTextInputMethodClient(e.Client, composing => ImeCompositionChanged?.Invoke(this, composing));
            e.Client = _imeClient;
        }, RoutingStrategies.Bubble, handledEventsToo: true);
    }
    private void ApplyTheme()
    {
        if (!_attached) return;
        TextArea.SelectionBrush = SyntaxStyles.Brush(this, SyntaxTokenType.SearchMatch);
        TextArea.SelectionForeground = SyntaxStyles.Brush(this, SyntaxTokenType.Default);
        if (this.TryFindResource("SelectionBrush", ActualThemeVariant, out var selection) && selection is IBrush brush) TextArea.SelectionBrush = brush;
        SearchResultsBrush = SyntaxStyles.Brush(this, SyntaxTokenType.SearchMatch);
    }
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        // Search UI is not part of this increment; avoid installing a second, untranslated command surface.
        SearchPanel.Uninstall();
    }
    private void UpdateLineHeight()
    {
        if (!_attached) return;
        var natural = TextArea.TextView.DefaultLineHeight / Options.LineHeightFactor;
        if (natural > 0) Options.LineHeightFactor = Math.Max(1, LineHeight / natural);
    }
    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        if (!_initialized || _updatingText) return;
        _updatingText = true;
        try { SetCurrentValue(TextProperty, base.Text); }
        finally { _updatingText = false; }
        RefreshHighlighting();
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (!_initialized) return;
        if (change.Property == TextProperty && !_updatingText)
        {
            _updatingText = true;
            try { if (base.Text != Text) base.Text = Text; }
            finally { _updatingText = false; }
            RefreshHighlighting();
        }
        if (change.Property == CaretIndexProperty && !_updatingSelection)
        {
            CaretOffset = Math.Clamp(CaretIndex, 0, Document.TextLength);
            TextArea.Caret.BringCaretToView();
        }
        if ((change.Property == SelectionStartProperty || change.Property == SelectionEndProperty) && !_updatingSelection)
        {
            _updatingSelection = true;
            try { TextArea.Selection = Selection.Create(TextArea, Math.Clamp(SelectionStart, 0, Document.TextLength), Math.Clamp(SelectionEnd, 0, Document.TextLength)); }
            finally { _updatingSelection = false; }
        }
        if (change.Property == TextWrappingProperty) WordWrap = TextWrapping == TextWrapping.Wrap;
        if (change.Property == AcceptsTabProperty) Options.AcceptsTab = AcceptsTab;
        if (change.Property == PlaceholderTextProperty) Watermark = PlaceholderText;
        if (change.Property == LineHeightProperty || change.Property == FontSizeProperty || change.Property == FontFamilyProperty) UpdateLineHeight();
        if (change.Property == SyntaxSettings.LanguageProperty) RefreshHighlighting();
    }
    private void BindContext()
    {
        if (_viewModel is not null) _viewModel.PropertyChanged -= ContextChanged;
        _viewModel = _attached ? DataContext as INotifyPropertyChanged : null;
        if (_viewModel is not null) _viewModel.PropertyChanged += ContextChanged;
    }
    private void ContextChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is "Profile" or "Database" or "Collection" or "Mode" or "SyntaxContext") RefreshHighlighting();
    }

    private sealed class TrackingTextInputMethodClient(TextInputMethodClient inner, Action<bool> compositionChanged) : TextInputMethodClient
    {
        public TextInputMethodClient Inner { get; } = inner;
        public override Visual TextViewVisual => Inner.TextViewVisual;
        public override bool SupportsPreedit => Inner.SupportsPreedit;
        public override bool SupportsSurroundingText => Inner.SupportsSurroundingText;
        public override string SurroundingText => Inner.SurroundingText;
        public override Rect CursorRectangle => Inner.CursorRectangle;
        public override TextSelection Selection { get => Inner.Selection; set => Inner.Selection = value; }
        public override void SetPreeditText(string? text)
        {
            Inner.SetPreeditText(text!);
            compositionChanged(!string.IsNullOrEmpty(text));
        }
        public override void SetPreeditText(string? text, int? cursor)
        {
            Inner.SetPreeditText(text!, cursor);
            compositionChanged(!string.IsNullOrEmpty(text));
        }
        public override void ExecuteContextMenuAction(ContextMenuAction action) => Inner.ExecuteContextMenuAction(action);
    }
    public void RefreshHighlighting()
    {
        if (!_attached) return;
        _pending?.Cancel(); _pending?.Dispose(); _pending = new();
        var text = Text; var language = SyntaxSettings.GetLanguage(this); var context = SyntaxContext.Empty;
        if (DataContext is WorkspaceTabViewModel tab)
        {
            context = tab.CaptureSyntaxContext();
            if (language == SyntaxLanguage.MongoScript && tab.Mode == "Agregação") language = SyntaxLanguage.Aggregation;
        }
        if (Snapshot is { } last && last.Context.Connection == context.Connection && last.Context.Database == context.Database
            && last.Context.Collection == context.Collection && last.Context.Names.SequenceEqual(context.Names)) context = last.Context;
        HighlightingTask = UpdateAsync(text, language, context, Snapshot, _pending.Token);
    }
    private async Task UpdateAsync(string text, SyntaxLanguage language, SyntaxContext context, SyntaxSnapshot? previous, CancellationToken token)
    {
        try
        {
            await Task.Delay(SyntaxHighlightingOptions.DebounceMilliseconds, token);
            var result = await Task.Run(() => Service.Highlight(text, language, context, previous, token), token);
            if (token.IsCancellationRequested || !_attached || Text != text) return;
            Snapshot = result; TextArea.TextView.Redraw();
        }
        catch (OperationCanceledException) { }
    }
    public Point PositionInTextView(int offset)
    {
        var view = TextArea.TextView;
        return view.GetVisualPosition(new TextViewPosition(Document.GetLocation(Math.Clamp(offset, 0, Document.TextLength))), VisualYPosition.LineTop) - view.ScrollOffset;
    }
    public (int Start, int End) VisibleLineRange(int offset) =>
        LongLineElementGenerator.VisibleRange(Document.GetLineByOffset(Math.Clamp(offset, 0, Document.TextLength)), CaretIndex);
    private sealed class MongoColorizer(MongoTextEditor editor) : DocumentColorizingTransformer
    {
        protected override void ColorizeLine(DocumentLine line)
        {
            if (editor.Snapshot is not { } snapshot || !ReferenceEquals(snapshot.Text, editor.Text)) return;
            var visible = LongLineElementGenerator.VisibleRange(line, editor.CaretIndex);
            var low = 0; var high = snapshot.Tokens.Count;
            while (low < high)
            {
                var middle = low + (high - low) / 2; var token = snapshot.Tokens[middle];
                if (token.Start + token.Length <= visible.Start) low = middle + 1; else high = middle;
            }
            var caret = editor.CaretIndex;
            var bracket = snapshot.Brackets.ContainsKey(caret) ? caret : snapshot.Brackets.ContainsKey(caret - 1) ? caret - 1 : -1;
            var partner = bracket < 0 ? -1 : snapshot.Brackets[bracket];
            for (var i = low; i < snapshot.Tokens.Count; i++)
            {
                var token = snapshot.Tokens[i];
                if (token.Start >= visible.End) break;
                var start = Math.Max(token.Start, visible.Start); var end = Math.Min(token.Start + token.Length, visible.End);
                if (start >= end) continue;
                var type = token.Start == bracket || token.Start == partner ? partner < 0 ? SyntaxTokenType.UnmatchedBracket : SyntaxTokenType.MatchingBracket : token.Type;
                ChangeLinePart(start, end, element =>
                {
                    element.TextRunProperties.SetForegroundBrush(SyntaxStyles.Brush(editor, type));
                    if (type is SyntaxTokenType.MatchingBracket or SyntaxTokenType.UnmatchedBracket)
                        element.TextRunProperties.SetTextDecorations(new TextDecorationCollection { new TextDecoration { Location = TextDecorationLocation.Underline } });
                });
            }
        }
    }
}
