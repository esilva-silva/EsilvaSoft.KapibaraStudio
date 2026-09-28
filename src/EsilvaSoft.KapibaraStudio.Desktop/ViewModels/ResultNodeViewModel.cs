using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

/// <summary>Row of the results tree. Children are created on first expansion; before that only a placeholder exists.</summary>
public sealed partial class ResultNodeViewModel : ObservableObject
{
    private static string T(string key) => LocalizationViewModel.Current.Resolve(key);
    private static string F(string key, params object?[] args) => LocalizationViewModel.Current.Format(key, args);
    private const int MaximumValueLength = 240;
    private const int MaximumToolTipLength = 2000;
    private static readonly JsonDocumentOptions Options = new() { MaxDepth = ExtendedJsonFormatter.MaxDepth };
    private readonly ResultTreeState? _state;
    private Func<IReadOnlyList<ResultNodeViewModel>>? _load;

    private ResultNodeViewModel(ResultNodeKind kind, string key, string name, string typeName, string value, ResultDocumentViewModel? document, ResultTreeState? state, string? toolTip = null)
    {
        Kind = kind; Key = key; Name = name; TypeName = typeName; Document = document; _state = state;
        Value = value.Length > MaximumValueLength ? value[..MaximumValueLength] + F("truncatedCharacters", value.Length) : value;
        var tip = toolTip ?? name + (typeName.Length == 0 ? "" : " · " + typeName) + (value.Length == 0 ? "" : "\n" + value);
        ToolTip = tip.Length > MaximumToolTipLength ? tip[..MaximumToolTipLength] + "…" : tip;
    }

    public ResultNodeKind Kind { get; }
    /// <summary>Stable position path, e.g. <c>s2/d0/3/1</c>; field names are not used, so duplicate and dotted names stay distinct.</summary>
    public string Key { get; }
    /// <summary>Field name as stored (dots are not paths), <c>[i]</c> for array items, or the set/document label.</summary>
    public string Name { get; }
    public string TypeName { get; }
    public string Value { get; }
    public string ToolTip { get; }
    /// <summary>Document that owns the row; the context menu always acts on this document.</summary>
    public ResultDocumentViewModel? Document { get; }
    public int DocumentCount { get; private init; }
    public ObservableCollection<ResultNodeViewModel> Children { get; } = [];
    public bool IsDocument => Kind == ResultNodeKind.Document;
    public bool IsHeading => Kind is ResultNodeKind.ResultSet or ResultNodeKind.Document;
    public bool IsNotice => Kind == ResultNodeKind.Notice;
    public bool HasType => TypeName.Length > 0;
    public bool IsChildrenLoaded => _load is null;
    public string AutomationName => Name + (TypeName.Length == 0 ? "" : ", " + TypeName) + (Value.Length == 0 ? "" : ", " + Value);

    [ObservableProperty] private bool _isExpanded;

    partial void OnIsExpandedChanged(bool value)
    {
        if (value) EnsureChildren();
        if (_state is null) return;
        if (value) _state.Expanded.Add(Key); else _state.Expanded.Remove(Key);
    }

    public void EnsureChildren()
    {
        if (_load is not { } load) return;
        _load = null;
        var children = load();
        Children.Clear();
        foreach (var child in children) Children.Add(child);
    }

    internal static ResultNodeViewModel ForSet(StructuredResultSet set, IReadOnlyList<ResultDocumentViewModel>? documents, IdentifierDisplayOptions representation, ResultTreeState state)
    {
        var key = SetKey(set);
        if (documents is not null)
        {
            var node = new ResultNodeViewModel(ResultNodeKind.ResultSet, key, set.Label, (set.Method ?? T("resultDefault")) + " · " + F("documentCount", documents.Count),
                Flags(set), null, state, set.Origin.Source + "\n" + set.Origin.Destination) { DocumentCount = documents.Count };
            node.Defer(() => SetContent(set, documents, representation, state));
            return node;
        }
        // Values that are not document lists (counts, write acknowledgements) are inspected like fields.
        try
        {
            using var parsed = JsonDocument.Parse(set.Json, Options);
            var root = parsed.RootElement.Clone();
            var description = ExtendedJsonValue.Describe(root, representation);
            var node = new ResultNodeViewModel(ResultNodeKind.ResultSet, key, set.Label, description.TypeName, description.Display, null, state, set.Origin.Source);
            if (description.Shape != ExtendedJsonShape.Scalar && description.ChildCount > 0) node.Defer(() => FieldChildren(root, key, null, representation, state));
            return node;
        }
        catch (JsonException exception)
        {
            var node = new ResultNodeViewModel(ResultNodeKind.ResultSet, key, set.Label, T("invalidJson"), exception.Message, null, state);
            node.Defer(() => [Notice(key + "/raw", LocalizationViewModel.Current.Resolve("originalContent"), set.Json, null)]);
            return node;
        }
    }

    internal static IReadOnlyList<ResultNodeViewModel> SetContent(StructuredResultSet set, IReadOnlyList<ResultDocumentViewModel> documents, IdentifierDisplayOptions representation, ResultTreeState state)
    {
        var key = SetKey(set);
        var nodes = new List<ResultNodeViewModel>();
        if (set.Completeness == ResultCompleteness.PartialProjection)
            nodes.Add(Notice(key + "/projection", LocalizationViewModel.Current.Resolve("partialProjectionNotice"), LocalizationViewModel.Current.Resolve("partialProjectionExplain"), null));
        if (set.Completeness == ResultCompleteness.Derived)
            nodes.Add(Notice(key + "/derived", LocalizationViewModel.Current.Resolve("derivedNotice"), LocalizationViewModel.Current.Resolve("derivedExplain"), null));
        if (set.IsTruncated)
            nodes.Add(Notice(key + "/truncated", LocalizationViewModel.Current.Resolve("limitedNotice"), LocalizationViewModel.Current.Resolve("limitedExplain"), null));
        if (documents.Count == 0)
            nodes.Add(Notice(key + "/empty", LocalizationViewModel.Current.Resolve("noDocumentsNotice"), LocalizationViewModel.Current.Resolve("noDocumentInResultNotice"), null));
        foreach (var document in documents)
            nodes.Add(ForDocument(document, key + "/d" + document.Document.Position.ToString(CultureInfo.InvariantCulture), representation, state));
        return nodes;
    }

    private static ResultNodeViewModel ForDocument(ResultDocumentViewModel document, string key, IdentifierDisplayOptions representation, ResultTreeState state)
    {
        ResultNodeViewModel node;
        var toolTip = document.Summary + "\n" + document.IdentityText + (document.FlagsText.Length == 0 ? "" : "\n" + document.FlagsText);
        if (document.Document.Root is not { } root)
        {
            node = new(ResultNodeKind.Document, key, document.Label, T("invalidJson"), document.Document.InvalidJsonMessage ?? "", document, state, toolTip);
            node.Defer(() => [Notice(key + "/raw", LocalizationViewModel.Current.Resolve("originalContent"), document.Json, document)]);
        }
        else
        {
            var description = ExtendedJsonValue.Describe(root, representation);
            node = root.ValueKind == JsonValueKind.Object
                ? new(ResultNodeKind.Document, key, document.Label, T("documentTypeLabel"), document.IdentityText + " · " + description.Display, document, state, toolTip)
                : new(ResultNodeKind.Document, key, document.Label, description.TypeName, description.Display, document, state, toolTip);
            if (description.Shape != ExtendedJsonShape.Scalar && description.ChildCount > 0) node.Defer(() => FieldChildren(root, key, document, representation, state));
        }
        state.DocumentNodes[document] = node;
        return node;
    }

    private static List<ResultNodeViewModel> FieldChildren(JsonElement element, string key, ResultDocumentViewModel? document, IdentifierDisplayOptions representation, ResultTreeState state, int offset = 0)
    {
        const int pageSize = 256;
        var children = new List<ResultNodeViewModel>();
        var index = offset;
        var hasMore = false;
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject().Skip(offset))
            {
                if (children.Count == pageSize) { hasMore = true; break; }
                children.Add(ForValue(property.Name.Length == 0 ? "\"\"" : property.Name, property.Value, key + "/" + (index++).ToString(CultureInfo.InvariantCulture), document, representation, state));
            }
        }
        else
        {
            foreach (var item in element.EnumerateArray().Skip(offset))
            {
                if (children.Count == pageSize) { hasMore = true; break; }
                var position = (index++).ToString(CultureInfo.InvariantCulture);
                children.Add(ForValue("[" + position + "]", item, key + "/" + position, document, representation, state));
            }
        }
        if (hasMore)
        {
            var next = new ResultNodeViewModel(ResultNodeKind.Field, key + "/more" + index.ToString(CultureInfo.InvariantCulture), LocalizationViewModel.Current.Resolve("nextFields"), "", LocalizationViewModel.Current.Resolve("expandMoreFields"), document, state);
            next.Defer(() => FieldChildren(element, key, document, representation, state, index));
            children.Add(next);
        }
        return children;
    }

    private static ResultNodeViewModel ForValue(string name, JsonElement value, string key, ResultDocumentViewModel? document, IdentifierDisplayOptions representation, ResultTreeState state)
    {
        var description = ExtendedJsonValue.Describe(value, representation);
        var node = new ResultNodeViewModel(ResultNodeKind.Field, key, name, description.TypeName, description.Display, document, state);
        if (description.Shape != ExtendedJsonShape.Scalar && description.ChildCount > 0) node.Defer(() => FieldChildren(value, key, document, representation, state));
        return node;
    }

    private void Defer(Func<IReadOnlyList<ResultNodeViewModel>> load)
    {
        _load = load;
        Children.Add(Notice(Key + "/loading", LocalizationViewModel.Current.Resolve("loadingEllipsis"), "", Document));
        if (_state?.Expanded.Contains(Key) == true) IsExpanded = true;
    }

    private static ResultNodeViewModel Notice(string key, string name, string value, ResultDocumentViewModel? document) =>
        new(ResultNodeKind.Notice, key, name, "", value, document, null);

    private static string SetKey(StructuredResultSet set) => "s" + set.Number.ToString(CultureInfo.InvariantCulture);

    private static string Flags(StructuredResultSet set) => string.Join(" · ", new[]
    {
        set.IsTruncated ? T("limitedFlag") : null,
        set.Completeness switch { ResultCompleteness.PartialProjection => T("partialProjectionFlag"), ResultCompleteness.Derived => T("derivedFlag"), _ => null }
    }.OfType<string>());
}
