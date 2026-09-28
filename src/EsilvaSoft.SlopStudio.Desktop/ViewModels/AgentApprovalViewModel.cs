using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Core.Agents;
using EsilvaSoft.SlopStudio.Desktop.Agents;

namespace EsilvaSoft.SlopStudio.Desktop.ViewModels;

public enum AgentApprovalPhase
{
    Loading,
    Pending,
    Submitting,
    Granted,
    Rejected,
    Expired,
    Closed,
    DetailsMissing,
    Failed,
}

/// <summary>
/// One pending approval, bound to its session, turn and approval ID. Only the explicit <see cref="ApproveOnceCommand"/>
/// can grant it; model text, closing the window, Escape and expiry never do. Details come from a trusted source; without
/// them approval stays disabled and rejecting is the only action.
/// </summary>
public sealed partial class AgentApprovalViewModel : ObservableObject
{
    private readonly IAgentRuntime _runtime;
    private readonly IAgentApprovalDetailsSource? _detailsSource;
    private readonly TimeProvider _clock;
    private int _decisionStarted;

    public AgentApprovalViewModel(
        IAgentRuntime runtime,
        AgentSessionId sessionId,
        AgentTurnId turnId,
        AgentApprovalId approvalId,
        IAgentApprovalDetailsSource? detailsSource,
        TimeProvider clock,
        DateTimeOffset? runtimeExpiresAtUtc = null)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(clock);
        _runtime = runtime;
        _detailsSource = detailsSource;
        _clock = clock;
        SessionId = sessionId;
        TurnId = turnId;
        ApprovalId = approvalId;
        RuntimeExpiresAtUtc = runtimeExpiresAtUtc;
    }

    public event EventHandler? CloseRequested;

    /// <summary>Raised once when the decision (or its closure) is final, for the conversation card.</summary>
    public event EventHandler<AgentApprovalCardState>? Settled;

    public AgentSessionId SessionId { get; }

    public AgentTurnId TurnId { get; }

    public AgentApprovalId ApprovalId { get; }

    /// <summary>Deadline announced by the runtime on <c>ApprovalRequested</c> (trusted, never from provider data).</summary>
    public DateTimeOffset? RuntimeExpiresAtUtc { get; }

    /// <summary>
    /// The earlier of the runtime deadline and the details deadline: a details source can shorten the window shown,
    /// never extend it beyond what the runtime enforces.
    /// </summary>
    public DateTimeOffset? EffectiveExpiresAtUtc => Details is not { } details ? RuntimeExpiresAtUtc
        : RuntimeExpiresAtUtc is { } runtime && runtime < details.ExpiresAtUtc ? runtime : details.ExpiresAtUtc;

    private static LocalizationViewModel Text => LocalizationViewModel.Current;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToolText), nameof(TargetText), nameof(FilterText), nameof(ChangeText),
        nameof(LimitText), nameof(RiskText), nameof(IsDestructive), nameof(DestructivePrompt), nameof(HasDetails))]
    [NotifyCanExecuteChangedFor(nameof(ApproveOnceCommand))]
    private AgentApprovalDetails? _details;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MessageText), nameof(IsMessageError), nameof(IsDecisionOpen), nameof(IsBusy))]
    [NotifyCanExecuteChangedFor(nameof(ApproveOnceCommand), nameof(RejectCommand))]
    private AgentApprovalPhase _phase = AgentApprovalPhase.Loading;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApproveOnceCommand))]
    private string _destructiveConfirmation = "";

    [ObservableProperty] private string _remainingText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MessageText))]
    private string? _failureCode;

    public bool HasDetails => Details is not null;

    public bool IsDestructive => Details?.Risk == AgentToolRisk.Destructive;

    public bool IsBusy => Phase is AgentApprovalPhase.Loading or AgentApprovalPhase.Submitting;

    /// <summary>True while a human decision may still be taken.</summary>
    public bool IsDecisionOpen => Phase is AgentApprovalPhase.Loading or AgentApprovalPhase.Pending or
        AgentApprovalPhase.DetailsMissing;

    public string ToolText => Details?.ToolName ?? "—";

    public string TargetText => Details is { } d ? $"{d.ConnectionLabel} › {d.Database} › {d.Collection}" : "—";

    public string FilterText => Details?.Target ?? "—";

    public string ChangeText => Details is null ? "—"
        : Details.Change is { Length: > 0 } change ? change : Text.Resolve("agentApprovalNoChange");

    public string LimitText => Details?.AffectedLimit is { } limit
        ? Text.Format("agentApprovalLimitValue", limit)
        : Text.Resolve("agentApprovalLimitUnknown");

    public string RiskText => Details?.Risk switch
    {
        AgentToolRisk.ReadOnly => Text.Resolve("agentRiskReadOnly"),
        AgentToolRisk.Write => Text.Resolve("agentRiskWrite"),
        AgentToolRisk.Destructive => Text.Resolve("agentRiskDestructive"),
        _ => "—",
    };

    public string DestructivePrompt => Details is { } d ? Text.Format("agentApprovalDestructiveConfirm", d.Collection) : "";

    public bool IsMessageError => Phase is AgentApprovalPhase.Expired or AgentApprovalPhase.DetailsMissing or
        AgentApprovalPhase.Failed or AgentApprovalPhase.Closed;

    public string MessageText => Phase switch
    {
        AgentApprovalPhase.Loading => Text.Resolve("agentApprovalLoading"),
        AgentApprovalPhase.Pending => Text.Resolve("agentApprovalApproveHint"),
        AgentApprovalPhase.Submitting => Text.Resolve("agentApprovalSubmitting"),
        AgentApprovalPhase.Granted => Text.Resolve("agentApprovalGrantedNotice"),
        AgentApprovalPhase.Rejected => Text.Resolve("agentApprovalRejectedNotice"),
        AgentApprovalPhase.Expired => Text.Resolve("agentApprovalExpired"),
        AgentApprovalPhase.DetailsMissing => Text.Resolve("agentApprovalDetailsMissing"),
        AgentApprovalPhase.Closed => Text.Format("agentApprovalClosedByRuntime", FailureCode ?? "—"),
        _ => Text.Format("agentApprovalFailed", FailureCode ?? "—"),
    };

    /// <summary>Loads trusted details. Missing or failing details fail closed.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        AgentApprovalDetails? details = null;
        if (_detailsSource is not null)
        {
            try
            {
                details = await _detailsSource.DescribeAsync(SessionId, TurnId, ApprovalId, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception)
            {
                details = null;
            }
        }

        if (Phase != AgentApprovalPhase.Loading)
        {
            return; // Closed by the runtime or rejected while loading.
        }

        Details = details;
        Phase = details is null ? AgentApprovalPhase.DetailsMissing : AgentApprovalPhase.Pending;
        Tick();
    }

    /// <summary>Refreshes the countdown; the view calls it once per second. Expiry only ever disables approval.</summary>
    public void Tick()
    {
        if (Details is null || EffectiveExpiresAtUtc is not { } expiresAt)
        {
            RemainingText = "";
            return;
        }

        var remaining = expiresAt - _clock.GetUtcNow();
        if (remaining <= TimeSpan.Zero)
        {
            RemainingText = Text.Format("agentApprovalExpiresIn", "0:00");
            if (Phase == AgentApprovalPhase.Pending)
            {
                Phase = AgentApprovalPhase.Expired;
                Settle(AgentApprovalCardState.Expired);
            }

            return;
        }

        RemainingText = Text.Format("agentApprovalExpiresIn",
            string.Create(CultureInfo.InvariantCulture, $"{(int)remaining.TotalMinutes}:{remaining.Seconds:00}"));
    }

    private bool CanApproveOnce() =>
        Phase == AgentApprovalPhase.Pending && Details is { } details && EffectiveExpiresAtUtc > _clock.GetUtcNow() &&
        (details.Risk != AgentToolRisk.Destructive ||
         string.Equals(DestructiveConfirmation.Trim(), details.Collection, StringComparison.Ordinal));

    [RelayCommand(CanExecute = nameof(CanApproveOnce))]
    private async Task ApproveOnceAsync()
    {
        Tick();
        if (!CanApproveOnce() || Interlocked.Exchange(ref _decisionStarted, 1) != 0)
        {
            return;
        }

        await DecideAsync(AgentApprovalOutcome.Granted);
    }

    private bool CanReject() => Phase is not (AgentApprovalPhase.Submitting);

    /// <summary>Safe close action: rejects while a decision is open, otherwise only closes the window.</summary>
    [RelayCommand(CanExecute = nameof(CanReject))]
    private async Task RejectAsync()
    {
        if (!IsDecisionOpen || Interlocked.Exchange(ref _decisionStarted, 1) != 0)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        await DecideAsync(AgentApprovalOutcome.Denied);
    }

    /// <summary>The runtime closed the approval (expiry, turn end or a decision delivered elsewhere).</summary>
    public void CloseByRuntime(AgentApprovalOutcome outcome, string? errorCode)
    {
        if (Phase is AgentApprovalPhase.Granted or AgentApprovalPhase.Rejected)
        {
            return;
        }

        Interlocked.Exchange(ref _decisionStarted, 1);
        if (outcome == AgentApprovalOutcome.Granted)
        {
            Phase = AgentApprovalPhase.Granted;
            Settle(AgentApprovalCardState.Granted);
            return;
        }

        FailureCode = errorCode;
        (Phase, var card) = errorCode switch
        {
            "ApprovalExpired" => (AgentApprovalPhase.Expired, AgentApprovalCardState.Expired),
            "ApprovalCancelled" => (AgentApprovalPhase.Closed, AgentApprovalCardState.Cancelled),
            null => (AgentApprovalPhase.Rejected, AgentApprovalCardState.Denied),
            _ => (AgentApprovalPhase.Closed, AgentApprovalCardState.Denied),
        };
        Settle(card);
    }

    private async Task DecideAsync(AgentApprovalOutcome outcome)
    {
        var decision = new AgentApprovalDecision(SessionId, TurnId, ApprovalId, outcome);
        Phase = AgentApprovalPhase.Submitting;
        try
        {
            await _runtime.DecideApprovalAsync(decision, CancellationToken.None);
            Phase = outcome == AgentApprovalOutcome.Granted ? AgentApprovalPhase.Granted : AgentApprovalPhase.Rejected;
            Settle(outcome == AgentApprovalOutcome.Granted ? AgentApprovalCardState.Granted : AgentApprovalCardState.Denied);
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (AgentRuntimeException exception) when (exception.Code == "ApprovalExpired")
        {
            FailureCode = exception.Code;
            Phase = AgentApprovalPhase.Expired;
            Settle(AgentApprovalCardState.Expired);
        }
        catch (AgentRuntimeException exception)
        {
            FailureCode = exception.Code;
            // A rejection that could not be delivered still executes nothing; an approval that failed is not granted.
            Phase = outcome == AgentApprovalOutcome.Denied ? AgentApprovalPhase.Rejected : AgentApprovalPhase.Failed;
            Settle(AgentApprovalCardState.Denied);
            if (outcome == AgentApprovalOutcome.Denied)
            {
                CloseRequested?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception)
        {
            FailureCode = "ApprovalDeliveryFailed";
            Phase = outcome == AgentApprovalOutcome.Denied ? AgentApprovalPhase.Rejected : AgentApprovalPhase.Failed;
            Settle(AgentApprovalCardState.Denied);
        }
    }

    private void Settle(AgentApprovalCardState state) => Settled?.Invoke(this, state);
}
