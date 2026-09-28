using EsilvaSoft.SlopStudio.Application;

namespace EsilvaSoft.SlopStudio.Desktop.ViewModels;

/// <summary>
/// Startup report of durable credential recovery records (lote 1 journals, including those of deleted profiles). The
/// count is read once in the background and published through the existing operation coordinator, so the status bar
/// shows it as text; initialization never waits for it and a failure is visible instead of silent. No URI, secret or
/// profile detail is shown — only the number.
/// </summary>
public sealed partial class WorkspaceViewModel
{
    private readonly IConnectionProfileCredentialStatusProvider? _credentialStatus;
    private readonly CancellationTokenSource _credentialCheckLifetime = new();

    /// <summary>Last count read at startup; null while unknown (not composed, not finished, failed or cancelled).</summary>
    public int? PendingCredentialRecoveryCount { get; private set; }

    /// <summary>The background check started by <see cref="InitializeAsync"/> (completed when not composed).</summary>
    public Task CredentialRecoveryCheck { get; private set; } = Task.CompletedTask;

    private async Task ReportCredentialRecoveryAsync()
    {
        if (_credentialStatus is not { } status || _disposed)
        {
            return;
        }

        using var operation = _workspace.Operations.Begin(T("credentialRecoveryChecking"), ApplicationOperationPriority.Low,
            canCancel: true, _credentialCheckLifetime.Token);
        try
        {
            var count = await status.CountPendingCredentialRecoveryAsync(operation.Token);
            PendingCredentialRecoveryCount = Math.Max(0, count);
            operation.Complete(count > 0 ? ApplicationOperationStatus.Warning : ApplicationOperationStatus.Success,
                count > 0 ? F("credentialRecoveryPending", count) : T("credentialRecoveryNone"));
        }
        catch (OperationCanceledException) when (operation.Token.IsCancellationRequested)
        {
            operation.Complete(ApplicationOperationStatus.Cancelled, T("credentialRecoveryCancelled"));
        }
        catch (Exception)
        {
            // The repository message may describe paths or storage internals; only a fixed sentence is shown.
            operation.Complete(ApplicationOperationStatus.Error, T("credentialRecoveryFailed"));
        }
    }

    private void CancelCredentialRecoveryCheck()
    {
        _credentialCheckLifetime.Cancel();
        _credentialCheckLifetime.Dispose();
    }
}
