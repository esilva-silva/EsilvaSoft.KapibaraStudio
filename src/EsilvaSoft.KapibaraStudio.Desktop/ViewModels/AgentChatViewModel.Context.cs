using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core.Agents;

namespace EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

public sealed partial class AgentChatViewModel
{
    private bool _activeFileRemoved;
    private bool _tabMetadataRemoved;

    /// <summary>Chips above the composer: exactly what the next message carries besides its text.</summary>
    public ObservableCollection<AgentContextChipViewModel> Chips { get; } = [];

    public bool HasChips => Chips.Count > 0;

    public string ContextSummary => Text.Format("agentContextSummary", Chips.Count);

    /// <summary>Chips are sent only to providers that honor them (<see cref="AgentProviderCapabilities.TurnPlan"/>).</summary>
    public bool AreChipsEnabled => IsFeatureAvailable && SupportsTurnPlan;

    /// <summary>Visible explanation when the provider receives only the message text.</summary>
    public bool ShowChipsDisabledNotice => IsFeatureAvailable && SelectedProvider is not null && !SupportsTurnPlan;

    /// <summary>"Arquivo externo…" is offered only when the permissions allow external attachments.</summary>
    public bool CanAttachExternal => AreChipsEnabled && CurrentPermissions is { IsWellFormed: true, DataSending.ExternalAttachments: true };

    /// <summary>Picker of workspace files (filtered by the exclusions of the permissions).</summary>
    public AgentWorkspaceFilePickerViewModel WorkspaceFilePicker { get; }

    /// <summary>
    /// Called by the host when the active tab or its own destination changed (never on explorer selection): automatic
    /// chips follow the tab that will be captured at send time.
    /// </summary>
    public void OnWorkspaceContextChanged()
    {
        if (!_disposed)
        {
            RefreshAutomaticChips();
        }
    }

    /// <summary>Rebuilds the automatic chips from the host snapshot and the permissions (manual chips are kept).</summary>
    private void RefreshAutomaticChips()
    {
        var permissions = CurrentPermissions is { IsWellFormed: true } valid ? valid : null;
        AgentWorkspaceContext context;
        try
        {
            context = _host.CaptureWorkspace();
        }
        catch (Exception)
        {
            context = new AgentWorkspaceContext(_services.Clock.GetUtcNow());
        }

        var wantActive = AreChipsEnabled && permissions is not null && !_activeFileRemoved &&
            permissions.AutomaticContext.ActiveFile && permissions.DataSending.ActiveFile && context.TabId is not null;
        var wantTab = AreChipsEnabled && permissions is not null && !_tabMetadataRemoved &&
            permissions.AutomaticContext.TabMetadata && permissions.DataSending.TabMetadata && context.ConnectionName is not null;

        UpsertAutomatic(AgentAttachmentKind.ActiveFile, wantActive,
            ActiveFileLabel(context), Text.Resolve("agentChipActive"));
        UpsertAutomatic(AgentAttachmentKind.TabMetadata, wantTab,
            string.Join(" › ", new[] { context.ConnectionName, context.DatabaseName, context.CollectionName }
                .Where(static part => !string.IsNullOrWhiteSpace(part))), null);
        if (!AreChipsEnabled)
        {
            foreach (var manual in Chips.Where(static chip => !chip.IsAutomatic).ToArray())
            {
                Chips.Remove(manual);
            }
        }

        OnPropertyChanged(nameof(HasChips));
        OnPropertyChanged(nameof(ContextSummary));
        OnPropertyChanged(nameof(AreChipsEnabled));
        OnPropertyChanged(nameof(ShowChipsDisabledNotice));
        OnPropertyChanged(nameof(CanAttachExternal));
        AttachWorkspaceFileCommand.NotifyCanExecuteChanged();
        AttachExternalFileCommand.NotifyCanExecuteChanged();
        ScheduleContextMeasurement();
    }

    private static string ActiveFileLabel(AgentWorkspaceContext context) =>
        !string.IsNullOrWhiteSpace(context.ActiveFileName) ? context.ActiveFileName
        : !string.IsNullOrWhiteSpace(context.ActiveFilePath) ? Path.GetFileName(context.ActiveFilePath)
        : Text.Resolve("agentChipUntitled");

    private void UpsertAutomatic(AgentAttachmentKind kind, bool wanted, string name, string? qualifier)
    {
        var existing = Chips.FirstOrDefault(chip => chip.IsAutomatic && chip.Kind == kind);
        if (!wanted)
        {
            if (existing is not null)
            {
                Chips.Remove(existing);
            }

            return;
        }

        if (existing is null)
        {
            // Automatic chips come first, active file before tab metadata.
            var index = kind == AgentAttachmentKind.ActiveFile ? 0 : Chips.Count(static chip => chip.IsAutomatic);
            Chips.Insert(Math.Min(index, Chips.Count), new AgentContextChipViewModel(kind, name, qualifier, null, isAutomatic: true));
            return;
        }

        existing.Name = name;
        existing.Qualifier = qualifier;
        existing.Error = null;
    }

    /// <summary>"×" of a chip: removed for this message only (automatic chips return after sending).</summary>
    [RelayCommand]
    private void RemoveChip(AgentContextChipViewModel? chip)
    {
        if (chip is null || !Chips.Remove(chip))
        {
            return;
        }

        if (chip.IsAutomatic)
        {
            if (chip.Kind == AgentAttachmentKind.ActiveFile)
            {
                _activeFileRemoved = true;
            }
            else
            {
                _tabMetadataRemoved = true;
            }
        }

        OnPropertyChanged(nameof(HasChips));
        OnPropertyChanged(nameof(ContextSummary));
        ComposerFocusRequested?.Invoke(this, EventArgs.Empty);
    }

    private bool CanAttachWorkspaceFile() => AreChipsEnabled && IsIdle;

    /// <summary>"+ Anexar → Arquivo do workspace…": loads the filtered listing for the picker.</summary>
    [RelayCommand(CanExecute = nameof(CanAttachWorkspaceFile))]
    private Task AttachWorkspaceFileAsync()
    {
        var permissions = CurrentPermissions is { IsWellFormed: true } valid ? valid : null;
        return WorkspaceFilePicker.LoadAsync(SessionFolderCandidate(),
            permissions?.Workspace.EffectiveExclusions ?? AgentWorkspacePermissions.DefaultExclusions,
            permissions is { DataSending.WorkspaceFiles: true }, _lifetime.Token);
    }

    /// <summary>Adds the file chosen in the workspace picker. A refusal is shown on the chip itself.</summary>
    public AgentContextChipViewModel? AddWorkspaceFile(string path)
    {
        if (!AreChipsEnabled || string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var permissions = CurrentPermissions is { IsWellFormed: true } valid ? valid : null;
        var folder = SessionFolderCandidate();
        var chip = new AgentContextChipViewModel(AgentAttachmentKind.WorkspaceFile, Path.GetFileName(path), null, path, isAutomatic: false);
        if (!AgentWorkspacePaths.TryResolveInside(folder, path,
                permissions?.Workspace.EffectiveExclusions ?? AgentWorkspacePermissions.DefaultExclusions,
                out _, out var relative, out var error, _services.PathProbe))
        {
            chip.Error = error switch
            {
                AgentWorkspacePathError.NoWorkspace => AgentAttachmentError.NoWorkspace,
                AgentWorkspacePathError.Excluded => AgentAttachmentError.Excluded,
                AgentWorkspacePathError.InvalidExclusion => AgentAttachmentError.InvalidExclusion,
                AgentWorkspacePathError.UnsafePath => AgentAttachmentError.UnsafePath,
                AgentWorkspacePathError.OutsideWorkspace or AgentWorkspacePathError.LinkTraversal => AgentAttachmentError.OutsideWorkspace,
                _ => AgentAttachmentError.InvalidPath,
            };
        }
        else
        {
            chip.Qualifier = Path.GetDirectoryName(relative.Replace('/', Path.DirectorySeparatorChar)) is { Length: > 0 } directory
                ? directory.Replace(Path.DirectorySeparatorChar, '/')
                : null;
            if (permissions is not { DataSending.WorkspaceFiles: true })
            {
                chip.Error = AgentAttachmentError.NotPermitted;
            }
        }

        if (Chips.Any(existing => existing.Kind == chip.Kind && string.Equals(existing.Path, chip.Path, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        Chips.Add(chip);
        OnPropertyChanged(nameof(HasChips));
        OnPropertyChanged(nameof(ContextSummary));
        ComposerFocusRequested?.Invoke(this, EventArgs.Empty);
        return chip;
    }

    private bool CanAttachExternalFile() => CanAttachExternal && IsIdle;

    /// <summary>"+ Anexar → Arquivo externo…": the view opens the native dialog (only when permitted).</summary>
    [RelayCommand(CanExecute = nameof(CanAttachExternalFile))]
    private void AttachExternalFile() => ExternalFilePickRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Adds a file picked in the native dialog. Only its file name is ever shown or persisted.</summary>
    public AgentContextChipViewModel? AddExternalFile(string path)
    {
        if (!CanAttachExternal || string.IsNullOrWhiteSpace(path) ||
            Chips.Any(existing => string.Equals(existing.Path, path, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var permissions = CurrentPermissions!;
        var chip = new AgentContextChipViewModel(AgentAttachmentKind.ExternalFile, Path.GetFileName(path), null, path, isAutomatic: false);
        var check = AgentWorkspacePaths.CheckFile(path, SessionFolderCandidate(), permissions.Workspace.EffectiveExclusions, _services.PathProbe);
        if (check == AgentWorkspacePathError.Excluded)
        {
            chip.Error = AgentAttachmentError.Excluded;
        }
        else if (check != AgentWorkspacePathError.None)
        {
            chip.Error = AgentAttachmentError.InvalidPath;
        }

        Chips.Add(chip);
        OnPropertyChanged(nameof(HasChips));
        OnPropertyChanged(nameof(ContextSummary));
        ComposerFocusRequested?.Invoke(this, EventArgs.Empty);
        return chip;
    }

    /// <summary>After a send: manual chips are consumed and removed automatic chips come back.</summary>
    private void ResetChipsAfterSend()
    {
        foreach (var manual in Chips.Where(static chip => !chip.IsAutomatic).ToArray())
        {
            Chips.Remove(manual);
        }

        _activeFileRemoved = false;
        _tabMetadataRemoved = false;
        RefreshAutomaticChips();
    }
}

/// <summary>
/// Workspace file picker of "+ Anexar": lists files of the Files panel folder off the UI thread, keeping only those that
/// <see cref="AgentWorkspacePaths.TryResolveInside"/> accepts (containment, alias segments, links and the exclusion globs
/// of the permissions), bounded in count. The listing is only names; nothing is read.
/// </summary>
public sealed partial class AgentWorkspaceFilePickerViewModel : ObservableObject
{
    private readonly IAgentWorkspacePathProbe? _pathProbe;
    private readonly IAgentWorkspaceFileCatalog? _catalog;
    public AgentWorkspaceFilePickerViewModel(IAgentWorkspacePathProbe? pathProbe = null, IAgentWorkspaceFileCatalog? catalog = null)
    {
        _pathProbe = pathProbe;
        _catalog = catalog;
    }
    public const int MaximumListed = 1000;
    private const int MaximumVisited = 20000;
    private static LocalizationViewModel Text => LocalizationViewModel.Current;
    private IReadOnlyList<AgentWorkspaceFileChoice> _all = [];
    private int _generation;

    public ObservableCollection<AgentWorkspaceFileChoice> Files { get; } = [];

    [ObservableProperty] private string _search = "";

    [ObservableProperty] private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string _status = "";

    public bool HasStatus => Status.Length > 0;

    partial void OnSearchChanged(string value) => ApplyFilter();

    public async Task LoadAsync(string? folder, IReadOnlyList<string> exclusions, bool permitted, CancellationToken cancellationToken)
    {
        var generation = ++_generation;
        _all = [];
        Files.Clear();
        IsLoading = false;
        Search = "";
        if (!permitted)
        {
            Status = Text.Resolve("agentPickerNotPermitted");
            return;
        }

        if (!AgentWorkspacePaths.TryGetWorkspaceRoot(folder, out var root, _pathProbe))
        {
            Status = Text.Resolve("agentPickerNoWorkspace");
            return;
        }

        var capturedExclusions = exclusions.ToArray();
        if (!capturedExclusions.All(AgentWorkspaceExclusions.IsValidPattern))
        {
            Status = Text.Resolve("agentPickerInvalidExclusion");
            return;
        }

        IsLoading = true;
        Status = Text.Resolve("agentPickerLoading");
        try
        {
            if (_catalog is null) throw new InvalidOperationException("Workspace file catalog unavailable.");
            var listing = await _catalog.ListAsync(root, capturedExclusions, MaximumListed, MaximumVisited, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var files = listing.Files.Select(static file => new AgentWorkspaceFileChoice(file.FullPath, file.RelativePath)).ToArray();
            var truncated = listing.Truncated;
            if (generation != _generation)
            {
                return;
            }

            _all = files;
            ApplyFilter();
            Status = files.Length == 0 ? Text.Resolve("agentPickerEmpty")
                : truncated ? Text.Format("agentPickerTruncated", MaximumListed) : "";
        }
        catch (OperationCanceledException)
        {
            if (generation == _generation) Status = "";
        }
        catch (Exception)
        {
            if (generation == _generation)
            {
                Status = Text.Resolve("agentPickerFailed");
            }
        }
        finally
        {
            if (generation == _generation)
            {
                IsLoading = false;
            }
        }
    }

    private void ApplyFilter()
    {
        Files.Clear();
        var term = Search.Trim();
        foreach (var file in _all.Where(file => term.Length == 0 || file.RelativePath.Contains(term, StringComparison.OrdinalIgnoreCase)).Take(200))
        {
            Files.Add(file);
        }
    }

}

/// <summary>One file offered by the workspace picker (relative path shown; full path used to resolve).</summary>
public sealed record AgentWorkspaceFileChoice(string FullPath, string RelativePath)
{
    public string Name => Path.GetFileName(RelativePath);

    public override string ToString() => RelativePath;
}
