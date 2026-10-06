using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Desktop.Agents;

namespace EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

/// <summary>Why the composer is replaced by a call to action (nothing can be sent).</summary>
public enum AgentSendBlock
{
    None,

    /// <summary>The persisted permissions are being read.</summary>
    Loading,

    /// <summary>No persisted consent for the external destination (<see cref="AgentTurnBlockReason.ConsentMissing"/>).</summary>
    ConsentMissing,

    /// <summary>Malformed permissions (<see cref="AgentTurnBlockReason.InvalidPermissions"/>).</summary>
    InvalidPermissions,

    /// <summary>The stored permissions are unreadable or from a newer version; they are never replaced silently.</summary>
    PermissionsUnreadable,

    /// <summary>No permissions store composed: consent cannot exist.</summary>
    PermissionsUnavailable,
}

public sealed partial class AgentChatViewModel
{
    private readonly Dictionary<string, PermissionsSlot> _permissions = new(StringComparer.Ordinal);

    /// <summary>Last known permissions of one provider (never replaced by defaults after a read failure).</summary>
    private sealed class PermissionsSlot
    {
        public AgentProviderPermissions? Value { get; set; }

        public AgentPersistenceStatus? Failure { get; set; }

        public string? ErrorCode { get; set; }

        public Task? Loading { get; set; }
    }

    /// <summary>Permissions of the selected provider, when readable (NotFound = defaults, which carry no consent).</summary>
    public AgentProviderPermissions? CurrentPermissions =>
        SelectedProvider is { } provider && _permissions.TryGetValue(provider.ProviderId, out var slot) ? slot.Value : null;

    private PermissionsSlot? CurrentSlot =>
        SelectedProvider is { } provider && _permissions.TryGetValue(provider.ProviderId, out var slot) ? slot : null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSendBlocked), nameof(IsComposerVisible), nameof(SendBlockText), nameof(SendBlockActionText),
        nameof(HasSendBlockAction), nameof(IsComposerEnabled))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private AgentSendBlock _sendBlock;

    /// <summary>The composer is replaced by the call-to-action card (no partial send).</summary>
    public bool IsSendBlocked => SendBlock is not (AgentSendBlock.None or AgentSendBlock.Loading);

    public bool IsComposerVisible => !IsSendBlocked;

    public bool IsComposerEnabled => IsFeatureAvailable && SendBlock == AgentSendBlock.None;

    public string SendBlockText => SendBlock switch
    {
        AgentSendBlock.ConsentMissing => Text.Format("agentBlockConsent", SelectedProvider?.Presentation.DisplayName ?? ""),
        AgentSendBlock.InvalidPermissions => Text.Resolve("agentBlockInvalid"),
        AgentSendBlock.PermissionsUnreadable => Text.Format("agentBlockUnreadable", CurrentSlot?.ErrorCode ?? "DocumentUnreadable"),
        AgentSendBlock.PermissionsUnavailable => Text.Resolve("agentBlockUnavailable"),
        AgentSendBlock.Loading => Text.Resolve("agentBlockLoading"),
        _ => "",
    };

    public string SendBlockActionText => Text.Resolve(SendBlock == AgentSendBlock.ConsentMissing
        ? "agentBlockConfigure"
        : "agentBlockOpenPermissions");

    public bool HasSendBlockAction => SendBlock is AgentSendBlock.ConsentMissing or AgentSendBlock.InvalidPermissions or
        AgentSendBlock.PermissionsUnreadable;

    /// <summary>Opens the Permissions window at the section that resolves the block.</summary>
    [RelayCommand]
    private void ResolveSendBlock() => PermissionsRequested?.Invoke(this,
        SendBlock == AgentSendBlock.ConsentMissing ? AgentPermissionsViewModel.SectionDataSending : null);

    /// <summary>Link "Permissões" of the summary line.</summary>
    [RelayCommand]
    private void OpenPermissions() => PermissionsRequested?.Invoke(this, null);

    /// <summary>Platform facts for the policy, captured on the UI thread.</summary>
    /// <remarks>
    /// Product tools can arrive through the shared MCP channel or through a provider's separately declared custom-tool
    /// channel. Do not infer the latter from MCP availability; the catalog capability is the provider-neutral contract.
    /// </remarks>
    private bool ProductToolsAvailable => _services.McpChannels?.ProductToolsAvailable == true ||
        SelectedProvider?.Presentation.SupportsToolCalling == true;

    private AgentPlatformFacts CapturePlatformFacts(string? folder) => new(
        AgentWorkspacePaths.TryGetWorkspaceRoot(folder, out _, _services.PathProbe),
        ProductToolsAvailable,
        SelectedProvider?.Presentation.SupportsNativeTools == true);

    /// <summary>
    /// Recomputes the send block from the persisted permissions and <see cref="AgentModePolicy"/> (the only place that
    /// decides; the UI only maps its <see cref="AgentTurnBlockReason"/> to a call to action).
    /// </summary>
    private void RefreshSendBlock()
    {
        OnPropertyChanged(nameof(PermissionsSummary));
        OnPropertyChanged(nameof(HasPermissionsSummary));
        OnPropertyChanged(nameof(ActivePersistenceText));
        OnPropertyChanged(nameof(HasActivePersistenceText));
        OnPropertyChanged(nameof(ActivePersistenceIsError));
        OnPropertyChanged(nameof(ShowStatusArea));
        OnPropertyChanged(nameof(CanAttachExternal));
        OnPropertyChanged(nameof(IsHistoryDisabled));
        OnPropertyChanged(nameof(ActivePersistenceText));
        OnPropertyChanged(nameof(HasActivePersistenceText));
        OnPropertyChanged(nameof(ActivePersistenceIsError));
        AttachExternalFileCommand.NotifyCanExecuteChanged();
        if (SelectedProvider is not { } provider || !IsFeatureAvailable)
        {
            SendBlock = AgentSendBlock.None;
            return;
        }

        var turnPlan = provider.Presentation.SupportsTurnPlan;
        if (!turnPlan && !provider.IsExternal)
        {
            // A local provider without plan support receives only the message: no external destination to consent to.
            SendBlock = AgentSendBlock.None;
            return;
        }

        if (_services.Permissions is null)
        {
            SendBlock = AgentSendBlock.PermissionsUnavailable;
            return;
        }

        if (!_permissions.TryGetValue(provider.ProviderId, out var slot) || (slot.Value is null && slot.Failure is null))
        {
            SendBlock = AgentSendBlock.Loading;
            return;
        }

        if (slot.Value is not { } permissions)
        {
            SendBlock = AgentSendBlock.PermissionsUnreadable;
            return;
        }

        if (turnPlan)
        {
            var plan = AgentModePolicy.Plan(SelectedMode.Mode, permissions,
                CapturePlatformFacts(SessionFolderCandidate()), requireExternalDestinationConsent: provider.IsExternal);
            SendBlock = plan.BlockReason switch
            {
                AgentTurnBlockReason.ConsentMissing => AgentSendBlock.ConsentMissing,
                AgentTurnBlockReason.InvalidPermissions => AgentSendBlock.InvalidPermissions,
                _ => AgentSendBlock.None,
            };
            return;
        }

        SendBlock = !permissions.IsWellFormed ? AgentSendBlock.InvalidPermissions
            : !permissions.HasExternalDestinationConsent ? AgentSendBlock.ConsentMissing
            : AgentSendBlock.None;
    }

    /// <summary>Loads the permissions of a provider once (subsequent calls reuse the slot unless forced).</summary>
    private Task EnsurePermissionsAsync(string providerId, bool force = false)
    {
        if (string.IsNullOrWhiteSpace(providerId) || _services.Permissions is null)
        {
            RefreshSendBlock();
            return Task.CompletedTask;
        }

        if (!force && _permissions.TryGetValue(providerId, out var existing))
        {
            return existing.Loading ?? Task.CompletedTask;
        }

        var slot = new PermissionsSlot();
        _permissions[providerId] = slot;
        slot.Loading = LoadPermissionsAsync(providerId, slot);
        RefreshSendBlock();
        return slot.Loading;
    }

    private async Task LoadPermissionsAsync(string providerId, PermissionsSlot slot)
    {
        AgentPersistenceResult<AgentProviderPermissions> result;
        try
        {
            result = await _services.Permissions!.LoadAsync(providerId, _lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return;
        }
        catch (Exception)
        {
            result = AgentPersistenceResult.Failure<AgentProviderPermissions>(AgentPersistenceStatus.Failed, "StoreFailed");
        }

        if (!ReferenceEquals(_permissions.GetValueOrDefault(providerId), slot))
        {
            return; // A newer load (e.g. after saving) replaced this slot.
        }

        switch (result.Status)
        {
            case AgentPersistenceStatus.Succeeded:
                slot.Value = result.Value;
                break;
            case AgentPersistenceStatus.NotFound:
                slot.Value = AgentProviderPermissions.Default(providerId);
                break;
            default:
                // Unreadable, newer format or store failure: visible, and never replaced by permissive defaults.
                slot.Failure = result.Status;
                slot.ErrorCode = SafeCodeOrNull(result.ErrorCode) ?? result.Status.ToString();
                break;
        }

        slot.Loading = null;
        OnPermissionsChanged(providerId);
    }

    /// <summary>The Permissions window saved (or erased history for) a provider: adopt the stored copy.</summary>
    public void OnPermissionsSaved(AgentProviderPermissions stored)
    {
        ArgumentNullException.ThrowIfNull(stored);
        _permissions[stored.ProviderId] = new PermissionsSlot { Value = stored };
        OnPermissionsChanged(stored.ProviderId);
    }

    private void OnPermissionsChanged(string providerId)
    {
        if (SelectedProvider?.ProviderId == providerId)
        {
            _readScopeCache = null;
            if (!_copilotModelChoiceRequired && SelectedModel is null && CurrentPermissions?.DefaultModel is { } model && Models.Contains(model))
            {
                SelectedModel = model;
            }

            RefreshSendBlock();
            RefreshAutomaticChips();
            RefreshReadScope();
        }

        // Conversations waiting for a decision about KeepHistory are saved now (or get the opt-out notice).
        foreach (var conversation in _conversations.Values.Where(c => c.ProviderId == providerId && !c.IsEmpty))
        {
            _ = SaveConversationAsync(conversation);
        }
    }

    public AgentPermissionsViewModel CreatePermissionsViewModel(string? providerId = null, string? section = null)
    {
        var provider = Providers.FirstOrDefault(candidate => candidate.ProviderId == providerId) ?? SelectedProvider;
        return new AgentPermissionsViewModel(
            _services.Permissions, _services.Conversations, provider?.ProviderId ?? "",
            provider?.Presentation.DisplayName ?? "", _host.WorkspaceFolder, _host.ListConnections(),
            ProductToolsAvailable, _services.Clock, section)
        {
            Saved = OnPermissionsSaved,
            EraseHistory = EraseProviderHistoryAsync,
        };
    }

    // ---- Summary line ("Externo · leitura MongoDB · arquivos do workspace"). ----

    public string PermissionsSummary
    {
        get
        {
            if (SelectedProvider is not { } provider)
            {
                return "";
            }

            var parts = new List<string> { provider.DestinationText };
            var slot = CurrentSlot;
            if (slot?.Value is not { } permissions)
            {
                parts.Add(Text.Resolve(slot?.Failure is not null ? "agentSummaryUnreadable" : "agentSummaryLoading"));
                return string.Join(" · ", parts);
            }

            if (!provider.Presentation.SupportsTurnPlan)
            {
                parts.Add(Text.Resolve("agentSummaryMessageOnly"));
            }
            else
            {
                var productTools = ProductToolsAvailable;
                if (productTools && (permissions.EnabledReadTools ?? []).Any(static tool =>
                        AgentProductToolNames.CategoryOf(tool) == AgentConfirmationCategories.MongoMetadataRead))
                {
                    parts.Add(Text.Resolve("agentSummaryMongoRead"));
                }

                if (permissions.NativeFileRead && permissions.DataSending.WorkspaceFiles && permissions.Workspace.UseFilesFolder)
                {
                    parts.Add(Text.Resolve("agentSummaryWorkspaceFiles"));
                }

                if (permissions.DataSending.ExternalAttachments)
                {
                    parts.Add(Text.Resolve("agentSummaryExternalFiles"));
                }

                if (!productTools)
                {
                    parts.Add(Text.Format("agentSummaryToolsUnavailable", Branding.ProductName));
                }
            }

            if (!permissions.KeepHistory)
            {
                parts.Add(Text.Resolve("agentSummaryNoHistory"));
            }

            return string.Join(" · ", parts);
        }
    }

    public bool HasPermissionsSummary => PermissionsSummary.Length > 0;

    // ---- Automatic availability (P7-CL7-06). ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AvailabilityText), nameof(IsAvailabilityFailure), nameof(IsCheckingAvailability),
        nameof(ShowAvailabilityRetry), nameof(AvailabilityActionText), nameof(ShowRefreshProviders))]
    [NotifyCanExecuteChangedFor(nameof(RetryAvailabilityCommand))]
    private AgentProviderAvailability? _availability;

    public bool IsCheckingAvailability => Availability?.State == AgentProviderAvailabilityState.Checking;

    public bool IsAvailabilityFailure => Availability?.IsFailure == true;

    /// <summary>"Tentar novamente" on failures; "Verificar disponibilidade" when a vault-reading provider was not checked.</summary>
    public bool ShowAvailabilityRetry => _services.Availability is not null && SelectedProvider is { } provider &&
        !IsCheckingAvailability && (IsAvailabilityFailure || (Availability is null && !provider.Presentation.IsAvailable));

    public string AvailabilityActionText => Text.Resolve(IsAvailabilityFailure ? "agentAvailabilityRetry" : "agentRefreshProviders");

    public string AvailabilityText
    {
        get
        {
            if (SelectedProvider is not { } provider)
            {
                return "";
            }

                var cli = _services.CliAccountPresentation?.Describe(provider.ProviderId)?.CliName ?? provider.Presentation.DisplayName;
            return Availability?.State switch
            {
                AgentProviderAvailabilityState.Checking => Text.Resolve("agentAvailabilityChecking"),
                AgentProviderAvailabilityState.NotChecked => Text.Resolve("agentAvailabilityExplicitCheckRequired"),
                AgentProviderAvailabilityState.Available => Text.Resolve("agentAvailabilityAvailable"),
                AgentProviderAvailabilityState.NotConnected => Text.Resolve("agentAvailabilityNotConnected"),
                AgentProviderAvailabilityState.CliMissing => Text.Format("agentAvailabilityCliMissing", cli),
                AgentProviderAvailabilityState.Failed when Availability.TimedOut => Text.Resolve("agentAvailabilityTimedOut"),
                AgentProviderAvailabilityState.Failed => Text.Resolve("agentAvailabilityFailed"),
                _ => provider.Presentation.IsAvailable ? Text.Resolve("agentAvailabilityAvailable") : provider.AvailabilityText,
            };
        }
    }

    /// <summary>
    /// Background check of the selected provider (panel opened, provider switched). Automatic checks run only for
    /// providers whose check reads no vault slot (no API key): an OS vault prompt never appears unrequested.
    /// </summary>
    public void StartAutomaticAvailabilityCheck()
    {
        if (_services.Availability is not { } service || SelectedProvider is not { } provider || _disposed)
        {
            return;
        }

        if (service.Current(provider.ProviderId) is { } known)
        {
            Availability = known;
        }
        else
        {
            Availability = null;
        }

        if (provider.RequiresApiKey && !provider.UsesOfficialCli)
        {
            return;
        }

        _ = ObserveAvailabilityAsync(service.CheckAsync(provider.ProviderId));
    }

    private bool CanRetryAvailability() => _services.Availability is not null && SelectedProvider is not null && !IsCheckingAvailability;

    /// <summary>"Tentar novamente": an explicit, forced check of the selected provider (may read its vault slot).</summary>
    [RelayCommand(CanExecute = nameof(CanRetryAvailability))]
    private Task RetryAvailabilityAsync() =>
        SelectedProvider is { } provider && _services.Availability is { } service
            ? ObserveAvailabilityAsync(service.CheckAsync(provider.ProviderId, force: true))
            : Task.CompletedTask;

    private static async Task ObserveAvailabilityAsync(Task<AgentProviderAvailability> check)
    {
        try
        {
            await check;
        }
        catch (Exception)
        {
            // The service never throws for check failures; a defect leaves the previous state.
        }
    }

    private void OnAvailabilityChanged(object? sender, AgentProviderAvailability availability) => AgentUiDispatch.Post(() =>
    {
        if (_disposed || SelectedProvider?.ProviderId != availability.ProviderId)
        {
            return;
        }

        Availability = availability;
        if (availability.State != AgentProviderAvailabilityState.Checking)
        {
            // The refreshed presentation (availability, auth, models, capabilities) comes from the catalog snapshot.
            ReloadProvidersIfChanged();
            // A timeout/policy result can leave the catalog object unchanged; resolve any pending restored model
            // against the terminal check state instead of retaining an unknown model indefinitely.
            LoadModels(SelectedModel);
            UpdateIdleState();
        }
    });
}
