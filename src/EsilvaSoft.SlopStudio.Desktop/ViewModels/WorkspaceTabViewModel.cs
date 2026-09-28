using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Desktop.ViewModels;

public sealed partial class WorkspaceTabViewModel : ObservableObject, IDisposable
{
    private readonly WorkspaceService _workspace;
    public IApplicationOperationService Operations => _workspace.Operations;
    public Task ExportResultPageAsync(string path, IReadOnlyList<string> documents, bool csv, Action<int, int> progress, CancellationToken cancellationToken) =>
        _workspace.ExportResultPageAsync(path, documents, csv, progress, cancellationToken);
    public Task<string> FormatCodeAsync(string text, CancellationToken cancellationToken) => _workspace.FormatCodeAsync(text, cancellationToken);
    public Task<CodeValidationResult> ValidateCodeAsync(string text, bool aggregation, CancellationToken token) => _workspace.ValidateCodeAsync(text, aggregation, token);
    private CancellationTokenSource? _cancellation;
    private bool _restoring;
    private CancellationTokenSource? _presentationCancellation;
    private string? _savedText = "";
    private readonly SemaphoreSlim _fileOperationGate = new(1, 1);
    public TextFileEncoding FileEncoding { get; private set; } = TextFileEncoding.Utf8;
    public bool FileHasBom { get; private set; }
    public TextFileRevision? FileRevision { get; private set; }
    private int _historyGeneration;
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid? MissingProfileId { get; private set; }
    public string? MissingTargetHost { get; private set; }
    public bool ContainsResultData { get; set; }
    public event EventHandler? DraftChanged;
    // Modos que o editor entende. Script e Agregação continuam aqui para que rascunhos e histórico
    // salvos nesses modos reabram como foram gravados, sem conversão silenciosa.
    public IReadOnlyList<string> Modes { get; } = ["Console", "Script", "Agregação", "Texto"];
    // Modos oferecidos pela interface na fase atual. Script e Agregação estão no backlog
    // (docs/backlog/bkl-03-script-engine-entre-conexoes.md e bkl-04-modo-aggregation.md).
    public IReadOnlyList<string> SelectableModes { get; } = ["Console", "Texto"];

    [ObservableProperty] private ConnectionProfile? _profile;
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private string _database = "";
    [ObservableProperty] private string _collection = "";
    [ObservableProperty] private string _mode = "Console";
    [ObservableProperty] private string _text = "";
    [ObservableProperty] private string _inputJson = "{}";
    [ObservableProperty] private bool _persistInput;
    [ObservableProperty] private bool _historyEnabled = true;
    [ObservableProperty] private bool _scriptHistoryEnabled = true;
    [ObservableProperty] private string _filePath = "";
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _results = "";
    [ObservableProperty] private string _messages = "";
    [ObservableProperty] private string _errors = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _metrics = "";
    [ObservableProperty] private int _resultTabIndex;
    [ObservableProperty] private double _codeFontSize = 14;
    [ObservableProperty] private string _projection = "";
    [ObservableProperty] private string _sort = "";
    [ObservableProperty] private int _limit = 100;
    [ObservableProperty] private int _skip;
    [ObservableProperty] private string _hint = "";
    [ObservableProperty] private string _comment = "";
    [ObservableProperty] private string _collation = "";
    [ObservableProperty] private int _batchSize = 100;
    [ObservableProperty] private int _maxTimeMs = 30000;

    private static string T(string key) => LocalizationViewModel.Current.Resolve(key);
    private static string F(string key, params object?[] arguments) => LocalizationViewModel.Current.Format(key, arguments);
    private static string NotExecutedText => T("notExecuted");

    public string Title => (string.IsNullOrEmpty(FilePath) ? Mode == "Script" ? T("untitledScript") : string.IsNullOrEmpty(Collection) ? Mode : Collection : Path.GetFileName(FilePath)) + (IsDirty ? " •" : "");
    public string Context => $"{Profile?.Name ?? T("noConnection")} › {(string.IsNullOrWhiteSpace(Database) ? T("chooseDatabase") : Database)}{(IsConsole || string.IsNullOrWhiteSpace(Collection) ? "" : " › " + Collection)}";
    public string AccessHint => Profile is null ? T("chooseConnectionForTab") : !IsConnected ? T("disconnectedOpenConnection") : Profile.IsReadOnly ? T("readOnlyPrefix") + Profile.RoutingLabel : T("fixedTargetPrefix") + Profile.RoutingLabel;
    public bool IsScript => Mode == "Script";
    public bool IsAggregation => Mode == "Agregação";
    public bool IsText => Mode == "Texto";
    public bool CanExplainAggregation => IsAggregation && CanExecute;
    public bool IsQuery => Mode == "Consulta JSON";
    public double CodeLineHeight => CodeFontSize * 1.5;
    partial void OnCodeFontSizeChanged(double value) => OnPropertyChanged(nameof(CodeLineHeight));
    public bool CanEditContext => !IsRunning;
    public bool CanExecute => !IsText && !IsRunning && IsConnected && Profile is not null && !string.IsNullOrWhiteSpace(Database) && !string.IsNullOrWhiteSpace(Text)
        && (IsConsole || (IsScript ? !Profile.IsReadOnly : !string.IsNullOrWhiteSpace(Collection)));
    partial void OnProfileChanged(ConnectionProfile? value)
    {
        if (value is not null) { MissingProfileId = value.Id; MissingTargetHost = value.TargetHost; }
        _historyGeneration++;
        History.Clear(); SavedQueries.Clear(); SelectedHistory = null; SelectedSavedQuery = null;
        InvalidateDestinationResults();
    }
    partial void OnDatabaseChanged(string value) => InvalidateDestinationResults();
    partial void OnCollectionChanged(string value) => InvalidateDestinationResults();
    private void InvalidateDestinationResults()
    {
        if (IsRunning) return;
        _resultRenderable = false;
        ClearResults(NotExecutedText);
        Metrics = ""; Messages = ""; Errors = "";
        OnPropertyChanged(nameof(CanExport));
    }

    public WorkspaceTabViewModel(WorkspaceService workspace)
    {
        _workspace = workspace;
        Results = NotExecutedText;
        Status = T("statusReady");
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(Text) or nameof(InputJson) or nameof(PersistInput) or nameof(Database) or nameof(Collection) or nameof(Mode) or nameof(Profile) or nameof(FilePath) or nameof(Projection) or nameof(Sort) or nameof(Limit) or nameof(Skip) or nameof(Hint) or nameof(Comment) or nameof(Collation) or nameof(BatchSize) or nameof(MaxTimeMs) or nameof(HistoryEnabled) or nameof(ScriptHistoryEnabled))
            {
                if (!_restoring)
                {
                    IsDirty = e.PropertyName == nameof(Text) ? _savedText is null || Text != _savedText : IsDirty;
                    DraftChanged?.Invoke(this, EventArgs.Empty);
                }
                OnPropertyChanged(nameof(Context));
                OnPropertyChanged(nameof(IsScript));
                OnPropertyChanged(nameof(IsQuery));
                OnPropertyChanged(nameof(IsConsole));
                OnPropertyChanged(nameof(IsAggregation));
                OnPropertyChanged(nameof(IsText));
            }
            if (e.PropertyName is nameof(Profile) or nameof(IsConnected)) OnPropertyChanged(nameof(AccessHint));
            if (e.PropertyName is nameof(IsDirty) or nameof(FilePath) or nameof(Collection) or nameof(Mode)) OnPropertyChanged(nameof(Title));
            if (e.PropertyName is nameof(IsRunning) or nameof(Text) or nameof(Profile) or nameof(IsConnected) or nameof(Database) or nameof(Collection) or nameof(Mode))
            {
                OnPropertyChanged(nameof(CanExecute));
                OnPropertyChanged(nameof(CanEditContext));
                OnPropertyChanged(nameof(CanExport));
                ExecuteCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(CanExplainAggregation));
                ExplainAggregationCommand.NotifyCanExecuteChanged();
            }
        };
    }

    public void RefreshLanguage()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Context));
        OnPropertyChanged(nameof(AccessHint));
        OnPropertyChanged(nameof(CopyJsonHint));
        OnPropertyChanged(nameof(IsJsonResultView));
        OnPropertyChanged(nameof(IsTreeResultView));
        OnPropertyChanged(nameof(HasSelectedDocument));
        OnPropertyChanged(nameof(HasResultTree));
        if (_resultRenderable) RenderResults();
    }

    [RelayCommand(CanExecute = nameof(CanExecute))]
    private async Task ExecuteAsync(string? selection)
    {
        if (!CanExecute || IsText) return;
        var profile = Profile!;
        var database = Database;
        var collection = Collection;
        var mode = Mode;
        // Snapshot every input before the first await. Selection is never replaced after an error.
        var text = string.IsNullOrEmpty(selection) ? Text : selection;
        var input = InputJson;
        var query = BuildQuery(text);
        var limit = Limit;
        var maxTimeMs = MaxTimeMs;
        var historyEnabled = HistoryEnabled && !ContainsResultData;
        var uuidPolicy = UuidPolicy;
        var executedAt = DateTimeOffset.UtcNow;
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        using var operation = Operations.Begin(F("executingQuery", profile.Name, database), ApplicationOperationPriority.High);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(operation.Token);
        _cancellation = cancellation;
        IsRunning = true;
        Errors = "";
        Messages = "";
        Metrics = "";
        _resultRenderable = false; _resultPolicy = uuidPolicy; _resultProfileId = profile.Id; _resultSourceGenerationId = profile.SourceGenerationId; _resultIsConsole = mode == "Console"; _resultEmptyText = null;
        ClearResults(T("executing"));
        Status = T("executing");
        try
        {
            if (mode == "Console")
            {
                var result = await _workspace.ExecuteConsoleAsync(new(profile, database, text, Math.Clamp(limit, 1, 1000), Math.Clamp(maxTimeMs, 1, 300000), historyEnabled), ConfirmConsoleWrite, cancellation.Token);
                _resultEmptyText = result.Results.Count == 0 ? T("consoleNoResult") : null;
                _resultMetrics = F("consoleMetrics", result.Results.Count, result.Duration.TotalMilliseconds.ToString("F0", System.Globalization.CultureInfo.InvariantCulture), result.Environment, Math.Clamp(limit, 1, 1000));
                await SetResultsAsync(() => result.Results.Select(item => StructuredResultSet.FromConsoleLocalized(item, LocalizationViewModel.Current.Resolve)).ToArray(), result.Results, cancellation.Token);
                Messages = result.Messages; Errors = result.Error ?? "";
                Status = result.IsCanceled ? T("statusCancelled") : result.IsTimedOut ? T("timedOut") : result.Error is null ? T("statusSuccess") : T("statusError");
                ResultTabIndex = result.Error is null ? 0 : 2;
            }
            else if (mode == "Script")
            {
                var result = await _workspace.ExecuteScriptAsync(profile, text, input, database, cancellation.Token);
                _resultEmptyText = result.Results.Count == 0 ? T("scriptNoDocuments") : null;
                _resultMetrics = F("scriptMetrics", result.Results.Count, result.Duration.TotalMilliseconds.ToString("F0", System.Globalization.CultureInfo.InvariantCulture));
                await SetResultsAsync(() => [StructuredResultSet.FromDocuments(1, new ResultOrigin(T("scriptMongosh"), profile.Id, profile, database, null), result.Results, false, ResultCompleteness.Unknown)], null, cancellation.Token);
                Messages = result.StandardOutput;
                Errors = result.StandardError;
                Status = result.ExitCode == 0 ? T("statusSuccess") : F("scriptFailed", result.ExitCode);
                ResultTabIndex = result.ExitCode != 0 || Errors.Length > 0 ? 2 : Documents.Count == 0 ? 1 : 0;
            }
            else
            {
                var aggregation = mode == "Agregação";
                var result = aggregation
                    ? await _workspace.AggregateAsync(profile, new AggregationQuery(database, collection, text, limit), cancellation.Token)
                    : await _workspace.QueryAsync(profile, query, cancellation.Token);
                var completeness = aggregation ? ResultCompleteness.Derived
                    : query.ProjectionJson is null || ExtendedJsonComparer.AreEquivalent(query.ProjectionJson, "{}") ? ResultCompleteness.Complete : ResultCompleteness.PartialProjection;
                _resultEmptyText = result.Documents.Count == 0 ? T("noDocumentsFound") : null;
                _resultMetrics = F("documentsMetrics", result.Documents.Count, query.Limit, result.Duration.TotalMilliseconds.ToString("F0", System.Globalization.CultureInfo.InvariantCulture)) + (result.IsTruncated ? T("resultLimited") : "");
                await SetResultsAsync(() => [StructuredResultSet.FromDocuments(1, new ResultOrigin(aggregation ? T("resultAggregation") : T("resultQuery"), profile.Id, profile, database, collection),
                    result.Documents, result.IsTruncated, completeness, aggregation ? "aggregate" : "find")], null, cancellation.Token);
                Status = T("statusSuccess");
                ResultTabIndex = 0;
                if (historyEnabled && mode == "Consulta JSON")
                {
                    try
                    {
                        var entry = QueryHistoryEntry.Create(profile.Id, query);
                        await _workspace.SaveQueryHistoryAsync(entry, cancellation.Token);
                        History.Insert(0, entry);
                        while (History.Count > 50) History.RemoveAt(History.Count - 1);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException) { Messages = F("queryCompletedHistoryNotSaved", DesktopOperationErrorMessages.Describe(ex)); }
                }
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Status = T("statusCancelled");
            if (!_resultRenderable) SetResultState(T("queryInterrupted"));
            Messages = T("writeEffectsNotReverted");
            ResultTabIndex = 1;
        }
        catch (Exception ex)
        {
            Status = T("executionFailed");
            Errors = DesktopOperationErrorMessages.Describe(ex);
            if (!_resultRenderable) SetResultState(T("executionIncomplete"));
            ResultTabIndex = 2;
        }
        finally
        {
            if (mode == "Agregação" && historyEnabled)
            {
                try
                {
                    var entry = new ConsoleHistoryEntry(1, Guid.NewGuid(), executedAt, profile.Id, profile.Name, database,
                        "Ambiente não registrado", text, System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds, Status, [profile.Id])
                    { Mode = mode, Collection = collection, DocumentLimit = limit, TargetHost = profile.TargetHost };
                    await _workspace.SaveExecutionHistoryAsync(entry, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    Messages += "\n" + F("historyNotSaved", DesktopOperationErrorMessages.Describe(ex));
                    if (Errors.Length == 0) ResultTabIndex = 1;
                }
            }
            operation.Complete(Status == T("statusCancelled") ? ApplicationOperationStatus.Cancelled : Errors.Length > 0 || Status == T("executionFailed") ? ApplicationOperationStatus.Error : ApplicationOperationStatus.Success,
                $"{Status} — {profile.Name} › {database}");
            _cancellation = null; IsRunning = false; ApplyPendingUuidPolicy();
        }
    }

    [RelayCommand] private void Cancel() { _cancellation?.Cancel(); _presentationCancellation?.Cancel(); }

    public async Task SaveAsync(string path, bool overwriteExternalChanges = false)
    {
        path = Path.GetFullPath(path);
        var text = Text;
        var encoding = FileEncoding;
        var hasBom = FileHasBom;
        var samePath = !string.IsNullOrEmpty(FilePath) && string.Equals(path, Path.GetFullPath(FilePath), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        // A restored legacy draft has no revision: ask before replacing its file.
        var expected = overwriteExternalChanges ? null : samePath ? FileRevision ?? TextFileRevision.Missing : TextFileRevision.Missing;
        var persistInput = PersistInput; var input = InputJson; var historyEnabled = ScriptHistoryEnabled;
        await _fileOperationGate.WaitAsync();
        try
        {
            var revision = await _workspace.SaveTextDocumentAsync(path, text, encoding, hasBom, expected);
            FilePath = path;
            FileRevision = revision;
            _savedText = text;
            IsDirty = Text != _savedText;
            Status = T("savedFile");
            DraftChanged?.Invoke(this, EventArgs.Empty);
        }
        finally { _fileOperationGate.Release(); }
        if (historyEnabled)
        {
            try { await _workspace.SaveScriptHistoryAsync(ScriptHistoryEntry.Create(path, inputJson: persistInput ? input : null)); }
            catch (Exception ex) { Messages = F("savedFileHistoryNotSaved", DesktopOperationErrorMessages.Describe(ex)); }
        }
    }

    public async Task OpenAsync(string path)
    {
        await _fileOperationGate.WaitAsync();
        try
        {
            var document = await _workspace.LoadTextDocumentAsync(path);
            _restoring = true;
            Text = document.Content;
            FilePath = document.Path;
            FileEncoding = document.Encoding;
            FileHasBom = document.HasBom;
            FileRevision = document.Revision;
            _savedText = document.Content;
            IsDirty = false;
        }
        finally { _restoring = false; _fileOperationGate.Release(); }
        DraftChanged?.Invoke(this, EventArgs.Empty);
    }

    public WorkspaceDraft Snapshot() => new()
    {
        Id = Id, ContainsResultData = ContainsResultData, ProfileId = Profile?.Id ?? MissingProfileId, TargetHost = Profile?.TargetHost ?? MissingTargetHost, Database = Database, Collection = Collection,
        Mode = Mode, Text = Text, InputJson = PersistInput ? InputJson : null, FilePath = FilePath,
        FileEncoding = FileEncoding.ToString(), FileHasBom = FileHasBom,
        FileRevisionLength = FileRevision?.Length, FileRevisionLastWriteTimeUtc = FileRevision?.LastWriteTimeUtc,
        FileRevisionSha256 = FileRevision?.Sha256, SavedText = _savedText,
        IsDirty = IsDirty, Projection = Projection, Sort = Sort, Limit = Limit, Skip = Skip,
        Hint = Hint, Comment = Comment, Collation = Collation, BatchSize = BatchSize, MaxTimeMs = MaxTimeMs,
        HistoryEnabled = HistoryEnabled, ScriptHistoryEnabled = ScriptHistoryEnabled
    };

    public void Restore(WorkspaceDraft draft, ConnectionProfile? profile)
    {
        _restoring = true;
        Id = draft.Id;
        MissingProfileId = draft.ProfileId;
        MissingTargetHost = draft.TargetHost;
        Profile = profile is null ? null : profile with { TargetHost = draft.TargetHost ?? profile.TargetHost };
        Database = draft.Database;
        Collection = draft.Collection;
        Mode = draft.Mode == "Consulta JSON" ? "Console" : Modes.Contains(draft.Mode) ? draft.Mode : "Console";
        Text = draft.Mode == "Consulta JSON" ? ConsoleScripts.FromQuery(new MongoQuery(draft.Database, draft.Collection, draft.Text,
            ProjectionJson: EmptyToNull(draft.Projection), SortJson: EmptyToNull(draft.Sort), Limit: draft.Limit, Skip: draft.Skip,
            HintJson: EmptyToNull(draft.Hint), MaxTimeMs: draft.MaxTimeMs, Comment: EmptyToNull(draft.Comment), BatchSize: draft.BatchSize, CollationJson: EmptyToNull(draft.Collation))) : draft.Text;
        FilePath = draft.FilePath;
        FileEncoding = Enum.TryParse<TextFileEncoding>(draft.FileEncoding, out var encoding) && Enum.IsDefined(encoding) ? encoding : TextFileEncoding.Utf8;
        FileHasBom = draft.FileHasBom;
        FileRevision = draft.FileRevisionLength is { } length && draft.FileRevisionLastWriteTimeUtc is { } modified && draft.FileRevisionSha256 is { } hash
            ? new TextFileRevision(length, modified, hash) : null;
        PersistInput = draft.InputJson is not null;
        InputJson = draft.InputJson ?? "{}";
        Projection = draft.Projection;
        Sort = draft.Sort;
        Limit = Math.Clamp(draft.Limit, 1, 1000);
        Skip = Math.Max(0, draft.Skip);
        Hint = draft.Hint; Comment = draft.Comment; Collation = draft.Collation;
        BatchSize = Math.Clamp(draft.BatchSize, 1, 10000); MaxTimeMs = Math.Clamp(draft.MaxTimeMs, 1, 600000);
        HistoryEnabled = draft.HistoryEnabled; ScriptHistoryEnabled = draft.ScriptHistoryEnabled;
        IsDirty = draft.IsDirty || draft.Mode == "Consulta JSON";
        IsConnected = false;
        _savedText = draft.SavedText ?? (IsDirty ? null : Text);
        _restoring = false;
    }

    private static string? EmptyToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private MongoQuery BuildQuery(string text) => new(Database, Collection, text, ProjectionJson: EmptyToNull(Projection), SortJson: EmptyToNull(Sort),
        Limit: Limit, Skip: Skip, HintJson: EmptyToNull(Hint), MaxTimeMs: MaxTimeMs, Comment: EmptyToNull(Comment), BatchSize: BatchSize, CollationJson: EmptyToNull(Collation));

    [RelayCommand] private async Task NextPageAsync() { if (!CanExecute || !IsQuery) return; Skip = Math.Min(1000000, Skip + Limit); await ExecuteCommand.ExecuteAsync(null); }
    [RelayCommand] private async Task PreviousPageAsync() { if (!CanExecute || !IsQuery) return; Skip = Math.Max(0, Skip - Limit); await ExecuteCommand.ExecuteAsync(null); }
    [RelayCommand] private async Task FirstDocumentsAsync() { if (IsRunning || !IsQuery) return; Text = "{}"; Skip = 0; if (CanExecute) await ExecuteCommand.ExecuteAsync(null); }
}
