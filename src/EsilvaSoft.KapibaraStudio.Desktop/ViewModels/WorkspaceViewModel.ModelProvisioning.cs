using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;

namespace EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

public sealed partial class WorkspaceViewModel
{
    private readonly ISnapdragonModelProvisioner? _modelProvisioner;
    private readonly CancellationTokenSource _modelProvisioningLifetime = new();
    private bool _modelProvisioningStarted;

    /// <summary>Startup preparation, detached from windows/editor requests and cancelled with the workspace.</summary>
    public Task ModelProvisioning { get; private set; } = Task.CompletedTask;
    public SnapdragonProvisioningReport? ModelProvisioningReport { get; private set; }

    private void StartModelProvisioning()
    {
        if (_modelProvisioningStarted || _modelProvisioner is null || _disposed) return;
        _modelProvisioningStarted = true;
        _modelProvisioner.SetLocalization(LocalizationViewModel.Current.Resolve);
        // Capture the restored directory; this path never changes model/role/provider preferences.
        var directory = AutocompletePreferences.EffectiveModelDirectory;
        var token = _modelProvisioningLifetime.Token;
        ModelProvisioning = Task.Run(async () =>
        {
            try { ModelProvisioningReport = await _modelProvisioner.ProvisionAsync(directory, cancellationToken: token).ConfigureAwait(false); }
            catch (OperationCanceledException) { /* The operation coordinator already exposes cancellation. */ }
            catch (Exception ex)
            {
                if (token.IsCancellationRequested || _disposed) return;
                using var operation = _workspace.Operations.Begin(LocalizationViewModel.Current.Format("snapdragonProvisioning", directory), ApplicationOperationPriority.Low, false);
                operation.Complete(ApplicationOperationStatus.Error, LocalizationViewModel.Current.Format("modelNotDownloaded", ex.Message));
            }
        }, CancellationToken.None);
    }
}
