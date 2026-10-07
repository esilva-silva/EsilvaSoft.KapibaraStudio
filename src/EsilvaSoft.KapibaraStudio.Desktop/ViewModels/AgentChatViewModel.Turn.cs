using CommunityToolkit.Mvvm.Input;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Desktop.Agents;

namespace EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

public sealed partial class AgentChatViewModel
{
    /// <summary>State of one turn. Each turn owns its CTS; nothing is shared with other tabs or turns.</summary>
    internal sealed class TurnRun(AgentTurnId turnId, CancellationTokenSource cancellation)
    {
        // Guards the CTS so a concurrent cancel request and the turn's own disposal never race: once disposed, a
        // cancel request is a silent no-op instead of an ObjectDisposedException.
        private readonly object _cancelGate = new();
        private Task? _cancelTask;
        private bool _disposed;

        public AgentTurnId TurnId { get; } = turnId;
        public required AgentChatConversation Conversation { get; init; }
        public Task? Completion { get; set; }

        public CancellationTokenSource Cancellation { get; } = cancellation;

        public AgentSessionId? SessionId { get; set; }

        public string? ErrorCode { get; set; }

        /// <summary>Provider of the reviewed package that started this turn (fixed for the turn).</summary>
        public string ProviderId { get; init; } = "";

        public bool PersistProviderSession { get; init; } = true;

        public Dictionary<AgentMessageId, AgentChatMessageItem> Messages { get; } = [];

        public Dictionary<AgentToolCallId, AgentToolCallItem> Tools { get; } = [];

        public Dictionary<AgentApprovalId, AgentApprovalCardItem> Approvals { get; } = [];

        /// <summary>Requests cancellation of this turn's token. Idempotent and safe after the turn already finished
        /// and disposed its CTS: cancelling a finished turn does nothing, it never throws.</summary>
        public Task RequestCancellationAsync()
        {
            lock (_cancelGate)
            {
                return _disposed ? Task.CompletedTask : _cancelTask ??= Cancellation.CancelAsync();
            }
        }

        /// <summary>Disposes the CTS once the turn is fully finished. Mutually exclusive with
        /// <see cref="RequestCancellationAsync"/> under the same gate, so a cancel arriving exactly as the turn ends
        /// never observes a disposed token source.</summary>
        public void DisposeCancellation()
        {
            lock (_cancelGate)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                Cancellation.Dispose();
            }
        }
    }

    /// <summary>Completion of the running turn, for hosts and tests; null when idle.</summary>
    private bool CanSend() => !string.IsNullOrWhiteSpace(ComposerText) && IsIdle && IsProviderUsable && HasEligibleCopilotModel &&
        SelectedProvider is { } provider &&
        (CurrentPermissions is { IsWellFormed: true } || !provider.IsExternal) && SendBlock == AgentSendBlock.None;

    /// <summary>Direct send. Message, provider, mode, permissions, chips and active tab are snapshotted before awaiting.</summary>
    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        if (!CanSend() || SelectedProvider is not { } provider)
            return;
        var permissions = CurrentPermissions ?? (provider.IsExternal ? null : AgentProviderPermissions.Default(provider.ProviderId));
        if (permissions is not { IsWellFormed: true }) return;
        // Records retain collection references. Freeze them before attachment reads await so the request and
        // its tool plan use the same permissions, even when the original settings lists change during preparation.
        permissions = permissions with
        {
            Workspace = permissions.Workspace with { Exclusions = Array.AsReadOnly(permissions.Workspace.EffectiveExclusions.ToArray()) },
            EnabledReadTools = permissions.EnabledReadTools is { } tools ? Array.AsReadOnly(tools.ToArray()) : null!,
            SelectedConnectionIds = permissions.SelectedConnectionIds is { } ids ? Array.AsReadOnly(ids.ToArray()) : null!,
        };
        var message = ComposerText.Trim();
        var mode = SelectedMode.Mode;
        var modelId = SelectedModel;
        var conversation = ActiveConversation;
        var context = _host.CaptureWorkspace();
        if (!permissions.Workspace.UseFilesFolder)
            context = context with { WorkspaceFolder = null };
        var capturedChips = Chips.ToArray();
        var requests = capturedChips.Select(static chip => chip.ToRequest()).ToArray();
        var workingDirectory = context.WorkspaceFolder;
        var turnId = AgentTurnId.New();
        var facts = CapturePlatformFacts(workingDirectory);
        var plan = AgentModePolicy.Plan(mode, permissions, facts, requireExternalDestinationConsent: provider.IsExternal);
        if (plan.IsBlocked)
        {
            RefreshSendBlock();
            return;
        }

        var run = new TurnRun(turnId, CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token))
        {
            ProviderId = provider.ProviderId,
            PersistProviderSession = permissions.KeepHistory,
            Conversation = conversation,
        };
        conversation.Turn = run;
        conversation.TurnState = AgentChatState.Connecting;
        conversation.TurnDetail = null;
        conversation.ProviderId = provider.ProviderId;
        conversation.ModelId = modelId;
        conversation.Mode = mode;
        conversation.UsageTurns[turnId] = new AgentUsageAccumulator();
        ComposerText = "";
        StatusDetail = null;
        State = AgentChatState.Connecting;
        ComposerFocusRequested?.Invoke(this, EventArgs.Empty);
        run.Completion = RunPreparedTurnAsync(run, provider.ProviderId, modelId, workingDirectory,
            message, mode, permissions, plan, context, requests, capturedChips);
        await run.Completion;
    }

    private async Task RunPreparedTurnAsync(TurnRun run, string providerId, string? modelId, string? workingDirectory,
        string message, AgentOperationMode mode, AgentProviderPermissions permissions, AgentTurnPlan plan,
        AgentWorkspaceContext context, IReadOnlyList<AgentAttachmentRequest> requests,
        AgentContextChipViewModel[] capturedChips)
    {
        try
        {
            var resolution = await AgentAttachmentResolver.ResolveAsync(requests, context, permissions, message, run.Cancellation.Token, _services.FileReader, _services.PathProbe);
            if (!resolution.Succeeded)
            {
                // A background preparation owns its captured chips, never the current composer's indexes.
                if (ReferenceEquals(ActiveConversation, run.Conversation))
                    foreach (var failure in resolution.Failures)
                        if (failure.RequestIndex < capturedChips.Length && Chips.Contains(capturedChips[failure.RequestIndex]))
                            capturedChips[failure.RequestIndex].Error = failure.Error;
                run.ErrorCode = "AttachmentsRefused";
                Finish(run, AgentTurnOutcome.Failed);
                return;
            }
            foreach (var chip in capturedChips.Where(static chip => !chip.IsAutomatic)) Chips.Remove(chip);
            OnPropertyChanged(nameof(HasChips));
            OnPropertyChanged(nameof(ContextSummary));
            var systemPrompt = AgentSystemPromptBuilder.Build(new AgentSystemPromptContext(plan,
                context.WorkspaceFolder, permissions.DataSending.ActiveFile ? context.ActiveFileName : null));
            var request = new AgentTurnRequest(run.TurnId, message, context.TabId ?? "", context.DocumentVersion ?? 0)
            {
                Plan = plan, SystemPrompt = systemPrompt, Attachments = resolution.Attachments,
                ConversationId = run.Conversation.Id, Permissions = permissions, WorkspaceContext = context,
            };
            run.Conversation.Items.Add(new AgentChatMessageItem(AgentChatRole.User, message,
                attachments: resolution.Attachments.Select(static attachment => attachment.ToDescriptor()).ToArray())
            {
                ContextMeasurement = AgentContextMeasurement.Capture(request),
            });
            var newCopilotReservation = false;
            if (permissions.KeepHistory &&
                string.Equals(providerId, AgentProviderIds.GitHubCopilotSubscription, StringComparison.Ordinal))
            {
                newCopilotReservation = run.Conversation.ProviderSessionId is null;
                if (!await ReserveCopilotSessionAsync(run.Conversation, run.Cancellation.Token))
                {
                    run.ErrorCode = "CopilotHistoryReservationFailed";
                    Finish(run, AgentTurnOutcome.Failed);
                    return;
                }
            }

            await RunTurnAsync(run, providerId, modelId, workingDirectory, request, newCopilotReservation);
        }
        catch (OperationCanceledException) when (run.Cancellation.IsCancellationRequested)
        {
            if (run.Conversation.Turn == run) Finish(run, AgentTurnOutcome.Cancelled);
        }
        catch (Exception)
        {
            run.ErrorCode = "ContextPreparationFailed";
            Finish(run, AgentTurnOutcome.Failed);
        }
        finally
        {
            if (run.Conversation.Turn == run) run.Conversation.Turn = null;
            run.DisposeCancellation();
            if (ReferenceEquals(ActiveConversation, run.Conversation))
            {
                UpdateIdleState();
                NotifyCommands();
            }
            _ = SaveConversationAsync(run.Conversation);
        }
    }

    private async Task RunTurnAsync(TurnRun run, string providerId, string? modelId, string? workingDirectory,
        AgentTurnRequest request, bool newCopilotReservation)
    {
        var runtime = _services.Runtime!;
        var terminalEventReceived = false;
        try
        {
            var sessionId = await EnsureSessionAsync(runtime, run.Conversation, providerId, modelId, workingDirectory,
                run.PersistProviderSession, newCopilotReservation, run.Cancellation.Token);
            run.SessionId = sessionId;
            run.Conversation.SessionId = sessionId;
            if (!ReferenceEquals(run.Conversation.Turn, run))
            {
                return;
            }

            RefreshRunningState(run);
            await foreach (var item in runtime.RunTurnAsync(sessionId, request, run.Cancellation.Token))
            {
                // Events of another session/turn, or arriving after this turn stopped being current, are discarded.
                if (!ReferenceEquals(run.Conversation.Turn, run) || item.SessionId != sessionId || item.TurnId != run.TurnId)
                {
                    continue;
                }

                Apply(run, item);
                terminalEventReceived |= item.Kind == AgentEventKind.TaskCompleted;
            }
        }
        catch (OperationCanceledException) when (run.Cancellation.IsCancellationRequested)
        {
            if (ReferenceEquals(run.Conversation.Turn, run) && run.Conversation.IsBusy)
            {
                Finish(run, AgentTurnOutcome.Cancelled);
            }
        }
        catch (AgentRuntimeException exception)
        {
            if (exception.Code == "SessionBusy" || exception.Code == "UnknownSession")
            {
                ForgetSession(run.Conversation);
            }

            run.ErrorCode = SafeCode(exception.Code);
            if (ReferenceEquals(run.Conversation.Turn, run))
            {
                Finish(run, AgentTurnOutcome.Failed);
            }
        }
        catch (Exception)
        {
            run.ErrorCode = "RuntimeFailure";
            if (ReferenceEquals(run.Conversation.Turn, run))
            {
                Finish(run, AgentTurnOutcome.Failed);
            }
        }
        finally
        {
            if (ReferenceEquals(run.Conversation.Turn, run))
            {
                if (run.Conversation.IsBusy && !terminalEventReceived)
                {
                    // The stream ended without a terminal event: never report success.
                    Finish(run, AgentTurnOutcome.OutcomeUnknown);
                }

                run.Conversation.Turn = null;
            }

            run.DisposeCancellation();
            if (ReferenceEquals(ActiveConversation, run.Conversation)) OnStateChanged(State);
        }
    }

    private async Task<AgentSessionId> EnsureSessionAsync(
        IAgentRuntime runtime, AgentChatConversation conversation, string providerId, string? modelId, string? workingDirectory,
        bool persistProviderSession, bool newCopilotReservation, CancellationToken cancellationToken)
    {
        if (conversation.SessionId is { } existing && conversation.SessionProviderId == providerId &&
            conversation.SessionModelId == modelId &&
            (!string.Equals(providerId, AgentProviderIds.GitHubCopilotSubscription, StringComparison.Ordinal) ||
                conversation.SessionPersistsProviderState == persistProviderSession) &&
            !(workingDirectory is null && conversation.SessionWorkingDirectory is not null))
        {
            return existing;
        }

        var providerLabel = Providers.FirstOrDefault(option => option.ProviderId == providerId)?.Presentation.DisplayName ?? providerId;
        await CloseSessionAsync(conversation);
        var created = await runtime.StartSessionAsync(new AgentSessionOptions(providerId, modelId, workingDirectory)
        {
            ResumeProviderSessionId = newCopilotReservation ||
                (!persistProviderSession && string.Equals(providerId, AgentProviderIds.GitHubCopilotSubscription,
                    StringComparison.Ordinal)) ? null : conversation.ProviderSessionId,
            ReservedProviderSessionId = persistProviderSession && string.Equals(providerId, AgentProviderIds.GitHubCopilotSubscription,
                StringComparison.Ordinal) ? conversation.ProviderSessionId : null,
            ConversationId = conversation.Id,
            Mode = conversation.Mode,
            PersistProviderSession = persistProviderSession,
            ProviderSessionObserver = update => AgentUiDispatch.Post(() =>
            {
                if (update.ProviderSessionId is { } id)
                {
                    if (!persistProviderSession && string.Equals(providerId, AgentProviderIds.GitHubCopilotSubscription,
                        StringComparison.Ordinal))
                        ReportVolatileProviderSessionId(conversation, id);
                    else
                        ReportProviderSessionId(conversation, id);
                }
                if (update.Change == AgentProviderSessionChange.ResumeFallback)
                {
                    conversation.ResumeLost = true;
                    if (!string.Equals(providerId, AgentProviderIds.GitHubCopilotSubscription, StringComparison.Ordinal))
                        conversation.ProviderSessionId = null;
                    conversation.Items.Add(new AgentChatNoticeItem(
                        Text.Format("agentResumeLostForProvider", providerLabel),
                        isWarning: true));
                }
            }),
        }, cancellationToken);
        conversation.SessionId = created;
        conversation.SessionWorkingDirectory = workingDirectory;
        conversation.HasSessionWorkingDirectory = true;
        RefreshReadScope();
        conversation.SessionProviderId = providerId;
        conversation.SessionModelId = modelId;
        conversation.SessionPersistsProviderState = persistProviderSession;
        return created;
    }

    private void Apply(TurnRun run, AgentEvent item)
    {
        switch (item.Kind)
        {
            case AgentEventKind.UsageUpdated when item.Usage is { } usage:
                if (run.Conversation.UsageTurns.TryGetValue(run.TurnId, out var accumulator))
                    accumulator.Observe(usage, item.TimestampUtc);
                if (ReferenceEquals(ActiveConversation, run.Conversation)) OnPropertyChanged(nameof(UsageDetails));
                break;
            case AgentEventKind.TaskStarted:
            case AgentEventKind.TaskProgress:
                RefreshRunningState(run);
                break;
            case AgentEventKind.MessageStarted when item.MessageId is { } id:
                GetOrAddMessage(run, id);
                break;
            case AgentEventKind.MessageDelta when item.MessageId is { } id && !string.IsNullOrEmpty(item.Text):
                // Streaming text is data only: appended, never interpreted as a command or an approval.
                GetOrAddMessage(run, id).Append(item.Text);
                break;
            case AgentEventKind.MessageCompleted when item.MessageId is { } id:
                if (run.Messages.TryGetValue(id, out var message))
                {
                    message.IsStreaming = false;
                }

                break;
            case AgentEventKind.ToolRequested when item.ToolCallId is { } callId:
                if (!run.Tools.ContainsKey(callId))
                {
                    var tool = new AgentToolCallItem(callId, item.ToolName, item.ToolDestination, item.ToolOrigin,
                        item.ToolOrigin == AgentToolOrigin.ProviderObserved ? ObservedToolExecutor(run.ProviderId) : null);
                    run.Tools.Add(callId, tool);
                    run.Conversation.Items.Add(tool);
                }

                RefreshRunningState(run);
                break;
            case AgentEventKind.ToolStarted when item.ToolCallId is { } callId:
                if (run.Tools.TryGetValue(callId, out var started) && !started.IsTerminal)
                {
                    started.MarkStarted(_services.Clock.GetTimestamp());
                }

                break;
            case AgentEventKind.ToolCompleted or AgentEventKind.ToolFailed when item.ToolCallId is { } callId:
                if (run.Tools.TryGetValue(callId, out var finished) && !finished.IsTerminal)
                {
                    finished.Complete(MapToolState(item), SafeCodeOrNull(item.ErrorCode), _services.Clock);
                    if (finished.CanReviewPermissions)
                    {
                        PermissionsRequested?.Invoke(this, null);
                    }
                }

                RefreshRunningState(run);
                break;
            case AgentEventKind.ApprovalRequested when item.ApprovalId is { } approvalId:
                RequestApproval(run, approvalId, item.ApprovalExpiresAtUtc);
                break;
            case AgentEventKind.ApprovalGranted or AgentEventKind.ApprovalDenied when item.ApprovalId is { } approvalId:
                if (run.Approvals.TryGetValue(approvalId, out var card))
                {
                    card.Approval.CloseByRuntime(item.Kind == AgentEventKind.ApprovalGranted
                        ? AgentApprovalOutcome.Granted : AgentApprovalOutcome.Denied, SafeCodeOrNull(item.ErrorCode));
                }

                RefreshRunningState(run);
                break;
            case AgentEventKind.AgentError:
                run.ErrorCode = SafeCode(item.ErrorCode);
                if (IsAuthenticationFailure(run.ErrorCode) && _services.Availability is { } availability)
                {
                    // Revalidate the account after the failed turn so the panel can offer the official sign-in action.
                    // The failed turn is never replayed and no tools are repeated.
                    _ = ObserveAvailabilityAsync(availability.CheckAsync(run.ProviderId, force: true));
                }
                break;
            case AgentEventKind.TaskCompleted:
                Finish(run, item.Outcome ?? AgentTurnOutcome.OutcomeUnknown);
                break;
            case AgentEventKind.SessionCompleted:
                ForgetSession(run.Conversation);
                break;
        }
        if (ReferenceEquals(ActiveConversation, run.Conversation)) OnPropertyChanged(nameof(ShowStatusLine));
    }

    private static bool IsAuthenticationFailure(string? code) => code is
        "CopilotNotLoggedIn" or "CopilotNonSubscriptionAuth" or "ClaudeCodeNotLoggedIn" or
        "ClaudeCodeNonSubscriptionAuth" or "ClaudeCodeAuthenticationFailed";

    private static AgentChatMessageItem GetOrAddMessage(TurnRun run, AgentMessageId id)
    {
        if (!run.Messages.TryGetValue(id, out var message))
        {
            message = new AgentChatMessageItem(AgentChatRole.Agent, "", id);
            run.Messages.Add(id, message);
            run.Conversation.Items.Add(message);
        }

        return message;
    }

    private static AgentToolCallState MapToolState(AgentEvent item) => item.Kind == AgentEventKind.ToolCompleted
        ? AgentToolCallState.Succeeded
        : item.ToolStatus switch
        {
            AgentToolResultStatus.Denied => AgentToolCallState.Denied,
            AgentToolResultStatus.Cancelled => AgentToolCallState.Cancelled,
            AgentToolResultStatus.OutcomeUnknown => AgentToolCallState.OutcomeUnknown,
            AgentToolResultStatus.Succeeded => AgentToolCallState.Succeeded,
            _ => AgentToolCallState.Failed,
        };

    private void RefreshRunningState(TurnRun run)
    {
        if (!ReferenceEquals(run.Conversation.Turn, run) || run.Conversation.TurnState == AgentChatState.Cancelling)
        {
            return;
        }

        var state = run.Approvals.Values.Any(static card => card.IsPending) ? AgentChatState.WaitingApproval
            : run.Tools.Values.Any(static tool => !tool.IsTerminal) ? AgentChatState.WaitingTool
            : AgentChatState.Generating;
        run.Conversation.TurnState = state;
        if (ReferenceEquals(ActiveConversation, run.Conversation)) State = state;
    }

    private void RequestApproval(TurnRun run, AgentApprovalId approvalId, DateTimeOffset? runtimeExpiresAtUtc)
    {
        if (run.SessionId is not { } sessionId || run.Approvals.ContainsKey(approvalId))
        {
            return;
        }

        var approval = new AgentApprovalViewModel(_services.Runtime!, sessionId, run.TurnId, approvalId,
            _services.ApprovalDetails, _services.Clock, runtimeExpiresAtUtc);
        var card = new AgentApprovalCardItem(approval);
        approval.Settled += (_, state) =>
        {
            card.State = state;
            RefreshRunningState(run);
        };
        run.Approvals.Add(approvalId, card);
        run.Conversation.Items.Add(card);
        RefreshRunningState(run);
        _ = approval.LoadAsync(run.Cancellation.Token);
        ApprovalRequested?.Invoke(this, approval);
    }

    /// <summary>Reopens a pending approval from its conversation card.</summary>
    [RelayCommand]
    private void OpenApproval(AgentApprovalCardItem? card)
    {
        if (card is { IsPending: true })
        {
            ApprovalRequested?.Invoke(this, card.Approval);
        }
    }

    private void Finish(TurnRun run, AgentTurnOutcome outcome)
    {
        if (run.Conversation.UsageTurns.TryGetValue(run.TurnId, out var usage)) usage.Finish(outcome);
        if (ReferenceEquals(ActiveConversation, run.Conversation)) OnPropertyChanged(nameof(UsageDetails));
        foreach (var message in run.Messages.Values)
        {
            message.IsStreaming = false;
        }

        foreach (var card in run.Approvals.Values.Where(static card => card.IsPending))
        {
            card.Approval.CloseByRuntime(AgentApprovalOutcome.Denied, "ApprovalCancelled");
        }

        var state = outcome switch
        {
            AgentTurnOutcome.Completed => AgentChatState.Completed,
            AgentTurnOutcome.Cancelled => AgentChatState.Cancelled,
            AgentTurnOutcome.TimedOut => AgentChatState.TimedOut,
            AgentTurnOutcome.Failed => AgentChatState.Failed,
            _ => AgentChatState.OutcomeUnknown,
        };
        run.Conversation.TurnState = state;
        run.Conversation.TurnDetail = run.ErrorCode;
        if (ReferenceEquals(ActiveConversation, run.Conversation)) { StatusDetail = run.ErrorCode; State = state; }
    }

    private bool CanCancelTurn() => ActiveConversation.Turn is not null && State is not AgentChatState.Cancelling && IsBusy;

    /// <summary>Cancels only this tab's turn. Anything already dispatched may have taken effect: no rollback is shown.</summary>
    [RelayCommand(CanExecute = nameof(CanCancelTurn))]
    private async Task CancelTurnAsync()
    {
        if (ActiveConversation.Turn is not { } run)
        {
            return;
        }

        State = AgentChatState.Cancelling;
        if (run.SessionId is not { } sessionId)
        {
            // Cancelling a turn that just finished (and disposed its CTS) is a safe no-op, not an exception.
            await run.RequestCancellationAsync();
            return;
        }

        try
        {
            await _services.Runtime!.CancelTurnAsync(sessionId, run.TurnId, CancellationToken.None);
        }
        catch (AgentRuntimeException exception) when (exception.Code == "UnknownTurn")
        {
            // Already finished: its terminal event decides the state.
        }
        catch (Exception)
        {
            // Interruption not confirmed: stop consuming; the runtime reports OutcomeUnknown if it cannot confirm.
            run.ErrorCode = "CancellationUnconfirmed";
            await run.RequestCancellationAsync();
        }
    }

    private void ForgetSession(AgentChatConversation conversation)
    {
        conversation.SessionId = null;
        conversation.SessionProviderId = null;
        conversation.SessionModelId = null;
        conversation.SessionPersistsProviderState = null;
        conversation.SessionWorkingDirectory = null;
        conversation.HasSessionWorkingDirectory = false;
        if (ReferenceEquals(ActiveConversation, conversation)) RefreshReadScope();
    }

    private async Task CloseSessionAsync(AgentChatConversation conversation)
    {
        if (conversation.SessionId is not { } sessionId || _services.Runtime is not { } runtime)
        {
            ForgetSession(conversation);
            return;
        }

        ForgetSession(conversation);
        try
        {
            await runtime.CloseSessionAsync(sessionId, CancellationToken.None);
        }
        catch (Exception)
        {
            // Closing is best effort; the runtime disposes an adapter that does not confirm shutdown.
        }
    }

    private static string? SafeCodeOrNull(string? code) => code is null ? null : SafeCode(code);
}
