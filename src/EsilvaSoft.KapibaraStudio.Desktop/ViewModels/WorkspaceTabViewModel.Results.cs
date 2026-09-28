using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

public sealed partial class WorkspaceTabViewModel
{
    // Results live only in memory for this tab: Snapshot() never includes them; a new execution or destination change discards them.
    public ObservableCollection<ResultDocumentViewModel> ResultDocuments { get; } = [];
    [ObservableProperty] private ResultDocumentViewModel? _selectedDocument;
    public ObservableCollection<ResultNodeViewModel> ResultTree { get; } = [];
    public IReadOnlyList<ResultTextSegment> ResultSegments { get; private set; } = [];
    [ObservableProperty] private ResultViewMode _resultView = ResultViewMode.Json;
    [ObservableProperty] private ResultNodeViewModel? _selectedResultNode;
    [ObservableProperty] private string _resultTreeStatus = NotExecutedText;
    private StructuredResultSet[] _resultSets = [];
    private readonly Dictionary<StructuredResultSet, IReadOnlyList<ResultDocumentViewModel>> _documentViews = [];
    private readonly Dictionary<ConsoleResultSet, StructuredResultSet> _consoleSets = new(ReferenceEqualityComparer.Instance);
    private ResultTreeState _treeState = new();
    private ResultDocumentViewModel? _pendingSelection;
    private bool _syncingSelection;
    private IReadOnlyList<string> _documents = [];
    // Canonical Extended JSON remains the source for identity, editing and export; ObjectId/UUID constructors are presentation only.
    [ObservableProperty] private UuidDisplayPolicy _uuidPolicy = UuidDisplayPolicy.Default;
    private UuidDisplayPolicy _resultPolicy = UuidDisplayPolicy.Default;
    private Guid? _resultProfileId;
    private Guid? _resultSourceGenerationId;
    private bool _resultIsConsole;
    private bool _resultRenderable;
    private string? _resultEmptyText;
    private string _resultMetrics = "";

    public IReadOnlyList<StructuredResultSet> ResultSets => _resultSets;
    /// <summary>Extended JSON of the selected result set, used by export and field inference.</summary>
    public IReadOnlyList<string> Documents
    {
        get => _documents;
        private set
        {
            _documents = value;
            OnPropertyChanged(nameof(CanExport));
        }
    }
    public bool IsJsonResultView { get => ResultView == ResultViewMode.Json; set => ChooseResultView(value, ResultViewMode.Json); }
    public bool IsTreeResultView { get => ResultView == ResultViewMode.Tree; set => ChooseResultView(value, ResultViewMode.Tree); }
    public bool HasResultTree => ResultTree.Count > 0;
    public bool HasSelectedDocument => SelectedDocument is not null;
    public string CopyJsonHint => SelectedDocument is { } document
        ? LocalizationViewModel.Current.Format("copySelectedJsonHint", document.Summary)
        : LocalizationViewModel.Current.Resolve("selectDocumentToCopy");
    public bool CanExport => Documents.Count > 0 && !IsRunning;

    private void ChooseResultView(bool chosen, ResultViewMode mode)
    {
        if (!chosen) return;
        ResultView = mode;
        ResultTabIndex = 0;
    }

    partial void OnResultViewChanged(ResultViewMode value)
    {
        OnPropertyChanged(nameof(IsJsonResultView));
        OnPropertyChanged(nameof(IsTreeResultView));
        SyncTreeSelection();
    }

    partial void OnSelectedDocumentChanged(ResultDocumentViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelectedDocument));
        OnPropertyChanged(nameof(CopyJsonHint));
        if (!_syncingSelection) SyncTreeSelection();
    }

    partial void OnSelectedResultNodeChanged(ResultNodeViewModel? value)
    {
        if (_syncingSelection || value?.Document is not { } document || ReferenceEquals(document, SelectedDocument)) return;
        var previous = _syncingSelection;
        _syncingSelection = true;
        try { SelectResultDocumentCore(document); }
        finally { _syncingSelection = previous; }
    }

    /// <summary>Selects a document from any view; in the Console the owning result set becomes the selected set.</summary>
    public void SelectResultDocument(ResultDocumentViewModel document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!ReferenceEquals(document, SelectedDocument)) SelectResultDocumentCore(document);
    }

    private void SelectResultDocumentCore(ResultDocumentViewModel document)
    {
        if (_resultIsConsole && ConsoleFor(document.Document.Set) is { } console && !ReferenceEquals(console, SelectedConsoleResult))
        {
            _pendingSelection = document;
            try { SelectedConsoleResult = console; }
            finally { _pendingSelection = null; }
        }
        SelectedDocument = document;
    }

    /// <summary>Opening an editable copy reads nothing; availability depends only on the captured result.</summary>
    public static ResultEditAvailability GetEditAvailability(ResultDocumentViewModel document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var set = document.Document.Set;
        if (!document.Document.IsValid) return new(false, LocalizationViewModel.Current.Resolve("invalidJsonNoEdit"));
        if (!set.Origin.HasCollection) return new(false, LocalizationViewModel.Current.Resolve("resultNoCollection"));
        if (document.IdentityFilter is null) return new(false, LocalizationViewModel.Current.Resolve("resultNoIdentity"));
        return set.Completeness switch
        {
            ResultCompleteness.PartialProjection => new(false, LocalizationViewModel.Current.Resolve("partialProjectionEdit")),
            ResultCompleteness.Derived => new(false, LocalizationViewModel.Current.Resolve("derivedResultEdit")),
            ResultCompleteness.Unknown => new(false, LocalizationViewModel.Current.Resolve("unknownOriginEdit")),
            _ => new(true, "")
        };
    }

    /// <summary>
    /// Opens the copy held in memory without querying or writing. Saving is an explicit, confirmed action that reads the
    /// document again by <c>_id</c> and still uses the <c>$$ROOT</c> precondition.
    /// </summary>
    public DocumentMutationViewModel CreateResultDocumentEditor(ResultDocumentViewModel document)
    {
        var availability = GetEditAvailability(document);
        if (!availability.CanOpen) throw new InvalidOperationException(availability.Reason);
        var origin = document.Document.Set.Origin;
        return new(_workspace, origin.Profile!, origin.Database!, origin.Collection!, document, "Editar", rereadBeforeWrite: true,
            writeBlockReason: () => IsRunning ? LocalizationViewModel.Current.Resolve("waitExecutionBeforeSave") : !IsConnected ? LocalizationViewModel.Current.Resolve("connectionClosedBeforeSave") : null);
    }

    private void ShowSetDocuments(StructuredResultSet? set)
    {
        IReadOnlyList<ResultDocumentViewModel> views = set is not null && _documentViews.TryGetValue(set, out var list) ? list : [];
        Documents = set?.Documents?.Select(document => document.Json).ToArray() ?? [];
        var preferred = _pendingSelection is { } pending && views.Contains(pending) ? pending
            : SelectedDocument is { } current && views.Contains(current) ? current : views.Count > 0 ? views[0] : null;
        if (!ResultDocuments.SequenceEqual(views))
        {
            var previous = _syncingSelection;
            _syncingSelection = true;
            try
            {
                ResultDocuments.Clear();
                foreach (var view in views) ResultDocuments.Add(view);
            }
            finally { _syncingSelection = previous; }
        }
        SelectedDocument = preferred;
        if (!_syncingSelection) SyncTreeSelection();
    }

    private void SyncTreeSelection()
    {
        if (SelectedResultNode?.Document is { } current && ReferenceEquals(current, SelectedDocument)) return;
        var previous = _syncingSelection;
        _syncingSelection = true;
        try { SelectedResultNode = SelectedDocument is { } document ? FindDocumentNode(document) : null; }
        finally { _syncingSelection = previous; }
    }

    private ResultNodeViewModel? FindDocumentNode(ResultDocumentViewModel document)
    {
        if (_treeState.DocumentNodes.TryGetValue(document, out var node)) return node;
        var key = "s" + document.Document.Set.Number.ToString(CultureInfo.InvariantCulture);
        if (ResultTree.FirstOrDefault(n => n.Kind == ResultNodeKind.ResultSet && n.Key == key) is not { } set) return null;
        set.IsExpanded = true;
        return _treeState.DocumentNodes.GetValueOrDefault(document);
    }

    private ConsoleResultSet? ConsoleFor(StructuredResultSet set) => _consoleSets.FirstOrDefault(pair => ReferenceEquals(pair.Value, set)).Key;

    partial void OnUuidPolicyChanged(UuidDisplayPolicy value) => ApplyPendingUuidPolicy();
    private void ApplyPendingUuidPolicy()
    {
        // An execution renders with the policy captured at its start; a later preference is applied from canonical data once idle.
        if (IsRunning || !_resultRenderable || ReferenceEquals(_resultPolicy, UuidPolicy)) return;
        _resultPolicy = UuidPolicy;
        RenderResults();
    }

    private async Task SetResultsAsync(Func<StructuredResultSet[]> createSets, IReadOnlyList<ConsoleResultSet>? console, CancellationToken token)
    {
        var policy = _resultPolicy; var profileId = _resultProfileId; var isConsole = _resultIsConsole;
        var prepared = await Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            var sets = createSets();
            return (Sets: sets, Presentation: PrepareResults(sets, policy, profileId, isConsole, token));
        }, token);
        token.ThrowIfCancellationRequested();
        _resultSets = prepared.Sets;
        _consoleSets.Clear();
        if (console is not null)
            for (var i = 0; i < console.Count; i++) _consoleSets[console[i]] = _resultSets[i];
        ConsoleResults.Clear();
        LocalizedConsoleResults.Clear();
        if (console is not null)
            foreach (var set in console)
            {
                ConsoleResults.Add(set);
                LocalizedConsoleResults.Add(new(set));
            }
        _resultRenderable = true;
        ApplyPresentation(prepared.Presentation);
        NotifySchemaLearning(prepared.Sets);
    }

    /// <summary>
    /// L13 hook (schema-learning.md § Fluxo e isolamento): called only after the result is already assigned and
    /// rendered to this tab, never before. <see cref="EsilvaSoft.KapibaraStudio.Application.WorkspaceService.SchemaLearning"/>
    /// is null-safe by design — a missing service (feature off, DI absent in a test) makes this a no-op — and
    /// <see cref="EsilvaSoft.KapibaraStudio.Application.SchemaLearning.SchemaLearningService.NotifyResultDelivered"/>
    /// itself never throws, so this call can never fail the execution that already succeeded for the user.
    /// </summary>
    private void NotifySchemaLearning(StructuredResultSet[] sets)
    {
        if (_workspace.SchemaLearning is not { } schemaLearning || sets.Length == 0) return;
        var executionId = Guid.NewGuid();
        foreach (var set in sets) schemaLearning.NotifyResultDelivered(set, executionId, set.Number, 0, observedGenerationId: _resultSourceGenerationId);
    }

    private void ClearResults(string state)
    {
        _presentationCancellation?.Cancel();
        _resultSets = [];
        _consoleSets.Clear();
        _documentViews.Clear();
        _treeState = new();
        ConsoleResults.Clear();
        LocalizedConsoleResults.Clear();
        SelectedConsoleResult = null;
        SelectedLocalizedConsoleResult = null;
        var previous = _syncingSelection;
        _syncingSelection = true;
        try
        {
            SelectedResultNode = null;
            SelectedDocument = null;
            ResultTree.Clear();
            ResultDocuments.Clear();
        }
        finally { _syncingSelection = previous; }
        ResultSegments = [];
        Documents = [];
        SetResultState(state);
        OnPropertyChanged(nameof(HasResultTree));
    }

    private void SetResultState(string text)
    {
        Results = text;
        ResultTreeStatus = text;
    }

    private sealed record ResultPresentation(Dictionary<StructuredResultSet, IReadOnlyList<ResultDocumentViewModel>> Views,
        IReadOnlyList<ResultTextSegment> Segments, string Text, int Unknown, UuidRepresentation Primary);

    private static ResultPresentation PrepareResults(StructuredResultSet[] sets, UuidDisplayPolicy policy, Guid? profileId, bool isConsole, CancellationToken token)
    {
        var documentViews = new Dictionary<StructuredResultSet, IReadOnlyList<ResultDocumentViewModel>>();
        var primary = policy.Resolve(profileId);
        var unknown = 0;
        var text = new StringBuilder();
        var segments = new List<ResultTextSegment>();
        foreach (var set in sets)
        {
            var representation = policy.ResolveOptions(set.Origin.ProfileId ?? profileId);
            if (text.Length > 0) text.Append("\n\n");
            if (isConsole) text.Append("// ").Append(SetHeader(set, representation.Uuid == primary ? null : representation.Uuid)).Append('\n');
            if (set.Documents is { } documents)
            {
                var views = documents.Select(document => { token.ThrowIfCancellationRequested(); return new ResultDocumentViewModel(document, representation); }).ToArray();
                documentViews[set] = views;
                if (views.Length == 0 && isConsole) text.Append("// ").Append(LocalizationViewModel.Current.Resolve("noDocumentInResult"));
                for (var i = 0; i < views.Length; i++)
                {
                    var view = views[i];
                    if (i > 0) text.Append("\n\n");
                    if (!view.Document.IsValid) text.Append("// ").Append(view.Label).Append(": JSON inválido — ").Append(view.Document.InvalidJsonMessage).Append('\n');
                    unknown += view.UnknownLegacyUuidCount;
                    segments.Add(new(text.Length, view.FormattedJson.Length, view));
                    text.Append(view.FormattedJson);
                }
            }
            else
            {
                var display = IdentifierRepresentationService.FormatForDisplay(ExtendedJsonFormatter.TryFormat(set.Json, out var formatted, out _) ? formatted : set.Json, representation);
                unknown += display.UnknownLegacyCount;
                text.Append(display.Text);
            }
        }
        token.ThrowIfCancellationRequested();
        return new(documentViews, segments, text.ToString(), unknown, primary);
    }

    private void RenderResults()
    {
        _presentationCancellation?.Cancel();
        if (_resultSets.Sum(set => set.Documents?.Sum(document => (long)document.Json.Length) ?? set.Json.Length) <= 65536)
            ApplyPresentation(PrepareResults(_resultSets, _resultPolicy, _resultProfileId, _resultIsConsole, CancellationToken.None));
        else _ = RenderLargeResultsAsync();
    }

    private async Task RenderLargeResultsAsync()
    {
        var sets = _resultSets; var policy = _resultPolicy; var profileId = _resultProfileId; var isConsole = _resultIsConsole;
        using var operation = Operations.Begin(LocalizationViewModel.Current.Resolve("updatingBsonPresentation"), ApplicationOperationPriority.Normal);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(operation.Token);
        _presentationCancellation = cancellation;
        try
        {
            var presentation = await Task.Run(() => PrepareResults(sets, policy, profileId, isConsole, cancellation.Token), cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(sets, _resultSets) || !ReferenceEquals(policy, _resultPolicy)) return;
            ApplyPresentation(presentation);
            operation.Complete(ApplicationOperationStatus.Success, LocalizationViewModel.Current.Resolve("bsonPresentationUpdated"));
        }
        catch (OperationCanceledException) { operation.Complete(ApplicationOperationStatus.Cancelled, LocalizationViewModel.Current.Resolve("bsonPresentationCancelled")); }
        catch (Exception ex) { Errors = DesktopOperationErrorMessages.Describe(ex); operation.Complete(ApplicationOperationStatus.Error, LocalizationViewModel.Current.Resolve("bsonPresentationFailed")); }
        finally { if (ReferenceEquals(_presentationCancellation, cancellation)) _presentationCancellation = null; }
    }

    private void ApplyPresentation(ResultPresentation presentation)
    {
        var previous = SelectedDocument?.Document is { } selected ? (selected.Set.Number, selected.Position) : ((int Set, int Position)?)null;
        _documentViews.Clear();
        foreach (var pair in presentation.Views) _documentViews.Add(pair.Key, pair.Value);
        ResultSegments = presentation.Segments;
        Results = _resultEmptyText ?? presentation.Text;
        Metrics = _resultMetrics + " · IDs " + IdentifierRepresentationService.DisplayName(_resultPolicy.Mode) + " · UUID " + UuidCodec.DisplayName(presentation.Primary)
            + (presentation.Unknown == 0 ? "" : LocalizationViewModel.Current.Format("legacyUuidSuffix", presentation.Unknown));
        BuildResultTree();
        var target = previous is { } key ? _documentViews.FirstOrDefault(pair => pair.Key.Number == key.Set).Value?.ElementAtOrDefault(key.Position) : null;
        RestoreSelection(target);
    }

    private static string SetHeader(StructuredResultSet set, UuidRepresentation? distinctRepresentation) =>
        set.Label + (set.Method is null ? "" : " · " + set.Method)
        + (set.Documents is { } documents ? " · " + LocalizationViewModel.Current.Format("documentCountSuffix", documents.Count) : "")
        + (set.IsTruncated ? LocalizationViewModel.Current.Resolve("truncatedSuffix") : "")
        + (set.Completeness == ResultCompleteness.PartialProjection ? LocalizationViewModel.Current.Resolve("partialProjectionSuffix") : "")
        + (distinctRepresentation is { } representation ? " · UUID " + UuidCodec.DisplayName(representation) : "");

    private void BuildResultTree()
    {
        var previous = _syncingSelection;
        _syncingSelection = true;
        try
        {
            SelectedResultNode = null;
            ResultTree.Clear();
        }
        finally { _syncingSelection = previous; }
        _treeState.DocumentNodes.Clear();
        foreach (var set in _resultSets)
        {
            var representation = _resultPolicy.ResolveOptions(set.Origin.ProfileId ?? _resultProfileId);
            var views = _documentViews.GetValueOrDefault(set);
            if (_resultIsConsole) ResultTree.Add(ResultNodeViewModel.ForSet(set, views, representation, _treeState));
            else foreach (var node in ResultNodeViewModel.SetContent(set, views ?? [], representation, _treeState)) ResultTree.Add(node);
        }
        if (!_treeState.Rendered)
        {
            _treeState.Rendered = true;
            // A new result opens the first set with documents, and its document when it is the only one.
            var first = _resultIsConsole ? ResultTree.FirstOrDefault(node => node.DocumentCount > 0) : null;
            if (first is not null) first.IsExpanded = true;
            var documents = (_resultIsConsole ? first?.Children ?? [] : ResultTree).Where(node => node.IsDocument).ToArray();
            if (documents.Length == 1) documents[0].IsExpanded = true;
        }
        ResultTreeStatus = ResultTree.Count == 0 ? _resultEmptyText ?? LocalizationViewModel.Current.Resolve("noResults") : "";
        OnPropertyChanged(nameof(HasResultTree));
    }

    private void RestoreSelection(ResultDocumentViewModel? target)
    {
        _pendingSelection = target;
        try
        {
            if (_resultIsConsole)
            {
                var console = target is not null ? ConsoleFor(target.Document.Set)
                    : SelectedConsoleResult is { } current && ConsoleResults.Contains(current) ? current
                    : ConsoleResults.FirstOrDefault(set => set.Documents is { Count: > 0 });
                if (ReferenceEquals(console, SelectedConsoleResult)) ShowSetDocuments(console is null ? null : _consoleSets[console]);
                else SelectedConsoleResult = console;
            }
            else ShowSetDocuments(_resultSets.Length > 0 ? _resultSets[0] : null);
        }
        finally { _pendingSelection = null; }
        SyncTreeSelection();
    }

    public async Task<DocumentMutationViewModel> CreateDocumentMutationAsync(string operation)
    {
        if (Profile is null || !IsConnected || IsRunning) throw new InvalidOperationException(LocalizationViewModel.Current.Resolve("editNeedsConnectedCollection"));
        var profile = IsConsole ? SelectedConsoleResult?.SourceProfile : Profile;
        var database = IsConsole ? SelectedConsoleResult?.Database : Database;
        var collection = IsConsole ? SelectedConsoleResult?.Collection : Collection;
        if (profile is null || string.IsNullOrWhiteSpace(database) || string.IsNullOrWhiteSpace(collection)) throw new InvalidOperationException(LocalizationViewModel.Current.Resolve("editResultNeedsCollection"));
        var selected = SelectedDocument;
        var representation = UuidPolicy.ResolveOptions(profile.Id);
        profile.EnsureWriteAllowed();
        if (operation == "Inserir") return new(_workspace, profile, database, collection, null, operation);
        if (selected?.IdentityFilter is null) throw new InvalidOperationException(LocalizationViewModel.Current.Resolve("editDocumentNeedsId"));
        // Fetch the complete document explicitly before editing a potentially projected result.
        var page = await _workspace.QueryAsync(profile, new MongoQuery(database, collection, selected.IdentityFilter, Limit: 1));
        if (page.Documents.Count != 1) throw new InvalidOperationException(LocalizationViewModel.Current.Resolve("editDocumentUnavailable"));
        return new(_workspace, profile, database, collection, new ResultDocumentViewModel(page.Documents[0], 0, representation), operation);
    }
}
