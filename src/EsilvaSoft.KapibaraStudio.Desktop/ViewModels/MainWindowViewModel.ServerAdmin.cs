using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanKillOperation))]
    [NotifyCanExecuteChangedFor(nameof(KillOperationCommand))]
    private string _operationIdToKill = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanKillOperation))]
    [NotifyCanExecuteChangedFor(nameof(KillOperationCommand))]
    private string _operationKillConfirmation = string.Empty;

    [ObservableProperty]
    private string _administrationResults = T("adminInitial");

    public bool CanKillOperation => SelectedProfile is not null
        && !SelectedProfile.IsReadOnly
        && string.Equals(OperationIdToKill.Trim(), OperationKillConfirmation.Trim(), StringComparison.Ordinal);

    [RelayCommand(CanExecute = nameof(HasSelectedProfile))]
    private async Task LoadServerStatusAsync()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            AdministrationResults = await _workspace.GetServerStatusAsync(SelectedProfile, cancellationToken);
            StatusMessage = T("serverStatusLoaded");
        });
    }

    [RelayCommand(CanExecute = nameof(HasSelectedProfile))]
    private async Task LoadCurrentOperationsAsync()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            AdministrationResults = await _workspace.GetCurrentOperationsAsync(SelectedProfile, cancellationToken);
            StatusMessage = T("currentOperationsLoaded");
        });
    }

    [RelayCommand(CanExecute = nameof(CanLoadDatabaseStats))]
    private async Task LoadProfilerStatusAsync()
    {
        if (SelectedProfile is null || string.IsNullOrWhiteSpace(SelectedDatabase))
        {
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            AdministrationResults = await _workspace.GetProfilerStatusAsync(SelectedProfile, SelectedDatabase, cancellationToken);
            StatusMessage = T("profilerLoaded");
        });
    }

    [RelayCommand(CanExecute = nameof(HasSelectedProfile))]
    private async Task LoadTopologyAsync()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            AdministrationResults = await _workspace.GetTopologyAsync(SelectedProfile, cancellationToken);
            StatusMessage = T("topologyLoaded");
        });
    }

    [RelayCommand(CanExecute = nameof(CanKillOperation))]
    private async Task KillOperationAsync()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        var request = new OperationKillRequest(OperationIdToKill, OperationKillConfirmation);
        if (!await RunAsync(cancellationToken => _workspace.KillOperationAsync(SelectedProfile, request, cancellationToken)))
        {
            return;
        }

        var operationId = request.GetOperationId();
        OperationIdToKill = string.Empty;
        OperationKillConfirmation = string.Empty;
        await RecordAuditAsync("operation.kill", SelectedProfile, "admin", null, F("killRequested", operationId));
        StatusMessage = F("killRequested", operationId);
    }
}
