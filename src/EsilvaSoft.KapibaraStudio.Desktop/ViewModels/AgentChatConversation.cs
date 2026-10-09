using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Application.Agents;

namespace EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

/// <summary>
/// One workspace-global conversation in memory (ADR-056): its rows, its own runtime session and turn (CTS per turn),
/// and its persistence state. It is not bound to any tab. A turn writes only to the conversation that started it,
/// whether or not it is the one shown. Persisted form: <see cref="ToRecord"/> (messages redacted by the repository,
/// tool summaries, notices and proposal references; never attachment contents or credentials).
/// </summary>
public sealed partial class AgentChatConversation : ObservableObject
{
    public const int MaximumTitleChars = 60;

    private static LocalizationViewModel Text => LocalizationViewModel.Current;

    public AgentChatConversation(Guid id, string providerId, string? modelId, AgentOperationMode mode, DateTimeOffset createdAt)
    {
        Id = id;
        _providerId = providerId;
        _modelId = modelId;
        _mode = mode;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
        Items.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
    }

    public Guid Id { get; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; internal set; }

    /// <summary>Revision of the stored copy; 0 = never stored.</summary>
    public long Revision { get; internal set; }

    public ObservableCollection<AgentChatItemViewModel> Items { get; } = [];

    /// <summary>Observed usage for this execution only; excluded from ToRecord and LiteDB.</summary>
    public Dictionary<AgentTurnId, AgentUsageAccumulator> UsageTurns { get; } = [];

    public bool IsEmpty => Items.Count == 0;

    /// <summary>Sticky taint: once a captured result or diagnostic tool appears, reasoning is never retained.</summary>
    internal bool HasCapturedDocumentTool => Items.OfType<AgentToolCallItem>().Any(static tool =>
        tool.ToolName is "get_query_results" or "get_query_diagnostics");

    /// <summary>Refreshed from current provider permissions before each save; prevents stale turns re-adding opt-out text.</summary>
    internal bool AllowReasoningPersistence { get; set; }

    /// <summary>Provider of the conversation. Changing provider on a non-empty conversation starts another one.</summary>
    [ObservableProperty] private string _providerId;

    [ObservableProperty] private string? _modelId;

    [ObservableProperty] private AgentOperationMode _mode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayTitle))]
    private string _title = "";

    /// <summary>Provider's own session (e.g. <c>--resume</c>); opaque, never a credential.</summary>
    [ObservableProperty] private string? _providerSessionId;

    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? Text.Resolve("agentConversationUntitled") : Title;

    // Runtime session of this conversation (not persisted).
    internal AgentSessionId? SessionId { get; set; }

    internal string? SessionProviderId { get; set; }

    internal string? SessionModelId { get; set; }

    /// <summary>Retention captured when the runtime session started; never persisted with conversation history.</summary>
    internal bool? SessionPersistsProviderState { get; set; }

    /// <summary>Volatile Copilot IDs for explicit cleanup only; never serialized, reserved or resumed.</summary>
    internal HashSet<string> VolatileProviderSessionIds { get; } = new(StringComparer.Ordinal);

    internal string? SessionWorkingDirectory { get; set; }

    internal bool HasSessionWorkingDirectory { get; set; }

    /// <summary>The running turn of this conversation (one at a time), null when idle.</summary>
    internal AgentChatViewModel.TurnRun? Turn { get; set; }

    /// <summary>The provider session could not be resumed: the next send starts a new one with a local summary.</summary>
    internal bool ResumeLost { get; set; }

    /// <summary>Turn state shown while this conversation is visible; null = idle (the panel shows provider readiness).</summary>
    [ObservableProperty] private AgentChatState? _turnState;

    /// <summary>Safe detail code of the last turn state.</summary>
    [ObservableProperty] private string? _turnDetail;

    /// <summary>Visible persistence problem of this conversation (the conversation stays in memory).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPersistenceText))]
    private string? _persistenceText;

    [ObservableProperty] private bool _persistenceIsError;

    public bool HasPersistenceText => !string.IsNullOrEmpty(PersistenceText);

    /// <summary>A stored copy must not be overwritten (unreadable/newer format elsewhere): saves are suspended.</summary>
    internal bool IsWriteBlocked { get; set; }

    internal SemaphoreSlim SaveGate { get; } = new(1, 1);

    public bool IsBusy => Turn is not null;

    /// <summary>Title from the first user message: first line, whitespace collapsed, truncated.</summary>
    public static string TitleFrom(string message)
    {
        var line = (message ?? "").Replace('\r', '\n').Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(static part => part.Trim()).FirstOrDefault(static part => part.Length > 0) ?? "";
        line = string.Join(' ', line.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        line = new string([.. line.Where(static c => !char.IsControl(c))]);
        return line.Length <= MaximumTitleChars ? line : line[..(MaximumTitleChars - 1)].TrimEnd() + "…";
    }

    /// <summary>Persistable snapshot of the rows (UI thread). Confirmation/approval cards are runtime-only.</summary>
    public AgentConversation ToRecord(DateTimeOffset now)
    {
        var entries = new List<AgentConversationEntry>(Items.Count);
        var hasReasoningNotSavedNotice = Items.OfType<AgentChatNoticeItem>()
            .Any(static notice => notice.PersistenceKey == AgentChatNoticeItem.ReasoningNotSavedKey);
        foreach (var item in Items)
        {
            switch (item)
            {
                case AgentChatMessageItem message when message.IsUser:
                    entries.Add(new AgentConversationEntry(AgentConversationEntryKind.UserMessage, message.Content, message.Timestamp)
                    {
                        Attachments = message.Attachments,
                    });
                    break;
                case AgentChatMessageItem message when message.Content.Length > 0:
                    entries.Add(new AgentConversationEntry(AgentConversationEntryKind.AssistantMessage, message.Content, message.Timestamp));
                    break;
                case AgentToolCallItem tool:
                    entries.Add(new AgentConversationEntry(AgentConversationEntryKind.ToolCall, "", tool.Timestamp)
                    {
                        ToolName = tool.ToolName,
                        ToolOutcome = tool.PersistedOutcome,
                    });
                    break;
                case AgentChatNoticeItem notice:
                    entries.Add(new AgentConversationEntry(AgentConversationEntryKind.Notice, notice.Content, notice.Timestamp)
                    {
                        ToolName = notice.PersistenceKey,
                    });
                    break;
                case AgentEditProposalCardItem proposal:
                    entries.Add(new AgentConversationEntry(AgentConversationEntryKind.EditProposal, proposal.PersistedSummary, proposal.Timestamp)
                    {
                        ProposalId = proposal.ProposalId,
                    });
                    break;
                case AgentReasoningItem reasoning:
                    if (AllowReasoningPersistence && !reasoning.IsStreaming && reasoning.IsRetentionEligible &&
                        !HasCapturedDocumentTool && reasoning.RetentionExpiresAtUtc is { } expiresAt && expiresAt > now)
                    {
                        entries.Add(new AgentConversationEntry(AgentConversationEntryKind.ReasoningText, reasoning.Content, reasoning.Timestamp)
                        {
                            ExpiresAtUtc = expiresAt,
                        });
                    }
                    else if (!reasoning.IsStreaming && !hasReasoningNotSavedNotice)
                    {
                        entries.Add(ReasoningNotSavedNotice(reasoning.Timestamp));
                        hasReasoningNotSavedNotice = true;
                    }
                    break;
            }
        }

        return new AgentConversation(Id, ProviderId, string.IsNullOrWhiteSpace(Title) ? Text.Resolve("agentConversationUntitled") : Title,
            ModelId, Mode, ProviderSessionId, CreatedAt, now, Revision, entries);
    }

    /// <summary>Rebuilds a stored conversation (no session: a new one resumes by <see cref="ProviderSessionId"/>).</summary>
    public static AgentChatConversation FromRecord(AgentConversation record, Func<Guid, Desktop.Agents.AgentEditProposalEntry?> proposals,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(record);
        var conversation = new AgentChatConversation(record.Id, record.ProviderId, record.ModelId, record.Mode, record.CreatedAt)
        {
            Title = record.Title,
            ProviderSessionId = record.ProviderSessionId,
            Revision = record.Revision,
            UpdatedAt = record.UpdatedAt,
        };
        var instant = now ?? DateTimeOffset.UtcNow;
        DateTimeOffset? discardedReasoningAt = null;
        foreach (var entry in record.Entries ?? [])
        {
            AgentChatItemViewModel? item = entry.Kind switch
            {
                AgentConversationEntryKind.UserMessage => new AgentChatMessageItem(AgentChatRole.User, entry.Text, null, entry.Attachments),
                AgentConversationEntryKind.AssistantMessage => new AgentChatMessageItem(AgentChatRole.Agent, entry.Text),
                AgentConversationEntryKind.ToolCall => AgentToolCallItem.Restored(entry.ToolName, entry.ToolOutcome),
                AgentConversationEntryKind.Notice when entry.ToolName == AgentChatNoticeItem.ReasoningNotSavedKey =>
                    new AgentChatNoticeItem(entry.Text, isWarning: true, persistenceKey: AgentChatNoticeItem.ReasoningNotSavedKey),
                AgentConversationEntryKind.Notice => new AgentChatNoticeItem(entry.Text, isWarning: true),
                AgentConversationEntryKind.EditProposal when entry.ProposalId is { } id => RestoreProposal(id, entry.Text, proposals(id)),
                AgentConversationEntryKind.ReasoningText when entry.ExpiresAtUtc is { } expiresAt && expiresAt > instant =>
                    AgentReasoningItem.Restore(entry.Text, entry.Timestamp, expiresAt),
                _ => null,
            };
            if (item is not null)
            {
                item.Timestamp = entry.Timestamp;
                conversation.Items.Add(item);
            }
            else if (entry.Kind == AgentConversationEntryKind.ReasoningText)
            {
                discardedReasoningAt ??= entry.Timestamp;
            }
        }

        if (discardedReasoningAt is { } discardedAt && !conversation.Items.OfType<AgentChatNoticeItem>()
                .Any(static notice => notice.PersistenceKey == AgentChatNoticeItem.ReasoningNotSavedKey))
        {
            var notice = new AgentChatNoticeItem(Text.Resolve("agentReasoningNotSaved"), isWarning: true,
                persistenceKey: AgentChatNoticeItem.ReasoningNotSavedKey) { Timestamp = discardedAt };
            conversation.Items.Add(notice);
        }

        return conversation;
    }

    private static AgentConversationEntry ReasoningNotSavedNotice(DateTimeOffset timestamp) =>
        new(AgentConversationEntryKind.Notice, Text.Resolve("agentReasoningNotSaved"), timestamp)
        {
            ToolName = AgentChatNoticeItem.ReasoningNotSavedKey,
        };

    private static AgentEditProposalCardItem RestoreProposal(Guid id, string summary, Desktop.Agents.AgentEditProposalEntry? live)
    {
        if (live is not null)
        {
            return AgentEditProposalCardItem.From(live);
        }

        // Summary "file +N −M" as persisted; the texts are not kept, so the card is read-only.
        var separator = summary.LastIndexOf(AgentEditProposalCardItem.SummarySeparator, StringComparison.Ordinal);
        var name = separator < 0 ? summary : summary[..separator];
        return new AgentEditProposalCardItem(id, name.Length == 0 ? "?" : name, 0, 0, null)
        {
            PersistedCounts = separator < 0 ? "" : summary[(separator + AgentEditProposalCardItem.SummarySeparator.Length)..],
        };
    }
}
