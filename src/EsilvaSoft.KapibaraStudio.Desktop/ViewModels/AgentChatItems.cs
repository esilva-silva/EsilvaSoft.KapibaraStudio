using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Desktop.Agents;

namespace EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

/// <summary>
/// One row of an agent conversation. Messages, tool summaries, notices and proposal references are persisted by the
/// conversation (ADR-056, redacted, never attachment contents); confirmation cards are runtime-only.
/// </summary>
public abstract class AgentChatItemViewModel : ObservableObject
{
    protected static LocalizationViewModel Text => LocalizationViewModel.Current;

    /// <summary>When the row was created (or the persisted entry's timestamp when restored).</summary>
    public DateTimeOffset Timestamp { get; internal set; } = DateTimeOffset.UtcNow;
}

public enum AgentChatRole
{
    User,
    Agent,
}

public sealed partial class AgentChatMessageItem : AgentChatItemViewModel
{
    public AgentChatMessageItem(AgentChatRole role, string text, AgentMessageId? messageId = null,
        IReadOnlyList<AgentAttachmentDescriptor>? attachments = null)
    {
        Role = role;
        MessageId = messageId;
        _content = text;
        _isStreaming = role == AgentChatRole.Agent && messageId is not null;
        Attachments = attachments ?? [];
        UpdateBlocks(text);
    }

    public AgentChatRole Role { get; }

    public AgentMessageId? MessageId { get; }

    public bool IsUser => Role == AgentChatRole.User;

    public string RoleLabel => Text.Resolve(IsUser ? "agentRoleUser" : "agentRoleAgent");

    /// <summary>Descriptors of the chips sent with this message (name, kind, size — never the content).</summary>
    public IReadOnlyList<AgentAttachmentDescriptor> Attachments { get; }

    public bool HasAttachments => Attachments.Count > 0;

    /// <summary>Memory-only measurement of the authorized request used by this originating message.</summary>
    public AgentContextMeasurement? ContextMeasurement { get; init; }
    public bool HasContextMetrics => ContextMeasurement is not null;
    public string ContextMetricsDetails => AgentMetricText.Context(ContextMeasurement, sent: true);

    /// <summary>"Anexos: clientes.json (1,2 KB) · developercluster › CakeShop" — identity only.</summary>
    public string AttachmentsText => HasAttachments
        ? Text.Format("agentMessageAttachments", string.Join(" · ", Attachments.Select(AgentContextChipViewModel.DescribeDescriptor)))
        : "";

    /// <summary>Model text is displayed as data only; it never triggers commands, approvals or queries.</summary>
    [ObservableProperty] private string _content;

    [ObservableProperty] private bool _isStreaming;

    public string StreamingLabel { get; } = Text.Resolve("agentStreaming");

    public ObservableCollection<AgentChatMessageBlock> Blocks { get; } = [];

    partial void OnContentChanged(string value) => UpdateBlocks(value);

    private void UpdateBlocks(string content)
    {
        var parsed = AgentChatMessageBlock.Parse(content);
        for (var index = 0; index < parsed.Count; index++)
        {
            var block = parsed[index];
            if (index < Blocks.Count && Blocks[index].IsCode == block.IsCode)
            {
                // Preserve each block/control identity during streaming (selection, scroll and focus).
                Blocks[index].Text = block.Text;
                Blocks[index].Language = block.Language;
            }
            else if (index < Blocks.Count) Blocks[index] = block;
            else Blocks.Add(block);
        }
        while (Blocks.Count > parsed.Count) Blocks.RemoveAt(Blocks.Count - 1);
    }

    internal void Append(string fragment) => Content += fragment;
}

public enum AgentToolCallState
{
    Requested,
    Running,
    Succeeded,
    Failed,
    Denied,
    Cancelled,
    OutcomeUnknown,
}

public sealed partial class AgentToolCallItem : AgentChatItemViewModel
{
    private long? _startedTimestamp;

    public AgentToolCallItem(AgentToolCallId callId, string? toolName, AgentDataDestinationKind? destination = null,
        AgentToolOrigin? origin = null, string? observedBy = null)
    {
        CallId = callId;
        ToolName = string.IsNullOrWhiteSpace(toolName) ? null : toolName;
        ToolLabel = ToolName is null ? Text.Resolve("agentToolUnknown") : AgentToolConfirmationCardItem.DisplayToolName(ToolName);
        Destination = destination;
        Origin = origin;
        ObservedBy = observedBy;
    }

    /// <summary>Restores a persisted tool summary (name and outcome only).</summary>
    internal static AgentToolCallItem Restored(string? toolName, AgentToolResultStatus? outcome)
    {
        var item = new AgentToolCallItem(AgentToolCallId.New(), toolName);
        item.State = outcome switch
        {
            AgentToolResultStatus.Succeeded => AgentToolCallState.Succeeded,
            AgentToolResultStatus.Denied => AgentToolCallState.Denied,
            AgentToolResultStatus.Cancelled => AgentToolCallState.Cancelled,
            AgentToolResultStatus.OutcomeUnknown => AgentToolCallState.OutcomeUnknown,
            null => AgentToolCallState.OutcomeUnknown,
            _ => AgentToolCallState.Failed,
        };
        return item;
    }

    /// <summary>Who ran the call, as published by the runtime (never inferred from provider output).</summary>
    public AgentToolOrigin? Origin { get; }

    /// <summary>Name of the executor of a provider-native tool (e.g. the official CLI), supplied by the host.</summary>
    public string? ObservedBy { get; }

    /// <summary>Native tool of the provider (display only): ran outside the registry, with no product approval.</summary>
    public bool IsProviderObserved => Origin == AgentToolOrigin.ProviderObserved;

    public string OriginText => Origin switch
    {
        AgentToolOrigin.ProviderObserved => Text.Format("agentToolOriginObserved",
            string.IsNullOrWhiteSpace(ObservedBy) ? Text.Resolve("agentToolOriginProvider") : ObservedBy, Branding.ProductName),
        AgentToolOrigin.Registry => Text.Format("agentToolOriginRegistry", Branding.ProductName),
        _ => "",
    };

    public bool HasOriginText => OriginText.Length > 0;

    /// <summary>Sanitized output destination published by the runtime (Local/External); null when not reported.</summary>
    public AgentDataDestinationKind? Destination { get; }

    public string DestinationText => Destination switch
    {
        AgentDataDestinationKind.Local => Text.Format("agentToolDestination", Text.Resolve("agentDestinationLocal")),
        AgentDataDestinationKind.External => Text.Format("agentToolDestination", Text.Resolve("agentDestinationExternal")),
        _ => "",
    };

    public AgentToolCallId CallId { get; }

    /// <summary>Canonical name published by the runtime (persisted); raw model text is never echoed.</summary>
    public string? ToolName { get; }

    public string ToolLabel { get; }

    public string Title => Text.Format(IsProviderObserved ? "agentToolNativeCard" : "agentToolCard", ToolLabel);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(IsError), nameof(IsTerminal), nameof(IsUncertain), nameof(CanReviewPermissions))]
    private AgentToolCallState _state;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private long? _durationMilliseconds;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(CanReviewPermissions))]
    private string? _errorCode;

    public bool IsError => State is AgentToolCallState.Failed or AgentToolCallState.Denied or AgentToolCallState.OutcomeUnknown;

    public bool IsUncertain => State == AgentToolCallState.OutcomeUnknown;

    /// <summary>Permission denials can be corrected in the provider's persistent permissions window.</summary>
    public bool CanReviewPermissions => State == AgentToolCallState.Denied &&
        ErrorCode is "PermissionDenied" or "PermissionMissing";

    public bool IsTerminal => State is not (AgentToolCallState.Requested or AgentToolCallState.Running);

    /// <summary>Outcome persisted with the conversation (name and outcome only).</summary>
    public AgentToolResultStatus PersistedOutcome => State switch
    {
        AgentToolCallState.Succeeded => AgentToolResultStatus.Succeeded,
        AgentToolCallState.Denied => AgentToolResultStatus.Denied,
        AgentToolCallState.Cancelled => AgentToolResultStatus.Cancelled,
        AgentToolCallState.Failed => AgentToolResultStatus.Failed,
        _ => AgentToolResultStatus.OutcomeUnknown,
    };

    public string StatusText
    {
        get
        {
            var status = Text.Resolve(State switch
            {
                AgentToolCallState.Requested => "agentToolRequested",
                AgentToolCallState.Running => "agentToolRunning",
                AgentToolCallState.Succeeded => "agentToolSucceeded",
                AgentToolCallState.Failed => "agentToolFailed",
                AgentToolCallState.Denied => "agentToolDenied",
                AgentToolCallState.Cancelled => "agentToolCancelled",
                _ => "agentToolOutcomeUnknown",
            });
            if (DurationMilliseconds is { } duration)
            {
                status = Text.Format("agentToolStatusLine", status, Text.Format("agentToolDuration", duration));
            }

            return ErrorCode is { Length: > 0 } code
                ? Text.Format("agentToolStatusLine", status, Text.HasTranslation(AgentChatViewModel.ErrorCodePrefix + code)
                    ? Text.Resolve(AgentChatViewModel.ErrorCodePrefix + code)
                    : Text.Format("agentToolCode", code))
                : status;
        }
    }

    internal void MarkStarted(long timestamp)
    {
        _startedTimestamp = timestamp;
        State = AgentToolCallState.Running;
    }

    internal void Complete(AgentToolCallState state, string? errorCode, TimeProvider clock)
    {
        if (_startedTimestamp is { } started)
        {
            DurationMilliseconds = (long)clock.GetElapsedTime(started).TotalMilliseconds;
        }

        ErrorCode = errorCode;
        State = state;
    }
}

public enum AgentApprovalCardState
{
    Pending,
    Granted,
    Denied,
    Expired,
    Cancelled,
}

/// <summary>Card of a registry write approval (lote 10); the decision happens in the approval dialog.</summary>
public sealed partial class AgentApprovalCardItem : AgentChatItemViewModel
{
    public AgentApprovalCardItem(AgentApprovalViewModel approval) => Approval = approval;

    public AgentApprovalViewModel Approval { get; }

    public string Title { get; } = Text.Resolve("agentApprovalCardTitle");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(IsPending))]
    private AgentApprovalCardState _state;

    public bool IsPending => State == AgentApprovalCardState.Pending;

    public string StatusText => Text.Resolve(State switch
    {
        AgentApprovalCardState.Pending => "agentApprovalPending",
        AgentApprovalCardState.Granted => "agentApprovalGranted",
        AgentApprovalCardState.Denied => "agentApprovalDenied",
        AgentApprovalCardState.Expired => "agentApprovalExpiredCard",
        _ => "agentApprovalCancelledCard",
    });

    public string ReviewLabel { get; } = Text.Resolve("agentApprovalReview");
}

public enum AgentToolConfirmationState
{
    Pending,
    ApprovedOnce,
    ApprovedThisSession,
    Rejected,
    Expired,
}

/// <summary>
/// Inline confirmation of one tool call (modo Solicitar confirmações). The exact input is shown as data in code font;
/// "Rejeitar" is the safe action. Session approval is offered only for read-only operations and binds exact arguments
/// to the in-memory session. The registry deadline cancels the token: the card then shows "expirada" and rejects.
/// Runtime only: never persisted.
/// </summary>
public sealed partial class AgentToolConfirmationCardItem : AgentChatItemViewModel
{
    private const string McpPrefix = "mcp__" + McpServerLaunchSpec.DefaultServerName + "__";
    private readonly TaskCompletionSource<AgentToolConfirmationDecision> _decision =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public AgentToolConfirmationCardItem(AgentToolConfirmationRequest request, CancellationToken deadline)
    {
        ArgumentNullException.ThrowIfNull(request);
        Request = request;
        ToolLabel = DisplayToolName(request.ToolName);
        // The approval applies to this exact invocation. Keep the complete serialized arguments available in the
        // read-only, internally scrollable text box; silently truncating them would hide data the user is approving.
        InputText = request.InputJson ?? "";
        if (deadline.CanBeCanceled)
        {
            deadline.Register(() =>
            {
                if (_decision.TrySetResult(AgentToolConfirmationDecision.Rejected))
                {
                    AgentUiDispatch.Post(() => State = AgentToolConfirmationState.Expired);
                }
            });
        }
    }

    public AgentToolConfirmationRequest Request { get; }

    public Guid ConversationId => Request.ConversationId;

    public string ToolLabel { get; }

    public string Title => Text.Format("agentConfirmTitle", ToolLabel);

    public string InputText { get; }

    public bool HasInput => InputText.Length > 0;

    public bool CanApproveThisSession =>
        Request.ProviderId != AgentProviderIds.GitHubCopilotSubscription &&
        Request.Category is AgentConfirmationCategories.MongoMetadataRead or
            AgentConfirmationCategories.WorkspaceContextRead or AgentConfirmationCategories.NativeFileRead;

    public string CategoryText => Text.Resolve(Request.Category switch
    {
        AgentConfirmationCategories.MongoMetadataRead => "agentConfirmCategoryMongo",
        AgentConfirmationCategories.MongoDocumentRead => "agentConfirmCategoryMongoDocuments",
        AgentConfirmationCategories.WorkspaceContextRead => "agentConfirmCategoryWorkspace",
        AgentConfirmationCategories.NativeFileRead => "agentConfirmCategoryFile",
        AgentConfirmationCategories.NativeCommand => "agentConfirmCategoryCommand",
        AgentConfirmationCategories.NativeFileWrite => "agentConfirmCategoryWrite",
        AgentConfirmationCategories.NativeNetwork => "agentConfirmCategoryNetwork",
        AgentConfirmationCategories.EditProposal => "agentConfirmCategoryProposal",
        _ => "agentConfirmCategoryOther",
    });

    public Task<AgentToolConfirmationDecision> Decision => _decision.Task;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPending), nameof(StatusText))]
    [NotifyCanExecuteChangedFor(nameof(ApproveOnceCommand), nameof(ApproveThisSessionCommand), nameof(RejectCommand))]
    private AgentToolConfirmationState _state;

    public bool IsPending => State == AgentToolConfirmationState.Pending;

    public string StatusText => Text.Resolve(State switch
    {
        AgentToolConfirmationState.Pending => "agentConfirmPending",
        AgentToolConfirmationState.ApprovedOnce => "agentConfirmApproved",
        AgentToolConfirmationState.ApprovedThisSession => "agentConfirmApprovedSession",
        AgentToolConfirmationState.Rejected => "agentConfirmRejected",
        _ => "agentConfirmExpired",
    });

    [RelayCommand(CanExecute = nameof(IsPending))]
    private void ApproveOnce()
    {
        if (_decision.TrySetResult(AgentToolConfirmationDecision.ApprovedOnce))
        {
            State = AgentToolConfirmationState.ApprovedOnce;
        }
    }

    [RelayCommand(CanExecute = nameof(CanApproveThisSessionAndPending))]
    private void ApproveThisSession()
    {
        if (_decision.TrySetResult(AgentToolConfirmationDecision.ApprovedThisSession))
            State = AgentToolConfirmationState.ApprovedThisSession;
    }

    private bool CanApproveThisSessionAndPending() => IsPending && CanApproveThisSession;

    [RelayCommand(CanExecute = nameof(IsPending))]
    private void Reject()
    {
        if (_decision.TrySetResult(AgentToolConfirmationDecision.Rejected))
        {
            State = AgentToolConfirmationState.Rejected;
        }
    }

    /// <summary>Closes a pending card as rejected (turn ended, conversation deleted, panel disposed).</summary>
    internal void Close()
    {
        if (_decision.TrySetResult(AgentToolConfirmationDecision.Rejected))
        {
            State = AgentToolConfirmationState.Expired;
        }
    }

    /// <summary>"list_connections" for "mcp__kapibarastudio__list_connections"; native names are kept.</summary>
    public static string DisplayToolName(string? toolName) =>
        string.IsNullOrWhiteSpace(toolName) ? "?"
        : toolName.StartsWith(McpPrefix, StringComparison.Ordinal) ? toolName[McpPrefix.Length..]
        : toolName;
}

/// <summary>
/// Card of an edit proposal (<c>propose_file_edit</c>): file, <c>+N −M</c>, state in text and the actions
/// "Revisar" (opens/activates the target tab; the hunk review in the editor is CLP-6), "Aplicar tudo" and "Descartar";
/// in the automatic mode "Manter"/"Reverter". The proposal itself lives in <see cref="AgentEditProposalStore"/>; the
/// conversation persists only its ID and a short summary. After a restart the store no longer has it: the card is
/// then read-only ("indisponível após reiniciar").
/// </summary>
public sealed partial class AgentEditProposalCardItem : AgentChatItemViewModel
{
    public AgentEditProposalCardItem(Guid proposalId, string fileName, int added, int removed, AgentEditProposalEntry? entry)
    {
        ProposalId = proposalId;
        FileName = fileName;
        AddedLines = added;
        RemovedLines = removed;
        _entry = entry;
    }

    internal static AgentEditProposalCardItem From(AgentEditProposalEntry entry) =>
        new(entry.Id, entry.Proposal.TargetName ?? (entry.Proposal.TargetPath is { } path ? Path.GetFileName(path) : "Aba sem título"), entry.Proposal.AddedLineCount,
            entry.Proposal.RemovedLineCount, entry)
        {
            TargetPath = entry.Proposal.TargetPath,
            TabId = entry.Proposal.TabId,
        };

    public Guid ProposalId { get; }

    public string FileName { get; }

    public string? TargetPath { get; private init; }

    public string? TabId { get; private init; }

    public int AddedLines { get; }

    public int RemovedLines { get; }

    public string Title => Text.Format("agentProposalTitle", FileName);

    public string CountsText => PersistedCounts ?? Text.Format("agentProposalCounts", AddedLines, RemovedLines);

    public string AddedCountText => Text.Format("agentProposalAddedCount", AddedLines);

    public string RemovedCountText => Text.Format("agentProposalRemovedCount", RemovedLines);

    /// <summary>Counts restored from the persisted summary when the proposal is no longer in memory.</summary>
    public string? PersistedCounts { get; init; }

    public bool HasPersistedCounts => PersistedCounts is not null;

    /// <summary>Accessible summary of the counts ("3 linhas adicionadas, 1 removida").</summary>
    public string CountsAccessibleText => Text.Format("agentProposalCountsAccessible", AddedLines, RemovedLines);

    /// <summary>Persisted summary (no text of the proposal).</summary>
    public string PersistedSummary => FileName + SummarySeparator + CountsText;

    internal const string SummarySeparator = " · ";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(IsAvailable), nameof(CanApply), nameof(CanDiscard),
        nameof(ShowKeepRevert), nameof(ShowApplyDiscard))]
    [NotifyCanExecuteChangedFor(nameof(ReviewCommand), nameof(ApplyAllCommand), nameof(DiscardCommand), nameof(KeepCommand), nameof(RevertCommand))]
    private AgentEditProposalEntry? _entry;

    /// <summary>Applied automatically on arrival (Automático): the card offers Manter/Reverter.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowKeepRevert), nameof(ShowApplyDiscard))]
    [NotifyCanExecuteChangedFor(nameof(KeepCommand), nameof(RevertCommand))]
    private bool _isAutomatic;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApply), nameof(CanDiscard))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorText;

    public bool HasError => !string.IsNullOrEmpty(ErrorText);

    public bool IsAvailable => Entry is not null;

    public bool CanApply => !IsBusy && Entry is { HasPending: true };

    public bool CanDiscard => !IsBusy && Entry is { } entry && (entry.HasPending || entry.HasApplied);

    /// <summary>Automatic proposals with applied hunks offer Manter/Reverter instead of Aplicar/Descartar.</summary>
    public bool ShowKeepRevert => IsAutomatic && Entry is { HasApplied: true } entry &&
        entry.HunkStates.Any(static state => state == AgentEditHunkState.Applied);

    public bool ShowApplyDiscard => IsAvailable && !ShowKeepRevert;

    public string StatusText => Entry is not { } entry
        ? Text.Resolve("agentProposalUnavailable")
        : Text.Resolve(entry.Status switch
        {
            AgentEditProposalStatus.Registered => "agentProposalRegistered",
            AgentEditProposalStatus.Applied => "agentProposalApplied",
            AgentEditProposalStatus.PartiallyApplied => "agentProposalPartial",
            AgentEditProposalStatus.Discarded => "agentProposalDiscarded",
            _ => "agentProposalStale",
        });

    /// <summary>Raised by <see cref="ReviewCommand"/>; the chat activates the tab and forwards it to the store.</summary>
    internal Func<AgentEditProposalCardItem, Task>? ReviewHandler { get; set; }

    internal Func<AgentEditProposalCardItem, Task>? ApplyAllHandler { get; set; }

    internal Func<AgentEditProposalCardItem, Task>? DiscardHandler { get; set; }

    internal Func<AgentEditProposalCardItem, Task>? KeepHandler { get; set; }

    internal Func<AgentEditProposalCardItem, Task>? RevertHandler { get; set; }

    [RelayCommand(CanExecute = nameof(IsAvailable))]
    private Task ReviewAsync() => ReviewHandler?.Invoke(this) ?? Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanApply))]
    private Task ApplyAllAsync() => ApplyAllHandler?.Invoke(this) ?? Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanDiscard))]
    private Task DiscardAsync() => DiscardHandler?.Invoke(this) ?? Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(ShowKeepRevert))]
    private Task KeepAsync() => KeepHandler?.Invoke(this) ?? Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(ShowKeepRevert))]
    private Task RevertAsync() => RevertHandler?.Invoke(this) ?? Task.CompletedTask;
}

public sealed class AgentChatNoticeItem(string content, bool isError = false, bool isWarning = false) : AgentChatItemViewModel
{
    public string Content { get; } = content;

    public string TimeText => Timestamp.UtcDateTime.ToString("HH:mm 'UTC'", System.Globalization.CultureInfo.InvariantCulture);

    public bool IsError { get; } = isError;

    /// <summary>A visible, non-fatal notice (resume lost, tools unavailable…) shown with the warning outline.</summary>
    public bool IsWarning { get; } = isWarning;
}
