namespace EsilvaSoft.SlopStudio.Core;

/// <summary>Closed, content-free metadata for one agent tool invocation. No prompt, arguments, result or error text.</summary>
public sealed record AgentAuditEvent(
    Guid Id,
    int SchemaVersion,
    DateTimeOffset OccurredAtUtc,
    Guid PrincipalId,
    Guid InvocationId,
    Guid? SessionId,
    Guid? TurnId,
    AgentAuditChannel Channel,
    string? ExternalIdentifier,
    string ToolName,
    int ToolVersion,
    AgentToolRisk Risk,
    AgentPermission? Permission,
    AgentAuditDecision Decision,
    AgentAuditOutcome Outcome,
    long PolicyRevision,
    Guid? ConnectionId,
    long DurationMilliseconds,
    int ItemCount,
    int OutputBytes)
{
    public const int LegacySchemaVersion = 1;
    public const int PreviousSchemaVersion = 2;
    public const int CurrentSchemaVersion = 3;
    public const int MaximumItems = 10_000;
    public const int MaximumOutputBytes = 256 * 1024;
    /// <summary>Bounded wall-clock interval in milliseconds. This covers the entire DateTimeOffset range.</summary>
    public const long MaximumDurationMilliseconds = 315_537_897_600_000L;
    public const int PreviousMaximumDurationMilliseconds = 24 * 60 * 60 * 1000;

    /// <summary>Legacy v1 has no namespace evidence; callers must treat this as unknown, not unrestricted.</summary>
    public AgentAuditNamespaceKind NamespaceKind { get; init; } = AgentAuditNamespaceKind.LegacyUnknown;
    public string? DatabaseName { get; init; }
    public string? CollectionName { get; init; }
    public string? NamespacePseudonym { get; init; }
    public AgentAuditDecisionReason DecisionReason { get; init; } = AgentAuditDecisionReason.LegacyUnknown;
    public AgentAuditApprovalState ApprovalState { get; init; } = AgentAuditApprovalState.LegacyUnknown;
    public Guid? ApprovalId { get; init; }
    public DateTimeOffset? ApprovedAtUtc { get; init; }
    public DateTimeOffset StartedAtUtc { get; init; }
    public DateTimeOffset? CompletedAtUtc { get; init; }

    public AgentAuditEvent Validate()
    {
        if (Id == Guid.Empty || PrincipalId == Guid.Empty || InvocationId == Guid.Empty ||
            SessionId == Guid.Empty || TurnId == Guid.Empty || ConnectionId == Guid.Empty ||
            (TurnId is not null && SessionId is null))
            throw new ArgumentException("Identificadores de auditoria inválidos.");
        if (SchemaVersion is not (LegacySchemaVersion or PreviousSchemaVersion or CurrentSchemaVersion) ||
            OccurredAtUtc == default || OccurredAtUtc.Offset != TimeSpan.Zero ||
            !Enum.IsDefined(Channel) || !Enum.IsDefined(Risk) ||
            (Permission is { } permission && !Enum.IsDefined(permission)) ||
            !Enum.IsDefined(Decision) || !Enum.IsDefined(Outcome) || PolicyRevision < 0)
            throw new ArgumentException("Metadados de auditoria inválidos.");
        if (Channel == AgentAuditChannel.Internal ? ExternalIdentifier is not null : !SafeIdentifier(ExternalIdentifier, 64))
            throw new ArgumentException("Identificador externo inválido.", nameof(ExternalIdentifier));
        if (!SafeIdentifier(ToolName, 96, toolName: true) || ToolVersion is < 1 or > 1000)
            throw new ArgumentException("Identificador da tool inválido.", nameof(ToolName));
        if (DurationMilliseconds is < 0 or > MaximumDurationMilliseconds ||
            ItemCount is < 0 or > MaximumItems || OutputBytes is < 0 or > MaximumOutputBytes)
            throw new ArgumentException("Contagens de auditoria fora dos limites.");
        if (SchemaVersion == LegacySchemaVersion)
        {
            if (NamespaceKind != AgentAuditNamespaceKind.LegacyUnknown || DatabaseName is not null ||
                CollectionName is not null || NamespacePseudonym is not null ||
                DecisionReason != AgentAuditDecisionReason.LegacyUnknown ||
                ApprovalState != AgentAuditApprovalState.LegacyUnknown || ApprovalId is not null ||
                ApprovedAtUtc is not null || StartedAtUtc != OccurredAtUtc ||
                CompletedAtUtc != (Outcome == AgentAuditOutcome.Intent ? null : OccurredAtUtc))
                throw new ArgumentException("Compatibilidade de auditoria v1 inválida.");
            return this;
        }
        if (SchemaVersion == PreviousSchemaVersion &&
            (DurationMilliseconds > PreviousMaximumDurationMilliseconds ||
             Outcome == AgentAuditOutcome.AuditIncomplete ||
             DecisionReason == AgentAuditDecisionReason.AuditRecovery))
            throw new ArgumentException("Evento incompatível com o schema v2.");

        if (!Enum.IsDefined(NamespaceKind) || NamespaceKind == AgentAuditNamespaceKind.LegacyUnknown ||
            !Enum.IsDefined(DecisionReason) || DecisionReason == AgentAuditDecisionReason.LegacyUnknown ||
            !Enum.IsDefined(ApprovalState) || ApprovalState == AgentAuditApprovalState.LegacyUnknown)
            throw new ArgumentException("Metadados de auditoria v2 inválidos.");
        if (Outcome == AgentAuditOutcome.Intent ? DecisionReason != AgentAuditDecisionReason.NotEvaluated :
            DecisionReason == AgentAuditDecisionReason.NotEvaluated)
            throw new ArgumentException("Motivo incompatível com o estágio da auditoria.");
        // The authorization policy collection is keyed by PrincipalId; revision zero means no stored policy.
        if (PolicyRevision == 0 && Decision is (AgentAuditDecision.Allowed or AgentAuditDecision.ApprovedOnce))
            throw new ArgumentException("Decisão permitida exige política persistida.");
        var validNamespace = NamespaceKind switch
        {
            AgentAuditNamespaceKind.None => DatabaseName is null && CollectionName is null && NamespacePseudonym is null,
            AgentAuditNamespaceKind.Explicit => SafeNamespaceName(DatabaseName, 120) &&
                (CollectionName is null || SafeNamespaceName(CollectionName, 120)) && NamespacePseudonym is null,
            AgentAuditNamespaceKind.Pseudonym => DatabaseName is null && CollectionName is null &&
                SafeIdentifier(NamespacePseudonym, 64),
            _ => false
        };
        if (!validNamespace) throw new ArgumentException("Namespace de auditoria inválido.");
        if (StartedAtUtc == default || StartedAtUtc.Offset != TimeSpan.Zero || StartedAtUtc > OccurredAtUtc)
            throw new ArgumentException("Início de auditoria inválido.");
        if (Outcome == AgentAuditOutcome.Intent)
        {
            if (OccurredAtUtc != StartedAtUtc || CompletedAtUtc is not null || DurationMilliseconds != 0)
                throw new ArgumentException("Intenção de auditoria exige apenas horário de início.");
        }
        else
        {
            var elapsedTicks = (OccurredAtUtc - StartedAtUtc).Ticks;
            if (CompletedAtUtc != OccurredAtUtc || CompletedAtUtc?.Offset != TimeSpan.Zero ||
                DurationMilliseconds != (elapsedTicks + TimeSpan.TicksPerMillisecond - 1) /
                    TimeSpan.TicksPerMillisecond)
                throw new ArgumentException("Desfecho de auditoria exige início, fim e duração total coerentes.");
        }
        if (ApprovalId == Guid.Empty) throw new ArgumentException("Aprovação inválida.");
        switch (ApprovalState)
        {
            case AgentAuditApprovalState.NotRequired when ApprovalId is null && ApprovedAtUtc is null:
                break;
            case AgentAuditApprovalState.Pending when ApprovalId is not null && ApprovedAtUtc is null &&
                (Outcome == AgentAuditOutcome.Intent ||
                 (Outcome is (AgentAuditOutcome.Cancelled or AgentAuditOutcome.Failed or AgentAuditOutcome.Denied) &&
                  Decision is (AgentAuditDecision.Requested or AgentAuditDecision.Denied))):
                break;
            case AgentAuditApprovalState.ApprovedOnce when ApprovalId is not null &&
                ApprovedAtUtc is { Offset: var offset } && offset == TimeSpan.Zero &&
                ApprovedAtUtc >= StartedAtUtc && ApprovedAtUtc <= OccurredAtUtc &&
                Outcome != AgentAuditOutcome.Intent:
                break;
            case AgentAuditApprovalState.Rejected or AgentAuditApprovalState.Expired when ApprovalId is not null &&
                ApprovedAtUtc is null && Outcome == AgentAuditOutcome.Denied:
                break;
            default:
                throw new ArgumentException("Estado de aprovação incompatível com o evento.");
        }
        if (Decision == AgentAuditDecision.ApprovedOnce && ApprovalState != AgentAuditApprovalState.ApprovedOnce ||
            Decision == AgentAuditDecision.Rejected && ApprovalState != AgentAuditApprovalState.Rejected ||
            ApprovalState == AgentAuditApprovalState.Rejected &&
            (Decision != AgentAuditDecision.Rejected || DecisionReason != AgentAuditDecisionReason.ApprovalRejected) ||
            ApprovalState == AgentAuditApprovalState.Expired &&
            (Decision != AgentAuditDecision.Denied || DecisionReason != AgentAuditDecisionReason.ApprovalExpired) ||
            DecisionReason == AgentAuditDecisionReason.ApprovalRejected &&
            ApprovalState != AgentAuditApprovalState.Rejected ||
            DecisionReason == AgentAuditDecisionReason.ApprovalExpired &&
            ApprovalState != AgentAuditApprovalState.Expired ||
            Outcome == AgentAuditOutcome.Intent && Decision != AgentAuditDecision.Requested ||
            Risk != AgentToolRisk.ReadOnly && Outcome == AgentAuditOutcome.Intent &&
            ApprovalState != AgentAuditApprovalState.Pending ||
            Outcome != AgentAuditOutcome.Intent && !ValidTerminalDecision())
            throw new ArgumentException("Decisão, risco ou aprovação incompatíveis.");
        return this;
    }

    private bool ValidTerminalDecision()
    {
        if (Decision is (AgentAuditDecision.Allowed or AgentAuditDecision.ApprovedOnce) && PolicyRevision == 0)
            return false;
        if (Risk != AgentToolRisk.ReadOnly && Decision == AgentAuditDecision.Allowed)
            return false;
        if (Decision == AgentAuditDecision.Allowed && ApprovalState != AgentAuditApprovalState.NotRequired)
            return false;
        if (Outcome == AgentAuditOutcome.AuditIncomplete)
            return SchemaVersion == CurrentSchemaVersion && Risk == AgentToolRisk.ReadOnly &&
                Decision == AgentAuditDecision.Requested &&
                DecisionReason == AgentAuditDecisionReason.AuditRecovery &&
                ApprovalState == AgentAuditApprovalState.NotRequired;
        if (Decision == AgentAuditDecision.Requested &&
            (ApprovalState != AgentAuditApprovalState.Pending || Outcome is not (AgentAuditOutcome.Cancelled or AgentAuditOutcome.Failed)))
            return false;
        if (Decision == AgentAuditDecision.Denied && ApprovalState == AgentAuditApprovalState.ApprovedOnce &&
            Outcome != AgentAuditOutcome.Denied)
            return false;
        if (Decision == AgentAuditDecision.Rejected &&
            (Outcome != AgentAuditOutcome.Denied || DecisionReason != AgentAuditDecisionReason.ApprovalRejected))
            return false;
        return Outcome switch
        {
            AgentAuditOutcome.Succeeded => Decision switch
            {
                AgentAuditDecision.Allowed => DecisionReason == AgentAuditDecisionReason.PolicyAllowed,
                AgentAuditDecision.ApprovedOnce => DecisionReason == AgentAuditDecisionReason.ApprovalGranted,
                _ => false
            },
            AgentAuditOutcome.Uncertain => Risk != AgentToolRisk.ReadOnly &&
                Decision == AgentAuditDecision.ApprovedOnce &&
                DecisionReason == AgentAuditDecisionReason.OutcomeUncertain,
            AgentAuditOutcome.Denied => Decision is (AgentAuditDecision.Denied or AgentAuditDecision.Rejected) &&
                DecisionReason is (AgentAuditDecisionReason.PolicyMissing or AgentAuditDecisionReason.PermissionMissing or
                    AgentAuditDecisionReason.ApprovalRejected or AgentAuditDecisionReason.ValidationRejected or
                    AgentAuditDecisionReason.ApprovalExpired or AgentAuditDecisionReason.LimitExceeded or
                    AgentAuditDecisionReason.PolicyRevisionMismatch or AgentAuditDecisionReason.PolicyUnavailable),
            AgentAuditOutcome.Cancelled => DecisionReason == AgentAuditDecisionReason.Cancelled &&
                Decision is (AgentAuditDecision.Requested or AgentAuditDecision.Denied or
                    AgentAuditDecision.Allowed or AgentAuditDecision.ApprovedOnce),
            AgentAuditOutcome.Failed => DecisionReason is (AgentAuditDecisionReason.ExecutionFailed or
                    AgentAuditDecisionReason.ValidationRejected or AgentAuditDecisionReason.LimitExceeded) &&
                Decision is (AgentAuditDecision.Requested or AgentAuditDecision.Denied or
                    AgentAuditDecision.Allowed or AgentAuditDecision.ApprovedOnce),
            _ => false
        };
    }

    private static bool SafeIdentifier(string? value, int maximum, bool toolName = false) =>
        value is { Length: > 0 } && value.Length <= maximum &&
        (char.IsAsciiLetterLower(value[0]) || char.IsAsciiDigit(value[0])) &&
        value.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '_' ||
            (!toolName && (c == '.' || c == '-')));

    private static bool SafeNamespaceName(string? value, int maximum) =>
        value is { Length: > 0 } && value.Length <= maximum &&
        System.Text.Encoding.UTF8.GetByteCount(value) <= 255 &&
        !value.Contains("://", StringComparison.Ordinal) &&
        value.All(c => !char.IsControl(c) && c is not ('/' or '\\' or '@'));
}

public enum AgentAuditChannel { Internal = 0, ProviderExternal = 1, McpExternal = 2 }
public enum AgentAuditDecision { Requested = 0, Allowed = 1, Denied = 2, ApprovedOnce = 3, Rejected = 4 }
public enum AgentAuditOutcome { Intent = 0, Succeeded = 1, Denied = 2, Cancelled = 3, Failed = 4, Uncertain = 5, AuditIncomplete = 6 }
public enum AgentAuditNamespaceKind { LegacyUnknown = 0, None = 1, Explicit = 2, Pseudonym = 3 }
public enum AgentAuditDecisionReason
{
    LegacyUnknown = 0, PolicyAllowed = 1, PolicyMissing = 2, PermissionMissing = 3,
    ApprovalRequired = 4, ApprovalGranted = 5, ApprovalRejected = 6,
    ValidationRejected = 7, LimitExceeded = 8, Cancelled = 9, ExecutionFailed = 10,
    OutcomeUncertain = 11, NotEvaluated = 12, ApprovalExpired = 13, AuditRecovery = 14,
    PolicyRevisionMismatch = 15, PolicyUnavailable = 16
}
public enum AgentAuditApprovalState { LegacyUnknown = 0, NotRequired = 1, Pending = 2, ApprovedOnce = 3, Rejected = 4, Expired = 5 }
