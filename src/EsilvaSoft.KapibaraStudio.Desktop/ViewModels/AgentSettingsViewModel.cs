using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Desktop.Agents;

namespace EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

/// <summary>Texts of the global sign-out confirmation, built by the ViewModel and shown by the view.</summary>
public sealed record AgentCliSignOutPrompt(string Title, string Message, string ConfirmText, string CancelText);

/// <summary>
/// Provider configuration driven by capabilities. Opening it only lists the catalog: it never starts authentication,
/// runs a CLI or touches the vault. Only officially supported methods appear. Storing a key is an explicit action and
/// never consents to sending data; the key is not echoed in status, logs or bindings after saving.
/// </summary>
/// <remarks>
/// Providers whose account belongs to an official CLI (<c>OfficialCliDelegated</c>) get a section with installation,
/// authentication and account-type states, "Test connection" (re-detects and re-reads the status, never calls the
/// model), sign-in in a visible CLI window and a global sign-out that requires an explicit confirmation. Only states,
/// a version, the subscription tier and names of blocking variables are shown — never e-mail, organization or tokens.
/// </remarks>
public sealed partial class AgentSettingsViewModel : ObservableObject, IDisposable
{
    private readonly IAgentProviderCatalog? _catalog;
    private readonly IAgentApiKeyStore? _credentials;
    private readonly IAgentAccountManager? _accountManager;
    private readonly IAgentCliAccountPresentation? _cliPresentation;
    private readonly AgentProviderAvailabilityService? _availability;
    private readonly Func<string?>? _captureWorkspaceFolder;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _signInWait;

    public AgentSettingsViewModel(IAgentProviderCatalog? catalog, IAgentApiKeyStore? credentials, string? providerId = null,
        IAgentAccountManager? accountManager = null, IAgentCliAccountPresentation? cliPresentation = null,
        AgentProviderAvailabilityService? availability = null, Func<string?>? captureWorkspaceFolder = null)
    {
        _captureWorkspaceFolder = captureWorkspaceFolder;
        _catalog = catalog;
        _credentials = credentials;
        _accountManager = accountManager;
        _cliPresentation = cliPresentation ?? accountManager as IAgentCliAccountPresentation;
        _availability = availability;
        Reload(providerId);
    }

    private static LocalizationViewModel Text => LocalizationViewModel.Current;

    public ObservableCollection<AgentProviderOption> Providers { get; } = [];

    public bool HasProviders => Providers.Count > 0;

    public bool CanSetUpCredentials => _credentials is not null;

    /// <summary>Asked before a global sign-out; returns true only when the user explicitly confirmed. Set by the view.</summary>
    public Func<AgentCliSignOutPrompt, Task<bool>>? ConfirmSignOut { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RequiresApiKey), nameof(HasSelection), nameof(IsCliProvider), nameof(IsApiKeyProvider),
        nameof(CliProfile), nameof(SelectedModeText), nameof(HasSelectedModeText))]
    [NotifyCanExecuteChangedFor(nameof(SaveApiKeyCommand), nameof(RemoveApiKeyCommand), nameof(TestConnectionCommand),
        nameof(SignInCommand), nameof(SignOutCommand))]
    private AgentProviderOption? _selectedProvider;

    /// <summary>Bound to a masked input. Cleared as soon as a save starts.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveApiKeyCommand))]
    private string _apiKey = "";

    [ObservableProperty] private string _statusText = "";

    [ObservableProperty] private bool _isStatusError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle), nameof(ShowProgress))]
    [NotifyCanExecuteChangedFor(nameof(SaveApiKeyCommand), nameof(RemoveApiKeyCommand), nameof(TestConnectionCommand),
        nameof(SignInCommand), nameof(SignOutCommand), nameof(StopWaitingCommand))]
    private bool _isBusy;

    /// <summary>Progress sentence of the running account operation (checking, waiting for the sign-in window…).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowProgress))]
    private string _progressText = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StopWaitingCommand))]
    private bool _isWaitingForSignIn;

    /// <summary>Last explicit check of the selected CLI provider; null until "Test connection" runs.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CliInstallText), nameof(CliAuthText), nameof(CliAuthExplanation), nameof(HasCliAuthExplanation),
        nameof(IsCliAuthBlocked), nameof(IsCliSignedIn), nameof(IsCliAuthenticationConfigured), nameof(CliAccountTypeText), nameof(IsCliChecked), nameof(CliExecutableText),
        nameof(HasCliExecutable))]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand), nameof(SignOutCommand))]
    private AgentAccountStatus? _cliStatus;

    /// <summary>Exact command for platforms where no visible terminal can be opened ("Copy command").</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowManualCommand))]
    private string? _manualCommand;

    public bool IsIdle => !IsBusy;

    public bool ShowProgress => IsBusy && ProgressText.Length > 0;

    public bool ShowManualCommand => !string.IsNullOrEmpty(ManualCommand);

    public bool HasSelection => SelectedProvider is not null;

    public bool RequiresApiKey => SelectedProvider?.RequiresApiKey == true;

    public bool IsApiKeyProvider => RequiresApiKey && !IsCliProvider;

    public string? SelectedModeText => SelectedProvider?.ModeText;

    public bool HasSelectedModeText => SelectedModeText is not null;

    public string CredentialSetupNote => Text.Resolve(CanSetUpCredentials ? "agentSettingsVaultNote" : "agentKeySetupUnavailable");

    /// <summary>Profile of the selected provider when its account belongs to an official CLI managed by the host.</summary>
    public AgentCliProviderProfile? CliProfile =>
        SelectedProvider is { UsesOfficialCli: true } provider ? SafeDescribe(provider.ProviderId) : null;

    public bool IsCliProvider => CliProfile is not null;

    /// <summary>A CLI-delegated provider without a host adapter: the account actions are not offered, with a reason.</summary>
    public bool IsCliUnmanaged => SelectedProvider?.UsesOfficialCli == true && CliProfile is null;

    public bool IsCliChecked => CliStatus is not null;

    public bool IsCliSignedIn => CliStatus?.Auth == AgentAccountAuthState.Subscription;

    /// <summary>Any recognized native authentication method reported by the official CLI, without exposing credentials.</summary>
    public bool IsCliAuthenticationConfigured => CliStatus?.Auth is AgentAccountAuthState.Subscription or
        AgentAccountAuthState.ApiKey or AgentAccountAuthState.ApiKeyHelper or AgentAccountAuthState.EnvironmentToken or
        AgentAccountAuthState.CloudProvider or AgentAccountAuthState.UnsupportedMethod;

    /// <summary>Methods explicitly blocked by the CLI environment, not merely methods this panel does not classify.</summary>
    public bool IsCliAuthBlocked => CliStatus?.Auth == AgentAccountAuthState.BlockedEnvironment;

    public string CliInstallText => CliStatus is not { } status
        ? Text.Resolve("agentCliNotChecked")
        : status.Install switch
        {
            AgentAccountInstallState.Installed => Text.Format("agentCliInstalled", status.Version ?? "?"),
            AgentAccountInstallState.NotFound => Text.Format("agentCliNotFound", CliName),
            AgentAccountInstallState.UnsupportedExecutable => Text.Format("agentCliUnsupportedExecutable", CliName),
            AgentAccountInstallState.VersionTooLow => Text.Format("agentCliVersionTooLow", status.Version ?? "?", CliName),
            AgentAccountInstallState.VersionUnreadable => Text.Format("agentCliVersionUnreadable", CliName),
            AgentAccountInstallState.TimedOut => Text.Format("agentCliCheckTimedOut", CliName),
            _ => Text.Format("agentCliCheckFailed", CliName),
        };

    /// <summary>Resolved executable (local path of the user's own installation), shown after a check.</summary>
    public string CliExecutableText => CliStatus?.ExecutablePath is { Length: > 0 } path ? Text.Format("agentCliExecutablePath", path) : "";

    public bool HasCliExecutable => CliExecutableText.Length > 0;

    public string CliAuthText => CliStatus is not { } status
        ? Text.Resolve("agentCliNotChecked")
        : Text.Resolve(status.Auth switch
        {
            AgentAccountAuthState.Subscription => "agentCliAuthSubscription",
            AgentAccountAuthState.ApiKey => "agentCliAuthApiKey",
            AgentAccountAuthState.ApiKeyHelper => "agentCliAuthApiKeyHelper",
            AgentAccountAuthState.EnvironmentToken => "agentCliAuthEnvironmentToken",
            AgentAccountAuthState.CloudProvider => "agentCliAuthCloudProvider",
            AgentAccountAuthState.UnsupportedMethod => "agentCliAuthNativeUnclassified",
            AgentAccountAuthState.SignedOut => "agentCliAuthSignedOut",
            AgentAccountAuthState.NotChecked => "agentCliAuthNotChecked",
            AgentAccountAuthState.Unreadable => "agentCliAuthUnreadable",
            _ => "agentCliAuthBlocked",
        });

    /// <summary>Safe explanation of the effective CLI authentication, without credential values.</summary>
    public string CliAuthExplanation => CliStatus is not { } status
        ? ""
        : status.Auth switch
        {
            AgentAccountAuthState.ApiKey => Text.Format("agentCliNativeAuthNotice", CliName, Text.Resolve("agentCliAuthKindApiKey")),
            AgentAccountAuthState.ApiKeyHelper => Text.Format("agentCliNativeAuthNotice", CliName, Text.Resolve("agentCliAuthKindApiKeyHelper")),
            AgentAccountAuthState.EnvironmentToken => Text.Format("agentCliNativeAuthNotice", CliName, Text.Resolve("agentCliAuthKindToken")),
            AgentAccountAuthState.CloudProvider => Text.Format("agentCliNativeAuthNotice", CliName, Text.Resolve("agentCliAuthKindCloud")),
            AgentAccountAuthState.UnsupportedMethod => Text.Format("agentCliNativeAuthUnclassifiedNotice", CliName),
            AgentAccountAuthState.BlockedEnvironment when status.BlockingSource is { Length: > 0 } source =>
                Text.Format("agentCliBlockedVariable", source, CliName),
            AgentAccountAuthState.BlockedEnvironment => Text.Format("agentCliBlockedEnvironment", CliName),
            AgentAccountAuthState.SignedOut => Text.Format("agentCliSignedOutHint", SignInButtonText),
            AgentAccountAuthState.Unreadable => Text.Format("agentCliAuthUnreadableHint", CliName),
            _ => "",
        };

    public bool HasCliAuthExplanation => CliAuthExplanation.Length > 0;

    /// <summary>Only the tier token reported by the CLI (e.g. "Pro"); never e-mail, organization or account IDs.</summary>
    public string CliAccountTypeText => CliStatus switch
    {
        { Auth: AgentAccountAuthState.Subscription } status => string.IsNullOrWhiteSpace(status.SubscriptionTier)
            ? Text.Resolve("agentAccountTypeSubscription")
            : Text.Format("agentAccountTypeSubscriptionTier", Capitalize(status.SubscriptionTier)),
        { Auth: AgentAccountAuthState.ApiKey } => Text.Resolve("agentCliBillingApiKey"),
        { Auth: AgentAccountAuthState.ApiKeyHelper } => Text.Resolve("agentCliBillingApiKeyHelper"),
        { Auth: AgentAccountAuthState.EnvironmentToken } => Text.Resolve("agentCliBillingToken"),
        { Auth: AgentAccountAuthState.CloudProvider } => Text.Resolve("agentCliBillingCloud"),
        { Auth: AgentAccountAuthState.UnsupportedMethod } => Text.Resolve("agentCliBillingNativeUnclassified"),
        _ => Text.Resolve("agentAccountTypeNotReported"),
    };

    private string CliName => CliProfile?.CliName ?? "";

    public string SignInButtonText => Text.Format("agentCliSignIn", CliName);

    public string SignInHint => CliProfile is { UsesBrowserAppServerLogin: true } profile
        ? Text.Format("agentAppServerSignInHint", profile.CliName, profile.RecipientName, Branding.ProductName)
        : Text.Format("agentCliSignInHint", CliName, CliProfile?.RecipientName ?? "", Branding.ProductName);

    public string SignOutHint => Text.Format("agentCliSignOutHint", CliName);

    public string ReadScopeText
    {
        get
        {
            if (CliProfile is not { } profile || SelectedProvider is null)
            {
                return "";
            }

            return SafeReadScope(SelectedProvider.ProviderId) is { } scope
                ? AgentCliReadScopeText.Describe(scope, profile) + " " + Text.Resolve("agentCliReadScopeNewSessions")
                : "";
        }
    }

    /// <summary>
    /// With a workspace folder, the CLI's own user settings (permission allow rules, additional directories) could
    /// still grant reads outside that folder; the app cannot see or restrict them, so the limitation is shown (GCL-5).
    /// </summary>
    public string UserRulesNotice => CliProfile is { } profile && SelectedProvider is { } provider && SafeReadScope(provider.ProviderId) is { UsesWorkspace: true }
        ? Text.Format("agentCliNoticeUserRules", profile.CliName)
        : "";

    public bool HasUserRulesNotice => UserRulesNotice.Length > 0;

    public string TranscriptNotice => CliProfile is { } profile ? Text.Format("agentCliNoticeTranscript", profile.CliName, profile.TranscriptLocation, Branding.ProductName) : "";

    public string CredentialNotice => CliProfile is { } profile ? Text.Format(profile.CredentialNoticeKey, profile.CliName, profile.CredentialLocation, Branding.ProductName) : "";

    public string EnvironmentNotice => CliProfile is { } profile ? Text.Format("agentCliNoticeEnvironment", profile.CliName) : "";

    private bool _reloading;

    private void Reload(string? providerId)
    {
        _reloading = true;
        try
        {
            ReloadCore(providerId);
        }
        finally
        {
            _reloading = false;
        }
    }

    private void ReloadCore(string? providerId)
    {
        Providers.Clear();
        IReadOnlyList<AgentProviderPresentation> listed;
        try
        {
            listed = _catalog?.List() ?? [];
        }
        catch (Exception)
        {
            listed = [];
        }

        foreach (var item in listed)
        {
            Providers.Add(new AgentProviderOption(item));
        }

        SelectedProvider = Providers.FirstOrDefault(p => p.ProviderId == providerId) ?? Providers.FirstOrDefault();
        OnPropertyChanged(nameof(HasProviders));
        NotifyCliTexts();
    }

    partial void OnSelectedProviderChanged(AgentProviderOption? oldValue, AgentProviderOption? newValue)
    {
        if (_reloading || oldValue?.ProviderId == newValue?.ProviderId)
        {
            return; // Reload of the same provider keeps the last report.
        }

        ApiKey = "";
        StatusText = "";
        IsStatusError = false;
        CliStatus = null;
        ManualCommand = null;
        NotifyCliTexts();
    }

    private void NotifyCliTexts()
    {
        OnPropertyChanged(nameof(IsCliUnmanaged));
        OnPropertyChanged(nameof(SignInButtonText));
        OnPropertyChanged(nameof(SignInHint));
        OnPropertyChanged(nameof(SignOutHint));
        OnPropertyChanged(nameof(ReadScopeText));
        OnPropertyChanged(nameof(UserRulesNotice));
        OnPropertyChanged(nameof(HasUserRulesNotice));
        OnPropertyChanged(nameof(TranscriptNotice));
        OnPropertyChanged(nameof(CredentialNotice));
        OnPropertyChanged(nameof(EnvironmentNotice));
        OnPropertyChanged(nameof(CliInstallText));
        OnPropertyChanged(nameof(CliAuthText));
        OnPropertyChanged(nameof(CliAuthExplanation));
        OnPropertyChanged(nameof(HasCliAuthExplanation));
        OnPropertyChanged(nameof(CliAccountTypeText));
    }

    private bool CanTestConnection() => !IsBusy && SelectedProvider is not null && _catalog is not null;

    /// <summary>
    /// Re-checks only the selected provider. For a CLI-delegated account it re-detects the executable and re-reads the
    /// authentication status and any supported model catalog metadata; it never sends a prompt or runs inference. For
    /// key providers it re-reads the local configuration/vault presence without network.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanTestConnection))]
    private async Task TestConnectionAsync()
    {
        var providerId = SelectedProvider!.ProviderId;
        var isCli = IsCliProvider;
        ManualCommand = null;
        Begin(Text.Resolve("agentTestingConnection"));
        try
        {
            var failed = false;
            if (_availability is not null)
            {
                var result = await _availability.CheckAsync(providerId, force: true);
                if (isCli)
                {
                    CliStatus = _availability.CurrentAccountStatus(providerId) ??
                        new AgentAccountStatus(AgentAccountInstallState.CheckFailed, null, AgentAccountAuthState.Unreadable);
                }

                failed = result.State is AgentProviderAvailabilityState.Failed or AgentProviderAvailabilityState.CliMissing;
            }
            else
            {
                if (isCli) failed |= !await CheckCliAsync(providerId);
                failed |= !await RefreshProviderAsync(providerId);
            }
            if (_lifetime.IsCancellationRequested)
            {
                return;
            }

            Reload(providerId);
            Report(failed ? "agentTestConnectionFailed" : isCli ? CliOutcomeKey() : "agentTestConnectionDone", failed || (isCli && !IsCliAuthenticationConfigured));
        }
        finally
        {
            End();
        }
    }

    private string CliOutcomeKey() => CliStatus switch
    {
        { Install: not AgentAccountInstallState.Installed } => "agentTestConnectionCliUnavailable",
        { Auth: AgentAccountAuthState.Subscription } => CliProfile?.SubscriptionReadyMessageKey ?? "agentTestConnectionCliReady",
        { Auth: AgentAccountAuthState.UnsupportedMethod or AgentAccountAuthState.ApiKey or AgentAccountAuthState.ApiKeyHelper or AgentAccountAuthState.EnvironmentToken or AgentAccountAuthState.CloudProvider } => "agentTestConnectionCliNativeAuth",
        { Auth: AgentAccountAuthState.BlockedEnvironment } => "agentTestConnectionCliBlocked",
        _ => "agentTestConnectionCliSignedOut",
    };

    private bool CanSignIn() => !IsBusy && IsCliProvider && CliStatus is not { Install: not AgentAccountInstallState.Installed } &&
        !IsCliAuthenticationConfigured && !IsEnvironmentBlocked(CliStatus);

    /// <summary>
    /// Opens the official sign-in in a visible CLI window; the flow finishes with the vendor in the browser. The app
    /// only waits for that window and re-reads the status. "Stop waiting" ends the wait (the window stays with the user).
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSignIn))]
    private async Task SignInAsync()
    {
        var providerId = SelectedProvider!.ProviderId;
        var profile = CliProfile!;
        ManualCommand = null;
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _signInWait = wait;
        Begin(Text.Format("agentCliWaitingSignIn", profile.CliName, profile.RecipientName));
        IsWaitingForSignIn = true;
        try
        {
            AgentAccountCommandResult? result = null;
            var stopped = false;
            try
            {
                result = await _accountManager!.SignInAsync(providerId, wait.Token);
            }
            catch (OperationCanceledException) when (!_lifetime.IsCancellationRequested)
            {
                stopped = true; // The user stopped waiting; the CLI window (if any) stays open under their control.
            }
            catch (Exception) when (!_lifetime.IsCancellationRequested)
            {
                result = new AgentAccountCommandResult(AgentAccountCommandOutcome.StartFailed, null);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            IsWaitingForSignIn = false;
            ProgressText = Text.Resolve("agentTestingConnection");
            if (result?.Status is { } reported)
            {
                CliStatus = reported with { Install = AgentAccountInstallState.Installed, Version = CliStatus?.Version ?? reported.Version };
            }
            else
            {
                await CheckCliAsync(providerId);
            }

            await RefreshAccountProviderAsync(providerId);
            if (_lifetime.IsCancellationRequested)
            {
                return;
            }

            Reload(providerId);
            ReportCommand(stopped ? AgentAccountCommandOutcome.StillRunning : result!.Outcome, profile, signIn: true);
        }
        finally
        {
            _signInWait = null;
            IsWaitingForSignIn = false;
            End();
        }
    }

    private bool CanStopWaiting() => IsWaitingForSignIn;

    [RelayCommand(CanExecute = nameof(CanStopWaiting))]
    private void StopWaiting() => _signInWait?.Cancel();

    private bool CanSignOut() => !IsBusy && IsCliProvider && CliStatus is { Install: AgentAccountInstallState.Installed } status &&
        status.Auth == AgentAccountAuthState.Subscription;

    /// <summary>
    /// Global sign-out: it ends the CLI login for the whole OS user, including terminals outside the app, so it runs
    /// only after the explicit confirmation returned by <see cref="ConfirmSignOut"/>. Cancelling keeps the session.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSignOut))]
    private async Task SignOutAsync()
    {
        var providerId = SelectedProvider!.ProviderId;
        var profile = CliProfile!;
        var confirmation = ConfirmSignOut;
        var prompt = new AgentCliSignOutPrompt(
            Text.Format("agentCliSignOutTitle", profile.CliName),
            Text.Format(profile.SignOutMessageKey, profile.CliName, Branding.ProductName),
            Text.Format("agentCliSignOutConfirm", profile.CliName),
            Text.Resolve("cancel"));
        bool confirmed;
        try
        {
            confirmed = confirmation is not null && await confirmation(prompt);
        }
        catch (Exception)
        {
            confirmed = false;
        }

        if (!confirmed)
        {
            Report("agentCliSignOutCancelled", false);
            return;
        }

        Begin(Text.Format("agentCliSigningOut", profile.CliName));
        try
        {
            AgentAccountCommandResult result;
            try
            {
                result = await _accountManager!.SignOutAsync(providerId, userConfirmedGlobalSignOut: true, _lifetime.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception)
            {
                result = new AgentAccountCommandResult(AgentAccountCommandOutcome.StartFailed, null);
            }

            if (result.Status is { } reported)
            {
                CliStatus = reported with { Install = AgentAccountInstallState.Installed, Version = CliStatus?.Version ?? reported.Version };
            }
            else
            {
                await CheckCliAsync(providerId);
            }

            await RefreshAccountProviderAsync(providerId);
            if (_lifetime.IsCancellationRequested)
            {
                return;
            }

            Reload(providerId);
            ReportCommand(result.Outcome, profile, signIn: false);
        }
        finally
        {
            End();
        }
    }

    private void ReportCommand(AgentAccountCommandOutcome outcome, AgentCliProviderProfile profile, bool signIn)
    {
        switch (outcome)
        {
            case AgentAccountCommandOutcome.NoVisibleTerminal:
                ManualCommand = signIn ? profile.SignInCommand : null;
                StatusText = Text.Format("agentCliNoTerminal", profile.CliName);
                IsStatusError = true;
                return;
            case AgentAccountCommandOutcome.ExecutableUnavailable:
                StatusText = Text.Format("agentCliExecutableUnavailable", profile.CliName);
                IsStatusError = true;
                return;
            case AgentAccountCommandOutcome.StartFailed:
                StatusText = Text.Format("agentCliStartFailed", profile.CliName);
                IsStatusError = true;
                return;
            case AgentAccountCommandOutcome.CommandFailed:
                StatusText = Text.Format("agentCliCommandFailed", profile.CliName);
                IsStatusError = true;
                return;
            case AgentAccountCommandOutcome.StillRunning:
                StatusText = Text.Format("agentCliStillRunning", profile.CliName);
                IsStatusError = false;
                return;
        }

        if (signIn)
        {
            Report(IsCliAuthenticationConfigured ? CliProfile?.SignInCompletedMessageKey ?? (IsCliSignedIn ? "agentCliSignInDone" : "agentTestConnectionCliNativeAuth") : IsCliAuthBlocked ? "agentTestConnectionCliBlocked" : "agentCliSignInNotCompleted",
                !IsCliAuthenticationConfigured);
        }
        else
        {
            Report(CliStatus?.Auth == AgentAccountAuthState.SignedOut ? "agentCliSignOutDone" : "agentCliSignOutNotCompleted",
                CliStatus?.Auth != AgentAccountAuthState.SignedOut);
        }
    }

    /// <summary>Explicit CLI check; failures become a "check failed" state, never an exception.</summary>
    private async Task<bool> CheckCliAsync(string providerId)
    {
        try
        {
            CliStatus = await _accountManager!.CheckAsync(providerId, _lifetime.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception)
        {
            CliStatus = new AgentAccountStatus(AgentAccountInstallState.CheckFailed, null, AgentAccountAuthState.NotChecked);
            return false;
        }
    }

    private async Task<bool> RefreshProviderAsync(string providerId)
    {
        if (_catalog is null)
        {
            return true;
        }

        try
        {
            await _catalog.RefreshProviderAsync(providerId, _lifetime.Token);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Publishes account-command results through the same forced flight/cache used by startup and retry.</summary>
    private async Task RefreshAccountProviderAsync(string providerId)
    {
        if (_availability is null)
        {
            await RefreshProviderAsync(providerId);
            return;
        }

        var result = await _availability.RefreshAfterAccountCommandAsync(providerId);
        if (IsCliProvider)
        {
            CliStatus = _availability.CurrentAccountStatus(providerId) ?? CliStatus;
        }

        // Failure is represented by the shared availability state and the last known presentation. The explicit
        // sign-in/out outcome below still reports the account command's authoritative status.
        _ = result;
    }

    private void Begin(string progress)
    {
        StatusText = "";
        IsStatusError = false;
        ProgressText = progress;
        IsBusy = true;
    }

    private void End()
    {
        IsBusy = false;
        ProgressText = "";
    }

    private static bool IsEnvironmentBlocked(AgentAccountStatus? status) => status?.Auth == AgentAccountAuthState.BlockedEnvironment;

    private AgentCliProviderProfile? SafeDescribe(string providerId)
    {
        try
        {
            return _cliPresentation?.Describe(providerId);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private AgentCliReadScope? SafeReadScope(string providerId)
    {
        try
        {
            var candidate = _captureWorkspaceFolder?.Invoke();
            return _cliPresentation?.DescribeReadScope(providerId, string.IsNullOrWhiteSpace(candidate) ? null : candidate);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string Capitalize(string value) =>
        value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];

    private bool CanSaveApiKey() => !IsBusy && _credentials is not null && RequiresApiKey && ApiKey.Length > 0;

    [RelayCommand(CanExecute = nameof(CanSaveApiKey))]
    private async Task SaveApiKeyAsync()
    {
        var providerId = SelectedProvider!.ProviderId;
        var buffer = ApiKey.ToCharArray();
        ApiKey = "";
        IsBusy = true;
        try
        {
            try
            {
                var outcome = await _credentials!.SaveApiKeyAsync(providerId, buffer, CancellationToken.None);
                Report(outcome);
            }
            catch (Exception)
            {
                // Exception text could echo the secret or vault internals; only a fixed message is shown.
                Report(AgentCredentialSetupOutcome.Failed);
            }
            finally
            {
                Array.Clear(buffer);
            }

            await RefreshCatalogAsync();
        }
        finally
        {
            IsBusy = false;
            Reload(providerId);
        }
    }

    /// <summary>
    /// After an explicit save/removal the provider status is re-checked (vault presence only, no network), so the
    /// availability shown here and in the chat reflects the change. A failing check keeps the previous listing.
    /// </summary>
    private async Task RefreshCatalogAsync()
    {
        if (_catalog is null)
        {
            return;
        }

        try
        {
            await _catalog.RefreshAsync(CancellationToken.None);
        }
        catch (Exception)
        {
            // The outcome of the key operation was already reported; the listing simply stays as it was.
        }
    }

    private bool CanRemoveApiKey() => !IsBusy && _credentials is not null && RequiresApiKey;

    [RelayCommand(CanExecute = nameof(CanRemoveApiKey))]
    private async Task RemoveApiKeyAsync()
    {
        var providerId = SelectedProvider!.ProviderId;
        IsBusy = true;
        try
        {
            try
            {
                Report(await _credentials!.RemoveApiKeyAsync(providerId, CancellationToken.None));
            }
            catch (Exception)
            {
                Report(AgentCredentialSetupOutcome.Failed);
            }

            await RefreshCatalogAsync();
        }
        finally
        {
            IsBusy = false;
            Reload(providerId);
        }
    }

    private void Report(string key, bool error)
    {
        StatusText = Text.Resolve(key);
        IsStatusError = error;
    }

    private void Report(AgentCredentialSetupOutcome outcome)
    {
        var (key, error) = outcome switch
        {
            AgentCredentialSetupOutcome.Saved => ("agentKeySaved", false),
            AgentCredentialSetupOutcome.Removed => ("agentKeyRemoved", false),
            AgentCredentialSetupOutcome.VaultUnavailable => ("agentKeyVaultUnavailable", true),
            AgentCredentialSetupOutcome.Cancelled => ("agentKeyCancelled", false),
            AgentCredentialSetupOutcome.Rejected => ("agentKeyRejected", true),
            AgentCredentialSetupOutcome.UnknownProvider => ("agentKeySetupUnavailable", true),
            _ => ("agentKeyFailed", true),
        };
        StatusText = Text.Resolve(key);
        IsStatusError = error;
    }

    /// <summary>Closing the window ends any wait (the CLI window itself stays with the user) and pending checks.</summary>
    public void Dispose()
    {
        if (_lifetime.IsCancellationRequested)
        {
            return;
        }

        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
