using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

/// <summary>Top-bar update action: checks periodically, downloads on request and reports through the global operation bar.</summary>
public sealed partial class AppUpdateViewModel : ObservableObject, IDisposable
{
    private static string T(string key) => LocalizationViewModel.Current.Resolve(key);
    private static string F(string key, params object?[] args) => LocalizationViewModel.Current.Format(key, args);
    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);
    private readonly IAppUpdateService? _updates;
    private readonly IApplicationOperationService _operations;
    private readonly SynchronizationContext? _context = SynchronizationContext.Current;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Timer? _schedule;
    private bool _disposed;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsVisible), nameof(Label))] private AppUpdateUiState _state;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Label))] private double _progress;
    [ObservableProperty] private string _toolTip = "";

    public AppUpdateViewModel(IAppUpdateService? updates, IApplicationOperationService operations)
    {
        _updates = updates;
        _operations = operations;
        if (updates is null || updates.Availability == AppUpdateAvailability.Disabled) return;
        if (updates.GetStagedUpdate() is { } staged) SetReady(staged);
        _schedule = new Timer(_ => Dispatch(() => _ = CheckAsync()), null, FirstCheckDelay, CheckInterval);
    }

    public AppUpdateRelease? Release { get; private set; }
    public AppVersion? ReadyVersion { get; private set; }
    public bool IsVisible => State != AppUpdateUiState.Hidden;
    public string Label => State switch
    {
        AppUpdateUiState.Downloading => F("updateDownloading", Progress.ToString("F0", System.Globalization.CultureInfo.InvariantCulture)),
        AppUpdateUiState.Ready => T("updateRestart"),
        _ => T("update")
    };

    /// <summary>Queries the release feed; failures stay silent because the next scheduled check retries.</summary>
    public async Task CheckAsync()
    {
        if (_disposed || _updates is null || _updates.Availability == AppUpdateAvailability.Disabled || State is AppUpdateUiState.Downloading or AppUpdateUiState.Ready) return;
        AppUpdateRelease? release;
        try { release = await _updates.CheckAsync(_lifetime.Token); }
        catch (Exception) { return; }
        if (_disposed || release is null || State is AppUpdateUiState.Downloading or AppUpdateUiState.Ready) return;
        Release = release;
        if (_updates.Availability == AppUpdateAvailability.ManualOnly)
        {
            State = AppUpdateUiState.ManualOnly;
            ToolTip = F("updateAvailableManual", release.Version);
            return;
        }
        State = AppUpdateUiState.Available;
        ToolTip = F("updateAvailable", release.Version, _updates.CurrentVersion);
    }

    [RelayCommand]
    private async Task DownloadAsync()
    {
        if (_disposed || _updates is null || Release is not { } release || State != AppUpdateUiState.Available) return;
        State = AppUpdateUiState.Downloading;
        Progress = 0;
        ToolTip = F("updateDownloadTip", release.Version);
        using var operation = _operations.Begin(F("updateDownloadingOperation", release.Version), ApplicationOperationPriority.Normal, canCancel: true, _lifetime.Token);
        void OnProgress(object? sender, EventArgs args) =>
            Dispatch(() => { if (State == AppUpdateUiState.Downloading && operation.Snapshot.Progress is { } value) Progress = value; });
        _operations.Changed += OnProgress;
        try
        {
            // Hashing and extraction of a large package stay off the UI thread.
            var staged = await Task.Run(() => _updates.DownloadAsync(release, operation));
            operation.Complete(ApplicationOperationStatus.Success, F("updateReadyOperation", release.Version));
            if (!_disposed) SetReady(staged);
        }
        catch (OperationCanceledException) when (operation.Token.IsCancellationRequested)
        {
            operation.Complete(ApplicationOperationStatus.Cancelled, T("updateDownloadCancelled"));
            Restore(release, T("updateCancelled"));
        }
        catch (Exception ex)
        {
            operation.Complete(ApplicationOperationStatus.Error, F("updateNotDownloaded", ex.Message));
            Restore(release, F("updateDownloadFailed", release.Version, ex.Message));
        }
        finally { _operations.Changed -= OnProgress; }
    }

    private void SetReady(StagedAppUpdate staged)
    {
        ReadyVersion = staged.Version;
        State = AppUpdateUiState.Ready;
        Progress = 100;
        ToolTip = staged.LastApplyError is { } error
            ? F("updateInstallFailed", staged.Version, error)
            : F("updateReady", staged.Version);
    }

    private void Restore(AppUpdateRelease release, string reason)
    {
        if (_disposed) return;
        State = AppUpdateUiState.Available;
        Progress = 0;
        ToolTip = F("updateRetry", reason, release.Version);
    }

    private void Dispatch(Action action)
    {
        if (_disposed) return;
        if (_context is null) action(); else _context.Post(_ => { if (!_disposed) action(); }, null);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _schedule?.Dispose();
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
