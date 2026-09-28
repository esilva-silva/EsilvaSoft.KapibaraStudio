using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EsilvaSoft.KapibaraStudio.Application;

namespace EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

public sealed partial class ApplicationStatusViewModel : ObservableObject, IDisposable
{
    private readonly IApplicationOperationService _operations;
    private readonly SynchronizationContext? _context = SynchronizationContext.Current;
    private readonly Timer _expiry;
    private Guid? _selectedId;
    private Guid? _lastTerminalId;
    private bool _disposed;
    [ObservableProperty] private string _description = LocalizationViewModel.Current.Resolve("statusReady");
    [ObservableProperty] private string _additional = "";
    [ObservableProperty] private string _state = LocalizationViewModel.Current.Resolve("statusReady");
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _isIndeterminate;
    [ObservableProperty] private bool _canCancel;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _progressLabel = "";

    public ApplicationStatusViewModel(IApplicationOperationService operations)
    {
        _operations = operations;
        _expiry = new Timer(_ => Dispatch(Expire), null, Timeout.Infinite, Timeout.Infinite);
        _operations.Changed += Changed;
        Refresh();
    }

    private void Dispatch(Action action) { if (_disposed) return; if (_context is null) action(); else _context.Post(_ => { if (!_disposed) action(); }, null); }
    private void Changed(object? sender, EventArgs args) => Dispatch(Refresh);
    private void Refresh()
    {
        if (_disposed) return;
        var active = _operations.ActiveOperations;
        var current = active.Count == 0 ? null : active[0];
        IsRunning = current is not null;
        Additional = active.Count > 1 ? LocalizationViewModel.Current.Format("additionalOperations", active.Count - 1) : "";
        CanCancel = current?.CanCancel == true;
        _selectedId = current?.Id;
        IsIndeterminate = current?.IsIndeterminate == true;
        Progress = current?.Progress ?? 0;
        ProgressLabel = current?.Progress is { } percentValue ? $"{percentValue:F0}%" : "";
        if (current is not null)
        {
            _expiry.Change(Timeout.Infinite, Timeout.Infinite); State = LocalizationViewModel.Current.Resolve("statusRunning"); Description = current.Description;
        }
        else if (_operations.LastCompleted is { } terminal && terminal.Id != _lastTerminalId)
        {
            _lastTerminalId = terminal.Id;
            State = terminal.Status switch {
                ApplicationOperationStatus.Success => LocalizationViewModel.Current.Resolve("statusSuccess"),
                ApplicationOperationStatus.Error => LocalizationViewModel.Current.Resolve("statusError"),
                ApplicationOperationStatus.Cancelled => LocalizationViewModel.Current.Resolve("statusCancelled"),
                _ => LocalizationViewModel.Current.Resolve("statusWarning") };
            Description = terminal.Description;
            _expiry.Change(TimeSpan.FromSeconds(6), Timeout.InfiniteTimeSpan);
        }
    }

    private void Expire() { _expiry.Change(Timeout.Infinite, Timeout.Infinite); if (!IsRunning) { State = LocalizationViewModel.Current.Resolve("statusReady"); Description = LocalizationViewModel.Current.Resolve("statusReady"); } }
    [RelayCommand] private void Cancel() { if (_selectedId is { } id) _operations.Cancel(id); }
    public void Dispose() { _disposed = true; _operations.Changed -= Changed; _expiry.Change(Timeout.Infinite, Timeout.Infinite); _expiry.Dispose(); }
}
