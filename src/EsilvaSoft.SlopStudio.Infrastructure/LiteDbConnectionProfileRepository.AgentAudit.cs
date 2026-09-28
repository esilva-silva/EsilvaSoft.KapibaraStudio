using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Core;
using LiteDB;

namespace EsilvaSoft.SlopStudio.Infrastructure;

/// <summary>Agent audit facet of the one workspace owner. Local durability is per LiteDB transaction, not tamper proof.</summary>
public sealed partial class LiteDbConnectionProfileRepository
{
    private const string AgentAuditCollectionName = "agentAuditEvents";
    private const int MaximumStoredAgentAuditEvents = 10_000;

    /// <summary>Share of <see cref="MaximumStoredAgentAuditEvents"/> that external MCP reads may occupy.</summary>
    private const int MaximumStoredExternalReadAuditEvents = 5_000;
    private const int MaximumReadOnlyRecoveryBatch = 64;
    private static readonly TimeSpan AgentAuditRetention = TimeSpan.FromDays(30);
    private static readonly TimeSpan ReadOnlyRecoveryAge = TimeSpan.FromHours(1);

    Task IAgentAuditRepository.AppendAsync(AgentAuditEvent entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        entry.Validate();
        if (entry.SchemaVersion != AgentAuditEvent.CurrentSchemaVersion)
            throw new ArgumentException("Novos eventos exigem o schema de auditoria atual.", nameof(entry));
        return RunAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var now = DateTimeOffset.UtcNow;
            if (entry.OccurredAtUtc < now - AgentAuditRetention || entry.OccurredAtUtc > now.AddMinutes(5))
                throw new ArgumentException("Instante de auditoria fora da janela permitida.", nameof(entry));

            var collection = _database.GetCollection(AgentAuditCollectionName);
            // Decode every existing record before modifying storage. Unknown versions and corrupt data fail closed.
            var existing = collection.FindAll().Select(DecodeAgentAudit).ToArray();
            ValidateStoredAgentAuditSequence(existing);
            if (existing.Any(item => item.Id == entry.Id))
                throw new InvalidOperationException("Evento de auditoria duplicado.");

            var terminalIds = existing.Where(item => item.Outcome != AgentAuditOutcome.Intent)
                .Select(item => item.InvocationId).ToHashSet();
            var recovered = existing.Where(item => item.Outcome == AgentAuditOutcome.Intent &&
                    item.SchemaVersion >= AgentAuditEvent.PreviousSchemaVersion &&
                    item.Risk == AgentToolRisk.ReadOnly &&
                    item.ApprovalState == AgentAuditApprovalState.NotRequired &&
                    !terminalIds.Contains(item.InvocationId) &&
                    item.InvocationId != entry.InvocationId && item.OccurredAtUtc <= now - ReadOnlyRecoveryAge)
                .OrderBy(item => item.OccurredAtUtc).Take(MaximumReadOnlyRecoveryBatch)
                .Select(item => CreateIncompleteReadAudit(item, now)).ToArray();
            var recoveredIds = recovered.Select(item => item.InvocationId).ToHashSet();

            var prior = existing.Where(item => item.InvocationId == entry.InvocationId).ToArray();
            if (entry.Outcome == AgentAuditOutcome.Intent)
            {
                // An invocation ID is never recycled. A terminal-only record cannot validate a later intent.
                if (prior.Length != 0)
                    throw new InvalidOperationException("A chamada já possui evento de auditoria.");
            }
            else
            {
                if (prior.Any(item => item.Outcome != AgentAuditOutcome.Intent))
                    throw new InvalidOperationException("A chamada já possui desfecho de auditoria.");
                if (prior.Length == 0 && (entry.Outcome == AgentAuditOutcome.AuditIncomplete ||
                    entry.Risk != AgentToolRisk.ReadOnly && entry.Outcome != AgentAuditOutcome.Denied))
                    throw new InvalidOperationException("Desfecho exige intenção auditada.");
                if (prior.Length == 1 && !MatchesAgentAuditIntent(prior[0], entry))
                    throw new InvalidOperationException("O desfecho não corresponde à intenção auditada.");
            }

            var all = existing.Concat(recovered).Append(entry).ToArray();
            var unresolved = all.Where(item => item.Outcome == AgentAuditOutcome.Intent)
                .Select(item => item.InvocationId)
                .Except(all.Where(item => item.Outcome != AgentAuditOutcome.Intent).Select(item => item.InvocationId))
                .ToHashSet();
            // Reserve one terminal slot for each pending intent. Otherwise a full ledger of intents could
            // make it impossible to record their outcomes without evicting unresolved evidence.
            if (entry.Outcome == AgentAuditOutcome.Intent && unresolved.Count > MaximumStoredAgentAuditEvents / 2)
                throw new IOException("Limite de intenções de auditoria pendentes atingido.");
            var cutoff = now - AgentAuditRetention;
            var removableGroups = existing.GroupBy(item => item.InvocationId)
                .Where(group => group.Key != entry.InvocationId && !recoveredIds.Contains(group.Key) &&
                    !unresolved.Contains(group.Key))
                .Select(group => group.ToArray()).ToArray();
            var deletions = removableGroups.Where(group => group.All(item => item.OccurredAtUtc < cutoff))
                .SelectMany(group => group).Select(item => item.Id).ToHashSet();

            // Count-based rotation never evicts write evidence (only the 30-day retention does), and external MCP
            // reads rotate inside their own quota first: a flood from an MCP client can only push out older MCP reads,
            // never the internal chat's reads or any write/approval record.
            var rotatable = removableGroups.Where(group => group.All(item => item.Risk == AgentToolRisk.ReadOnly))
                .OrderBy(group => IsExternalMcpAudit(group[0]) ? 0 : 1)
                .ThenBy(group => group.Min(item => item.OccurredAtUtc))
                .ToArray();
            if (IsExternalMcpAudit(entry))
            {
                var externalExcess = existing.Count(item => IsExternalMcpAudit(item) && !deletions.Contains(item.Id)) +
                    recovered.Count(IsExternalMcpAudit) + 1 - MaximumStoredExternalReadAuditEvents;
                foreach (var group in rotatable.Where(group => IsExternalMcpAudit(group[0])))
                {
                    if (externalExcess <= 0) break;
                    if (deletions.Contains(group[0].Id)) continue;
                    foreach (var candidate in group) deletions.Add(candidate.Id);
                    externalExcess -= group.Length;
                }
                if (externalExcess > 0)
                    throw new IOException("Cota de auditoria de leituras MCP atingida com intenções pendentes.");
            }

            var excess = existing.Length - deletions.Count + recovered.Length + 1 - MaximumStoredAgentAuditEvents;
            if (excess > 0)
            {
                // Remove a resolved invocation as a unit. Never manufacture an orphan terminal or pending
                // intent by pruning only one member of its pair.
                foreach (var group in rotatable)
                {
                    if (excess <= 0) break;
                    if (deletions.Contains(group[0].Id)) continue;
                    foreach (var candidate in group) deletions.Add(candidate.Id);
                    excess -= group.Length;
                }
                if (excess > 0)
                    throw new IOException("Limite de auditoria atingido com intenções ou escritas protegidas.");
            }

            InTransaction(() =>
            {
                foreach (var id in deletions) collection.Delete(id);
                foreach (var recoveredEvent in recovered) collection.Insert(EncodeAgentAudit(recoveredEvent));
                collection.Insert(EncodeAgentAudit(entry));
            });
        }, cancellationToken);
    }

    Task<IReadOnlyList<AgentAuditEvent>> IAgentAuditRepository.GetRecentAsync(int maximum, CancellationToken cancellationToken)
    {
        if (maximum is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(maximum));
        return RunAsync(() =>
        {
            // A corrupt/unsupported record anywhere in this bounded ledger blocks reads, including recent reads.
            var stored = _database.GetCollection(AgentAuditCollectionName).FindAll()
                .Select(DecodeAgentAudit).ToArray();
            ValidateStoredAgentAuditSequence(stored);
            var events = stored.OrderByDescending(item => item.OccurredAtUtc).Take(maximum).ToArray();
            return (IReadOnlyList<AgentAuditEvent>)events;
        }, cancellationToken);
    }

    Task<IReadOnlyList<AgentAuditEvent>> IAgentAuditRepository.GetPendingAsync(int maximum, CancellationToken cancellationToken)
    {
        if (maximum is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(maximum));
        return RunAsync(() =>
        {
            var stored = _database.GetCollection(AgentAuditCollectionName).FindAll()
                .Select(DecodeAgentAudit).ToArray();
            ValidateStoredAgentAuditSequence(stored);
            var terminals = stored.Where(item => item.Outcome != AgentAuditOutcome.Intent)
                .Select(item => item.InvocationId).ToHashSet();
            return (IReadOnlyList<AgentAuditEvent>)stored
                .Where(item => item.Outcome == AgentAuditOutcome.Intent && !terminals.Contains(item.InvocationId))
                .OrderBy(item => item.OccurredAtUtc).Take(maximum).ToArray();
        }, cancellationToken);
    }

    /// <summary>Read evidence produced by an external MCP client; it rotates inside its own quota.</summary>
    private static bool IsExternalMcpAudit(AgentAuditEvent item) =>
        item.Channel == AgentAuditChannel.McpExternal && item.Risk == AgentToolRisk.ReadOnly;

    private static AgentAuditEvent CreateIncompleteReadAudit(AgentAuditEvent intent, DateTimeOffset now)
    {
        var elapsedTicks = (now - intent.StartedAtUtc).Ticks;
        return (intent with
        {
            Id = Guid.NewGuid(), SchemaVersion = AgentAuditEvent.CurrentSchemaVersion,
            OccurredAtUtc = now, CompletedAtUtc = now,
            DurationMilliseconds = (elapsedTicks + TimeSpan.TicksPerMillisecond - 1) / TimeSpan.TicksPerMillisecond,
            Outcome = AgentAuditOutcome.AuditIncomplete,
            DecisionReason = AgentAuditDecisionReason.AuditRecovery,
            ItemCount = 0, OutputBytes = 0
        }).Validate();
    }

    private static void ValidateStoredAgentAuditSequence(IReadOnlyList<AgentAuditEvent> events)
    {
        foreach (var group in events.GroupBy(item => item.InvocationId))
        {
            var intents = group.Where(item => item.Outcome == AgentAuditOutcome.Intent).ToArray();
            var terminals = group.Where(item => item.Outcome != AgentAuditOutcome.Intent).ToArray();
            if (intents.Length > 1 || terminals.Length > 1 ||
                (intents.Length == 0 && terminals.Length == 1 &&
                 (terminals[0].Outcome == AgentAuditOutcome.AuditIncomplete ||
                  terminals[0].Risk != AgentToolRisk.ReadOnly && terminals[0].Outcome != AgentAuditOutcome.Denied)) ||
                (intents.Length == 1 && terminals.Length == 1 && !MatchesAgentAuditIntent(intents[0], terminals[0])))
                throw InvalidAgentAudit();
        }
    }

    private static bool MatchesAgentAuditIntent(AgentAuditEvent intent, AgentAuditEvent terminal) =>
        intent.Outcome == AgentAuditOutcome.Intent && terminal.Outcome != AgentAuditOutcome.Intent &&
        terminal.OccurredAtUtc >= intent.OccurredAtUtc &&
        terminal.PrincipalId == intent.PrincipalId && terminal.SessionId == intent.SessionId &&
        terminal.TurnId == intent.TurnId && terminal.Channel == intent.Channel &&
        StringComparer.Ordinal.Equals(terminal.ExternalIdentifier, intent.ExternalIdentifier) &&
        StringComparer.Ordinal.Equals(terminal.ToolName, intent.ToolName) &&
        terminal.ToolVersion == intent.ToolVersion && terminal.Risk == intent.Risk &&
        terminal.Permission == intent.Permission && terminal.PolicyRevision == intent.PolicyRevision &&
        terminal.ConnectionId == intent.ConnectionId &&
        (terminal.SchemaVersion == intent.SchemaVersion ||
         intent.SchemaVersion == AgentAuditEvent.PreviousSchemaVersion &&
         terminal.SchemaVersion == AgentAuditEvent.CurrentSchemaVersion) &&
        (intent.SchemaVersion == AgentAuditEvent.LegacySchemaVersion ||
         (terminal.StartedAtUtc == intent.StartedAtUtc && terminal.NamespaceKind == intent.NamespaceKind &&
          StringComparer.Ordinal.Equals(terminal.DatabaseName, intent.DatabaseName) &&
          StringComparer.Ordinal.Equals(terminal.CollectionName, intent.CollectionName) &&
          StringComparer.Ordinal.Equals(terminal.NamespacePseudonym, intent.NamespacePseudonym) &&
          terminal.ApprovalId == intent.ApprovalId));

    private static BsonDocument EncodeAgentAudit(AgentAuditEvent entry) => new()
    {
        ["_id"] = entry.Id,
        ["schemaVersion"] = entry.SchemaVersion,
        ["occurredAtUtcTicks"] = entry.OccurredAtUtc.UtcDateTime.Ticks,
        ["principalId"] = entry.PrincipalId,
        ["invocationId"] = entry.InvocationId,
        ["sessionId"] = entry.SessionId is { } session ? new BsonValue(session) : BsonValue.Null,
        ["turnId"] = entry.TurnId is { } turn ? new BsonValue(turn) : BsonValue.Null,
        ["channel"] = (int)entry.Channel,
        ["externalIdentifier"] = entry.ExternalIdentifier is { } identifier ? new BsonValue(identifier) : BsonValue.Null,
        ["toolName"] = entry.ToolName,
        ["toolVersion"] = entry.ToolVersion,
        ["risk"] = (int)entry.Risk,
        ["permission"] = entry.Permission is { } permission ? new BsonValue((int)permission) : BsonValue.Null,
        ["decision"] = (int)entry.Decision,
        ["outcome"] = (int)entry.Outcome,
        ["policyRevision"] = entry.PolicyRevision,
        ["connectionId"] = entry.ConnectionId is { } connection ? new BsonValue(connection) : BsonValue.Null,
        ["durationMilliseconds"] = entry.DurationMilliseconds,
        ["itemCount"] = entry.ItemCount,
        ["outputBytes"] = entry.OutputBytes,
        ["namespaceKind"] = (int)entry.NamespaceKind,
        ["databaseName"] = entry.DatabaseName is { } database ? new BsonValue(database) : BsonValue.Null,
        ["collectionName"] = entry.CollectionName is { } collection ? new BsonValue(collection) : BsonValue.Null,
        ["namespacePseudonym"] = entry.NamespacePseudonym is { } pseudonym ? new BsonValue(pseudonym) : BsonValue.Null,
        ["decisionReason"] = (int)entry.DecisionReason,
        ["approvalState"] = (int)entry.ApprovalState,
        ["approvalId"] = entry.ApprovalId is { } approval ? new BsonValue(approval) : BsonValue.Null,
        ["approvedAtUtcTicks"] = entry.ApprovedAtUtc is { } approvedAt ? new BsonValue(approvedAt.UtcDateTime.Ticks) : BsonValue.Null,
        ["startedAtUtcTicks"] = entry.StartedAtUtc.UtcDateTime.Ticks,
        ["completedAtUtcTicks"] = entry.CompletedAtUtc is { } completedAt ? new BsonValue(completedAt.UtcDateTime.Ticks) : BsonValue.Null
    };

    private static AgentAuditEvent DecodeAgentAudit(BsonDocument document)
    {
        try
        {
            string[] legacyFields = ["_id", "schemaVersion", "occurredAtUtcTicks", "principalId", "invocationId",
                "sessionId", "turnId", "channel", "externalIdentifier", "toolName", "toolVersion", "risk",
                "permission", "decision", "outcome", "policyRevision", "connectionId", "durationMilliseconds",
                "itemCount", "outputBytes"];
            if (!document.TryGetValue("schemaVersion", out var schemaValue) || !schemaValue.IsInt32 ||
                schemaValue.AsInt32 is not (AgentAuditEvent.LegacySchemaVersion or
                    AgentAuditEvent.PreviousSchemaVersion or AgentAuditEvent.CurrentSchemaVersion))
                throw InvalidAgentAudit();
            var version = schemaValue.AsInt32;
            string[] v2Fields = [.. legacyFields, "namespaceKind", "databaseName", "collectionName",
                "namespacePseudonym", "decisionReason", "approvalState", "approvalId", "approvedAtUtcTicks",
                "startedAtUtcTicks", "completedAtUtcTicks"];
            var fields = version == AgentAuditEvent.LegacySchemaVersion ? legacyFields : v2Fields;
            if (document.Count != fields.Length || !document.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(fields))
                throw InvalidAgentAudit();
            var occurredAt = AuditDateTime(document, "occurredAtUtcTicks");
            var outcome = AuditEnum<AgentAuditOutcome>(document, "outcome");
            var entry = new AgentAuditEvent(
                AuditGuid(document, "_id"), version, occurredAt,
                AuditGuid(document, "principalId"), AuditGuid(document, "invocationId"),
                AuditNullableGuid(document, "sessionId"), AuditNullableGuid(document, "turnId"),
                AuditEnum<AgentAuditChannel>(document, "channel"), AuditNullableString(document, "externalIdentifier"),
                AuditString(document, "toolName"), AuditInt(document, "toolVersion"),
                AuditEnum<AgentToolRisk>(document, "risk"),
                document["permission"].IsNull ? null : AuditEnum<AgentPermission>(document, "permission"),
                AuditEnum<AgentAuditDecision>(document, "decision"), outcome,
                AuditLong(document, "policyRevision"), AuditNullableGuid(document, "connectionId"),
                version == AgentAuditEvent.CurrentSchemaVersion
                    ? AuditLong(document, "durationMilliseconds") : AuditInt(document, "durationMilliseconds"),
                AuditInt(document, "itemCount"), AuditInt(document, "outputBytes"))
            {
                NamespaceKind = version == AgentAuditEvent.LegacySchemaVersion
                    ? AgentAuditNamespaceKind.LegacyUnknown : AuditEnum<AgentAuditNamespaceKind>(document, "namespaceKind"),
                DatabaseName = version == AgentAuditEvent.LegacySchemaVersion ? null : AuditNullableString(document, "databaseName"),
                CollectionName = version == AgentAuditEvent.LegacySchemaVersion ? null : AuditNullableString(document, "collectionName"),
                NamespacePseudonym = version == AgentAuditEvent.LegacySchemaVersion ? null : AuditNullableString(document, "namespacePseudonym"),
                DecisionReason = version == AgentAuditEvent.LegacySchemaVersion
                    ? AgentAuditDecisionReason.LegacyUnknown : AuditEnum<AgentAuditDecisionReason>(document, "decisionReason"),
                ApprovalState = version == AgentAuditEvent.LegacySchemaVersion
                    ? AgentAuditApprovalState.LegacyUnknown : AuditEnum<AgentAuditApprovalState>(document, "approvalState"),
                ApprovalId = version == AgentAuditEvent.LegacySchemaVersion ? null : AuditNullableGuid(document, "approvalId"),
                ApprovedAtUtc = version == AgentAuditEvent.LegacySchemaVersion ? null : AuditNullableDateTime(document, "approvedAtUtcTicks"),
                StartedAtUtc = version == AgentAuditEvent.LegacySchemaVersion ? occurredAt : AuditDateTime(document, "startedAtUtcTicks"),
                CompletedAtUtc = version == AgentAuditEvent.LegacySchemaVersion
                    ? outcome == AgentAuditOutcome.Intent ? null : occurredAt
                    : AuditNullableDateTime(document, "completedAtUtcTicks")
            };
            return entry.Validate();
        }
        catch (ArgumentException)
        {
            throw InvalidAgentAudit();
        }
    }

    private static Guid AuditGuid(BsonDocument d, string key) =>
        d[key].IsGuid && d[key].AsGuid != Guid.Empty ? d[key].AsGuid : throw InvalidAgentAudit();
    private static Guid? AuditNullableGuid(BsonDocument d, string key) =>
        d[key].IsNull ? null : AuditGuid(d, key);
    private static string AuditString(BsonDocument d, string key) =>
        d[key].IsString ? d[key].AsString : throw InvalidAgentAudit();
    private static string? AuditNullableString(BsonDocument d, string key) =>
        d[key].IsNull ? null : AuditString(d, key);
    private static int AuditInt(BsonDocument d, string key) =>
        d[key].IsInt32 ? d[key].AsInt32 : throw InvalidAgentAudit();
    private static long AuditLong(BsonDocument d, string key) =>
        d[key].IsInt64 ? d[key].AsInt64 : throw InvalidAgentAudit();
    private static DateTimeOffset AuditDateTime(BsonDocument d, string key) =>
        new(new DateTime(AuditLong(d, key), DateTimeKind.Utc));
    private static DateTimeOffset? AuditNullableDateTime(BsonDocument d, string key) =>
        d[key].IsNull ? null : AuditDateTime(d, key);
    private static T AuditEnum<T>(BsonDocument d, string key) where T : struct, Enum
    {
        var raw = AuditInt(d, key);
        var value = (T)Enum.ToObject(typeof(T), raw);
        return Enum.IsDefined(value) ? value : throw InvalidAgentAudit();
    }
    private static InvalidDataException InvalidAgentAudit() =>
        new("Auditoria de agentes ilegível ou com versão não suportada. O documento foi preservado.");
}
