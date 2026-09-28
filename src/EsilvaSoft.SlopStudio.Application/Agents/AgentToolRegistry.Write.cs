using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Core.Agents;

namespace EsilvaSoft.SlopStudio.Application.Agents;

/// <summary>
/// Lote 10 unitary writes and index tools. Pipeline (doc 08): closed arguments and literal EJSON; internal principal
/// only; durable intent before anything else (ledger failure denies); authorization and before-state preview without
/// executing; immutable proposal bound to an operation hash; human approval through the approval authority; right
/// before dispatch, revalidation of principal, policy revision, profile/generation, read-only flag, namespace grant and
/// operation hash, then atomic single-use consumption; one send, never retried; uncertain outcome reported as
/// <c>OutcomeUnknown</c> without any rollback claim; revalidation again before releasing the result.
/// </summary>
public sealed partial class AgentToolRegistry
{
    public const string InsertOneToolName = "insert_one";
    public const string UpdateOneToolName = "update_one";
    public const string DeleteOneToolName = "delete_one";
    public const string CreateIndexToolName = "create_index";
    public const string DropIndexToolName = "drop_index";

    private const string ApprovalRejected = "ApprovalRejected";
    private const string ApprovalExpired = "ApprovalExpired";
    private const string ApprovalUnavailable = "ApprovalUnavailable";
    private const string ApprovalInvalid = "ApprovalInvalid";
    private const string OutcomeUnknown = "OutcomeUnknown";
    private const string AppliedAuditPending = "AppliedAuditPending";
    private const string AppliedOutputWithheld = "AppliedOutputWithheld";
    private const int MaximumStateBytes = 64 * 1024;

    private const string WriteCommonProperties =
        "\"connectionId\":{\"type\":\"string\",\"format\":\"uuid\"},\"database\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":255},\"collection\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":255},\"maxTimeMs\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":30000,\"default\":5000}";

    private const string InsertOneInputSchema =
        "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"connectionId\",\"database\",\"collection\",\"documentEjson\"],\"properties\":{" +
        WriteCommonProperties + ",\"documentEjson\":{\"type\":\"string\",\"maxLength\":65536}}}";
    private const string UpdateOneInputSchema =
        "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"connectionId\",\"database\",\"collection\",\"idEjson\",\"updateEjson\"],\"properties\":{" +
        WriteCommonProperties + ",\"idEjson\":{\"type\":\"string\",\"maxLength\":65536},\"updateEjson\":{\"type\":\"string\",\"maxLength\":65536}}}";
    private const string DeleteOneInputSchema =
        "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"connectionId\",\"database\",\"collection\",\"idEjson\"],\"properties\":{" +
        WriteCommonProperties + ",\"idEjson\":{\"type\":\"string\",\"maxLength\":65536}}}";
    private const string CreateIndexInputSchema =
        "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"connectionId\",\"database\",\"collection\",\"keysEjson\"],\"properties\":{" +
        WriteCommonProperties + ",\"keysEjson\":{\"type\":\"string\",\"maxLength\":65536},\"name\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":128},\"unique\":{\"type\":\"boolean\",\"default\":false},\"sparse\":{\"type\":\"boolean\",\"default\":false}}}";
    private const string DropIndexInputSchema =
        "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"connectionId\",\"database\",\"collection\",\"indexName\"],\"properties\":{" +
        WriteCommonProperties + ",\"indexName\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":128}}}";
    private const string WriteOutputSchema = """
        {"type":"object","additionalProperties":false,"required":["status","affectedCount"],"properties":{"status":{"type":"string","const":"Applied"},"affectedCount":{"type":"integer","minimum":0,"maximum":1},"insertedIdEjson":{"type":"string"},"indexName":{"type":"string"}}}
        """;

    private static readonly HashSet<string> ProtectedDatabases = new(StringComparer.Ordinal) { "admin", "local", "config" };

    // One write per session at a time (doc 09), for the whole approval + execution flow.
    private readonly ConcurrentDictionary<Guid, byte> _activeWriteSessions = new();

    private static bool IsWriteTool(string? name) => AgentToolExposure.WriteReleaseOf(name) != AgentWriteToolRelease.None;

    private static AgentWriteOperationKind OperationOf(string name) => name switch
    {
        InsertOneToolName => AgentWriteOperationKind.InsertOne,
        UpdateOneToolName => AgentWriteOperationKind.UpdateOne,
        DeleteOneToolName => AgentWriteOperationKind.DeleteOne,
        CreateIndexToolName => AgentWriteOperationKind.CreateIndex,
        _ => AgentWriteOperationKind.DropIndex
    };

    private async Task<AgentToolInvocationResult> InvokeWriteAsync(
        AgentPrincipal? principal, AgentInvocationContext? context, AgentOutputDestination? destination,
        AgentOutputDataScope? outputScope, AgentToolDescriptor descriptor, string? argumentsJson,
        CancellationToken cancellationToken)
    {
        // Writes exist only for the in-process chat under human approval. The MCP ingress never writes in this
        // lote, whatever the client, its annotations or its declared permissions say.
        if (principal is null || !IsCompleteInvocationContext(context) || destination is null ||
            !IsValidDestination(destination, context!) || principal.Origin != AgentPrincipalOrigin.Internal ||
            destination.Kind == AgentOutputDestinationKind.McpExternal || context!.ClientId is not null ||
            _write is null || _writeApprovals is null)
            return AgentToolInvocationResult.Failure(PermissionDenied);
        cancellationToken.ThrowIfCancellationRequested();
        var sessionId = context.SessionId!.Value;
        if (!_activeWriteSessions.TryAdd(sessionId, 0)) return AgentToolInvocationResult.Failure(Busy);
        try
        {
            return await RunWriteAsync(principal, context, destination, outputScope, descriptor, argumentsJson,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _activeWriteSessions.TryRemove(sessionId, out _);
        }
    }

    private async Task<AgentToolInvocationResult> RunWriteAsync(
        AgentPrincipal principal, AgentInvocationContext context, AgentOutputDestination destination,
        AgentOutputDataScope? outputScope, AgentToolDescriptor descriptor, string? argumentsJson,
        CancellationToken cancellationToken)
    {
        var parsed = TryParseWriteArguments(descriptor.Name, argumentsJson, out var arguments);
        var approvalId = Guid.NewGuid();
        var intent = CreateWriteIntent(principal, context, destination, descriptor,
            parsed ? arguments!.ConnectionId : null, parsed, approvalId);
        // Durable intent before any profile, policy, source or human access. No ledger, no write.
        Task intentAppend;
        try
        {
            intentAppend = _audit.AppendAsync(intent, cancellationToken);
        }
        catch (Exception exception)
        {
            intentAppend = Task.FromException(exception); // a synchronous failure is handled like an asynchronous one
        }
        try
        {
            await AwaitWithCancellationAsync(intentAppend, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Cancelled (deadline or turn) while the intent was being appended: nothing can have been sent, but the
            // append may still land. Close it with a Cancelled terminal so it does not stay pending for reconciliation.
            await CloseCancelledIntentAsync(intent, intentAppend).ConfigureAwait(false);
            throw;
        }
        catch
        {
            return AgentToolInvocationResult.Failure(PermissionDenied);
        }

        var audit = new WriteAuditState(intent);
        try
        {
            if (!parsed)
                return await DenyWriteAsync(audit, InvalidArguments, AgentAuditDecisionReason.ValidationRejected)
                    .ConfigureAwait(false);
            if (outputScope is null || outputScope != AgentToolOutputScopes.For(descriptor.Name))
                return await DenyWriteAsync(audit, PermissionDenied, AgentAuditDecisionReason.PermissionMissing)
                    .ConfigureAwait(false);

            // Preflight (authorization + before-state preview) is bounded by the execution budget.
            WritePreparation preparation;
            using (var preflight = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                preflight.CancelAfter(_executionTimeout);
                try
                {
                    preparation = await PrepareWriteAsync(principal, context, destination, outputScope.Value,
                        descriptor, arguments!, approvalId, intent.InvocationId, preflight.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested &&
                                                         preflight.IsCancellationRequested)
                {
                    await audit.AppendAsync(this, AgentAuditOutcome.Cancelled, AgentAuditDecision.Requested,
                        AgentAuditDecisionReason.Cancelled).ConfigureAwait(false);
                    return AgentToolInvocationResult.Failure(DeadlineExceeded);
                }
            }
            if (preparation.Failure is { } preflightFailure)
                return await DenyWriteAsync(audit, preflightFailure.ErrorCode!,
                    preflightFailure.AuditReason ?? AgentAuditDecisionReason.PermissionMissing).ConfigureAwait(false);
            var frozen = preparation.Proposal!;

            // Human approval: separate budget owned by the approval authority; never inferred from arguments.
            AgentWriteApprovalGrant grant;
            try
            {
                grant = await _writeApprovals!.RequestApprovalAsync(frozen, cancellationToken).ConfigureAwait(false) ??
                    throw new InvalidOperationException();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch
            {
                return await DenyWriteAsync(audit, ApprovalUnavailable, AgentAuditDecisionReason.PolicyUnavailable)
                    .ConfigureAwait(false);
            }
            switch (grant.Verdict)
            {
                case AgentWriteApprovalVerdict.Granted when grant.Ticket is not null:
                    break;
                case AgentWriteApprovalVerdict.Rejected:
                    await audit.AppendAsync(this, AgentAuditOutcome.Denied, AgentAuditDecision.Rejected,
                        AgentAuditDecisionReason.ApprovalRejected, AgentAuditApprovalState.Rejected).ConfigureAwait(false);
                    return AgentToolInvocationResult.Failure(ApprovalRejected);
                case AgentWriteApprovalVerdict.Expired:
                    await audit.AppendAsync(this, AgentAuditOutcome.Denied, AgentAuditDecision.Denied,
                        AgentAuditDecisionReason.ApprovalExpired, AgentAuditApprovalState.Expired).ConfigureAwait(false);
                    return AgentToolInvocationResult.Failure(ApprovalExpired);
                case AgentWriteApprovalVerdict.Cancelled when cancellationToken.IsCancellationRequested:
                    throw new OperationCanceledException(cancellationToken);
                default:
                    return await DenyWriteAsync(audit, ApprovalUnavailable, AgentAuditDecisionReason.PolicyUnavailable)
                        .ConfigureAwait(false);
            }
            audit.MarkApproved();

            // Execution budget starts only after approval.
            using var execution = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            execution.CancelAfter(_executionTimeout);
            using var lease = _quota.TryEnter(context.SessionId!.Value, context.TurnId!.Value, frozen.ConnectionId,
                trackTurn: true, out var busy);
            if (lease is null)
            {
                _writeApprovals.TryConsume(grant.Ticket, null); // burn: a refused dispatch cannot reuse the ticket
                return await DenyWriteAsync(audit, busy ? Busy : PermissionDenied,
                    AgentAuditDecisionReason.LimitExceeded).ConfigureAwait(false);
            }
            var previousLease = _activeQuotaLease.Value;
            _activeQuotaLease.Value = lease;
            try
            {
                return await ExecuteApprovedWriteAsync(audit, principal, context, destination, outputScope.Value,
                    descriptor, arguments!, frozen, preparation.Profile!, grant.Ticket, execution, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                _activeQuotaLease.Value = previousLease;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Nothing was sent on this path (post-dispatch cancellation is handled as OutcomeUnknown below).
            await audit.AppendAsync(this, AgentAuditOutcome.Cancelled,
                audit.Approved ? AgentAuditDecision.ApprovedOnce : AgentAuditDecision.Requested,
                AgentAuditDecisionReason.Cancelled).ConfigureAwait(false);
            throw;
        }
        catch
        {
            await audit.AppendAsync(this, AgentAuditOutcome.Failed,
                audit.Approved ? AgentAuditDecision.ApprovedOnce : AgentAuditDecision.Denied,
                AgentAuditDecisionReason.ExecutionFailed).ConfigureAwait(false);
            return AgentToolInvocationResult.Failure(PermissionDenied);
        }
    }

    private async Task<AgentToolInvocationResult> ExecuteApprovedWriteAsync(
        WriteAuditState audit, AgentPrincipal principal, AgentInvocationContext context,
        AgentOutputDestination destination, AgentOutputDataScope outputScope, AgentToolDescriptor descriptor,
        WriteArguments arguments, AgentWriteProposal frozen, ConnectionProfile frozenProfile,
        AgentWriteApprovalTicket ticket, CancellationTokenSource execution, CancellationToken cancellationToken)
    {
        // Revalidate everything the approval depended on, immediately before the send.
        WritePreparation current;
        try
        {
            current = await RebuildProposalAsync(principal, context, destination, outputScope, descriptor, arguments,
                frozen, frozenProfile, execution.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested &&
                                                 execution.IsCancellationRequested)
        {
            _writeApprovals!.TryConsume(ticket, null);
            await audit.AppendAsync(this, AgentAuditOutcome.Cancelled, AgentAuditDecision.ApprovedOnce,
                AgentAuditDecisionReason.Cancelled).ConfigureAwait(false);
            return AgentToolInvocationResult.Failure(DeadlineExceeded);
        }
        catch
        {
            _writeApprovals!.TryConsume(ticket, null);
            throw;
        }
        if (current.Failure is { } revalidation)
        {
            _writeApprovals!.TryConsume(ticket, null);
            return await DenyWriteAsync(audit, revalidation.ErrorCode!,
                revalidation.AuditReason ?? AgentAuditDecisionReason.PermissionMissing).ConfigureAwait(false);
        }

        var consumption = _writeApprovals!.TryConsume(ticket, current.Proposal);
        if (consumption != AgentWriteApprovalConsumption.Consumed)
        {
            if (consumption == AgentWriteApprovalConsumption.Expired)
            {
                await audit.AppendAsync(this, AgentAuditOutcome.Denied, AgentAuditDecision.Denied,
                    AgentAuditDecisionReason.ApprovalExpired, AgentAuditApprovalState.Expired).ConfigureAwait(false);
                return AgentToolInvocationResult.Failure(ApprovalExpired);
            }
            return await DenyWriteAsync(audit, ApprovalInvalid, AgentAuditDecisionReason.ValidationRejected)
                .ConfigureAwait(false);
        }

        var proposal = current.Proposal!;
        var approval = new AgentMongoWriteApproval(proposal.ApprovalId, proposal.OperationHash);
        var maxTimeMs = Math.Min(proposal.MaxTimeMs, (int)Math.Clamp(_executionTimeout.TotalMilliseconds, 1, 30_000));
        // From here the command may reach the server: any failure or cancellation is an uncertain outcome. The write
        // is never retried and no rollback is claimed.
        AgentMongoWriteResult? result;
        try
        {
            result = await AwaitWithCancellationAsync(
                DispatchWriteAsync(current.Profile!, proposal, approval, maxTimeMs, execution.Token), execution.Token)
                .ConfigureAwait(false);
        }
        catch
        {
            result = null;
        }

        if (result is null || result.Status == AgentMongoWriteStatus.OutcomeUnknown || !Enum.IsDefined(result.Status) ||
            result.Status == AgentMongoWriteStatus.Applied && !IsValidAppliedResult(proposal, result))
        {
            await audit.AppendAsync(this, AgentAuditOutcome.Uncertain, AgentAuditDecision.ApprovedOnce,
                AgentAuditDecisionReason.OutcomeUncertain).ConfigureAwait(false);
            return AgentToolInvocationResult.Failure(OutcomeUnknown);
        }
        if (result.Status != AgentMongoWriteStatus.Applied)
        {
            var (code, reason) = result.Status switch
            {
                AgentMongoWriteStatus.Conflict => ("WriteConflict", AgentAuditDecisionReason.ExecutionFailed),
                AgentMongoWriteStatus.NotFound => ("NotFound", AgentAuditDecisionReason.ExecutionFailed),
                AgentMongoWriteStatus.InvalidRequest => ("WriteRejected", AgentAuditDecisionReason.ValidationRejected),
                AgentMongoWriteStatus.Forbidden => ("WriteForbidden", AgentAuditDecisionReason.ExecutionFailed),
                AgentMongoWriteStatus.PreconditionUnavailable =>
                    ("PreconditionUnavailable", AgentAuditDecisionReason.ExecutionFailed),
                _ => ("WriteNotSent", AgentAuditDecisionReason.ExecutionFailed)
            };
            await audit.AppendAsync(this, AgentAuditOutcome.Failed, AgentAuditDecision.ApprovedOnce, reason)
                .ConfigureAwait(false);
            return AgentToolInvocationResult.Failure(code);
        }

        var json = JsonSerializer.Serialize(new WriteResponse("Applied", (int)result.AffectedCount,
            proposal.Operation == AgentWriteOperationKind.InsertOne ? InsertedIdOf(proposal, result) : null,
            proposal.Operation == AgentWriteOperationKind.CreateIndex ? IndexNameOf(proposal, result) : null),
            SerializerOptions);
        // The database effect is never hidden: a lost terminal audit leaves the intent pending for reconciliation.
        if (!await audit.AppendAsync(this, AgentAuditOutcome.Succeeded, AgentAuditDecision.ApprovedOnce,
                AgentAuditDecisionReason.ApprovalGranted, itemCount: (int)result.AffectedCount,
                outputBytes: Utf8ByteCount(json)).ConfigureAwait(false))
            return AgentToolInvocationResult.Failure(AppliedAuditPending);

        // Release gate: a revocation during the write withholds the output (the effect stays reported as applied).
        WritePreparation releaseCheck;
        try
        {
            using var release = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            release.CancelAfter(TimeSpan.FromSeconds(5));
            releaseCheck = await RebuildProposalAsync(principal, context, destination, outputScope, descriptor,
                arguments, frozen, frozenProfile, release.Token).ConfigureAwait(false);
        }
        catch
        {
            return AgentToolInvocationResult.Failure(AppliedOutputWithheld);
        }
        if (releaseCheck.Failure is not null ||
            !string.Equals(releaseCheck.Proposal?.OperationHash, proposal.OperationHash, StringComparison.Ordinal))
            return AgentToolInvocationResult.Failure(AppliedOutputWithheld);
        return AgentToolInvocationResult.Success(json, current.Profile!);
    }

    private Task<AgentMongoWriteResult> DispatchWriteAsync(ConnectionProfile profile, AgentWriteProposal proposal,
        AgentMongoWriteApproval approval, int maxTimeMs, CancellationToken cancellationToken) => proposal.Operation switch
    {
        AgentWriteOperationKind.InsertOne => _write!.InsertOneAsync(profile,
            new AgentMongoInsertRequest(proposal.Database, proposal.Collection, proposal.PayloadEjson!, maxTimeMs)
            { SourceGenerationId = proposal.SourceGenerationId, Approval = approval }, cancellationToken),
        AgentWriteOperationKind.UpdateOne => _write!.UpdateOneAsync(profile,
            new AgentMongoUpdateRequest(proposal.Database, proposal.Collection, proposal.IdEjson!, proposal.PayloadEjson!,
                proposal.ExpectedStateHash!, maxTimeMs)
            {
                SourceGenerationId = proposal.SourceGenerationId, Approval = approval,
                ExpectedStateEjson = proposal.BeforeEjson
            }, cancellationToken),
        AgentWriteOperationKind.DeleteOne => _write!.DeleteOneAsync(profile,
            new AgentMongoDeleteRequest(proposal.Database, proposal.Collection, proposal.IdEjson!,
                proposal.ExpectedStateHash!, maxTimeMs)
            {
                SourceGenerationId = proposal.SourceGenerationId, Approval = approval,
                ExpectedStateEjson = proposal.BeforeEjson
            }, cancellationToken),
        AgentWriteOperationKind.CreateIndex => _write!.CreateIndexAsync(profile,
            new AgentMongoCreateIndexRequest(proposal.Database, proposal.Collection, proposal.PayloadEjson!,
                proposal.IndexName, proposal.Unique, proposal.Sparse, maxTimeMs)
            { SourceGenerationId = proposal.SourceGenerationId, Approval = approval }, cancellationToken),
        _ => _write!.DropIndexAsync(profile,
            new AgentMongoDropIndexRequest(proposal.Database, proposal.Collection, proposal.IndexName!,
                proposal.ExpectedStateHash!, maxTimeMs)
            { SourceGenerationId = proposal.SourceGenerationId, Approval = approval }, cancellationToken)
    };

    private static bool IsValidAppliedResult(AgentWriteProposal proposal, AgentMongoWriteResult result)
    {
        // Insert/delete touch exactly one document. Update may match without modifying; create index may be the
        // idempotent no-op of an identical existing index; drop reports at most the one index.
        var minimum = proposal.Operation is AgentWriteOperationKind.InsertOne or AgentWriteOperationKind.DeleteOne ? 1 : 0;
        return result.TargetVerified && result.AffectedCount >= minimum && result.AffectedCount <= 1 &&
            (result.ResultEjson is null || Utf8ByteCount(result.ResultEjson) <= MaximumStateBytes &&
                AgentToolLiteralEjson.IsLiteralIdentifier(result.ResultEjson));
    }

    private static string? InsertedIdOf(AgentWriteProposal proposal, AgentMongoWriteResult result) =>
        result.ResultEjson ?? proposal.IdEjson;

    private static string? IndexNameOf(AgentWriteProposal proposal, AgentMongoWriteResult result)
    {
        if (result.ResultEjson is null) return proposal.IndexName;
        try
        {
            using var document = JsonDocument.Parse(result.ResultEjson);
            return document.RootElement.ValueKind == JsonValueKind.String &&
                   IsWritableIndexName(document.RootElement.GetString())
                ? document.RootElement.GetString()
                : proposal.IndexName;
        }
        catch (JsonException) { return proposal.IndexName; }
    }

    private async Task<WritePreparation> PrepareWriteAsync(
        AgentPrincipal principal, AgentInvocationContext context, AgentOutputDestination destination,
        AgentOutputDataScope outputScope, AgentToolDescriptor descriptor, WriteArguments arguments, Guid approvalId,
        Guid invocationId, CancellationToken cancellationToken)
    {
        var authorized = await AuthorizeWriteAsync(principal, context, destination, outputScope, descriptor, arguments,
            cancellationToken).ConfigureAwait(false);
        if (authorized.Failure is not null) return authorized;
        var profile = authorized.Profile!;
        var operation = OperationOf(descriptor.Name);

        string? idEjson = arguments.IdEjson;
        string? payload = arguments.PayloadEjson;
        string? before = null;
        string? stateHash = null;
        if (operation == AgentWriteOperationKind.InsertOne)
        {
            // The _id is fixed before approval: the human approves the exact document and an uncertain outcome can be
            // reconciled by this identifier, without replay.
            (payload, idEjson) = WithFixedId(payload!);
        }
        else if (operation is AgentWriteOperationKind.UpdateOne or AgentWriteOperationKind.DeleteOne or
                 AgentWriteOperationKind.DropIndex)
        {
            var target = operation == AgentWriteOperationKind.DropIndex
                ? new AgentMongoWriteTarget(AgentMongoWriteTargetKind.Index, arguments.Database, arguments.Collection,
                    null, arguments.IndexName, EffectiveMaxTime(arguments))
                : new AgentMongoWriteTarget(AgentMongoWriteTargetKind.Document, arguments.Database,
                    arguments.Collection, arguments.IdEjson, null, EffectiveMaxTime(arguments));
            AgentMongoWriteSnapshot snapshot;
            try
            {
                snapshot = await AwaitWithCancellationAsync(_write!.ReadTargetAsync(profile,
                        target with { SourceGenerationId = profile.SourceGenerationId!.Value }, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch
            {
                cancellationToken.ThrowIfCancellationRequested();
                return WritePreparation.Deny(PermissionDenied, AgentAuditDecisionReason.ExecutionFailed);
            }
            if (snapshot is null || !snapshot.TargetVerified)
                return WritePreparation.Deny(PermissionDenied, AgentAuditDecisionReason.ValidationRejected);
            if (snapshot.ResultTooLarge)
                return WritePreparation.Deny(ResultTooLarge, AgentAuditDecisionReason.LimitExceeded);
            if (!snapshot.Exists)
                return WritePreparation.Deny("NotFound", AgentAuditDecisionReason.ValidationRejected);
            if (snapshot.StateEjson is not { } state || Utf8ByteCount(state) > MaximumStateBytes ||
                !IsLiteralEjsonDocument(state, rejectCode: false, maximumBytes: MaximumStateBytes) ||
                !IsStateHash(snapshot.StateHash))
                return WritePreparation.Deny(PermissionDenied, AgentAuditDecisionReason.ValidationRejected);
            before = state;
            stateHash = snapshot.StateHash;
        }

        // Nothing is released until the principal, policy and profile are still the ones authorized above.
        if (await RevalidateMetadataProfileAsync(profile, cancellationToken).ConfigureAwait(false) is { } changed)
            return WritePreparation.Deny(PermissionDenied, changed);
        var policy = await LoadCurrentPolicyAsync(principal, cancellationToken).ConfigureAwait(false);
        if (policy.Policy is null) return WritePreparation.Deny(PermissionDenied, policy.DenialReason);
        if (policy.Policy.Revision != authorized.PolicyRevision)
            return WritePreparation.Deny(PermissionDenied, AgentAuditDecisionReason.PolicyRevisionMismatch);

        var proposal = new AgentWriteProposal(operation, descriptor.Name, descriptor.Version, descriptor.Risk,
            descriptor.RequiredPermissions[0], principal.Id, principal.Origin, context.SessionId!.Value,
            context.TurnId!.Value, invocationId, approvalId, context.ProviderId, profile.Id,
            profile.SourceGenerationId!.Value, authorized.PolicyRevision, profile.Name, arguments.Database,
            arguments.Collection, idEjson, payload, arguments.IndexName, arguments.Unique, arguments.Sparse,
            arguments.MaxTimeMs, stateHash, before);
        return new WritePreparation(proposal, profile, authorized.PolicyRevision, null);
    }

    // Same checks as the preflight, from fresh state, reusing only the approved before-state. The consumption compares
    // the resulting operation hash with the approved one, so any change of profile, generation, revision, provider or
    // literal operation denies.
    private async Task<WritePreparation> RebuildProposalAsync(
        AgentPrincipal principal, AgentInvocationContext context, AgentOutputDestination destination,
        AgentOutputDataScope outputScope, AgentToolDescriptor descriptor, WriteArguments arguments,
        AgentWriteProposal frozen, ConnectionProfile frozenProfile, CancellationToken cancellationToken)
    {
        if (FindDescriptor(descriptor.Name) is not { } currentDescriptor || currentDescriptor.Version != frozen.ToolVersion)
            return WritePreparation.Deny(PermissionDenied, AgentAuditDecisionReason.ValidationRejected);
        var authorized = await AuthorizeWriteAsync(principal, context, destination, outputScope, descriptor, arguments,
            cancellationToken).ConfigureAwait(false);
        if (authorized.Failure is not null) return authorized;
        var profile = authorized.Profile!;
        if (profile != frozenProfile || authorized.PolicyRevision != frozen.PolicyRevision)
            return WritePreparation.Deny(PermissionDenied, profile != frozenProfile
                ? AgentAuditDecisionReason.ValidationRejected : AgentAuditDecisionReason.PolicyRevisionMismatch);
        var proposal = new AgentWriteProposal(frozen.Operation, descriptor.Name, currentDescriptor.Version,
            currentDescriptor.Risk, currentDescriptor.RequiredPermissions[0], principal.Id, principal.Origin,
            context.SessionId!.Value, context.TurnId!.Value, frozen.InvocationId, frozen.ApprovalId, context.ProviderId,
            profile.Id, profile.SourceGenerationId!.Value, authorized.PolicyRevision, profile.Name, arguments.Database,
            arguments.Collection, frozen.IdEjson, frozen.Operation == AgentWriteOperationKind.InsertOne
                ? frozen.PayloadEjson : arguments.PayloadEjson,
            arguments.IndexName, arguments.Unique, arguments.Sparse, arguments.MaxTimeMs, frozen.ExpectedStateHash,
            frozen.BeforeEjson);
        return new WritePreparation(proposal, profile, authorized.PolicyRevision, null);
    }

    private async Task<WritePreparation> AuthorizeWriteAsync(
        AgentPrincipal principal, AgentInvocationContext context, AgentOutputDestination destination,
        AgentOutputDataScope outputScope, AgentToolDescriptor descriptor, WriteArguments arguments,
        CancellationToken cancellationToken)
    {
        if (await CheckPrincipalCurrentAsync(principal, cancellationToken).ConfigureAwait(false) is { } channelDenial)
            return WritePreparation.Deny(PermissionDenied,
                channelDenial.AuditReason ?? AgentAuditDecisionReason.PermissionMissing);
        var load = await LoadCurrentPolicyAsync(principal, cancellationToken).ConfigureAwait(false);
        if (load.Policy is null) return WritePreparation.Deny(PermissionDenied, load.DenialReason);
        var policy = load.Policy;

        ConnectionProfile profile;
        try
        {
            var profiles = await AwaitWithCancellationAsync(_profiles.GetAllAsync(cancellationToken), cancellationToken)
                .ConfigureAwait(false);
            if (profiles is null) return WritePreparation.Deny(PermissionDenied, AgentAuditDecisionReason.ExecutionFailed);
            var matching = profiles.Where(item => item?.Id == arguments.ConnectionId).Take(2).ToArray();
            if (matching.Length != 1 || matching[0] is not { SourceGenerationId: { } generation } ||
                generation == Guid.Empty || !IsValidProfileName(matching[0].Name) || HasDynamicMongoTarget(matching[0]))
                return WritePreparation.Deny(PermissionDenied, AgentAuditDecisionReason.ValidationRejected);
            profile = matching[0];
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch
        {
            cancellationToken.ThrowIfCancellationRequested();
            return WritePreparation.Deny(PermissionDenied, AgentAuditDecisionReason.ExecutionFailed);
        }
        // Read-only connections deny every write, index included, before policy evaluation or the human prompt.
        if (profile.IsReadOnly)
            return WritePreparation.Deny(PermissionDenied, AgentAuditDecisionReason.PermissionMissing);

        var scope = AgentNamespaceScope.ForCollection(profile.Id, arguments.Database, arguments.Collection);
        foreach (var permission in descriptor.RequiredPermissions)
        {
            AgentPermissionDecision decision;
            try
            {
                decision = await AwaitWithCancellationAsync(_permissions.EvaluateAsync(
                    new AgentPermissionRequest(principal, permission, descriptor.Risk, scope, policy.Revision,
                        profile.IsReadOnly, context, profile.SourceGenerationId, destination, outputScope),
                    cancellationToken), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch
            {
                cancellationToken.ThrowIfCancellationRequested();
                return WritePreparation.Deny(PermissionDenied, AgentAuditDecisionReason.PolicyUnavailable);
            }
            if (decision.PolicyRevision != policy.Revision)
                return WritePreparation.Deny(PermissionDenied, AgentAuditDecisionReason.PolicyRevisionMismatch);
            if (!decision.IsAllowed) return WritePreparation.Deny(PermissionDenied, MapDenialReason(decision.Reason));
        }
        return new WritePreparation(null, profile, policy.Revision, null);
    }

    private int EffectiveMaxTime(WriteArguments arguments) =>
        Math.Min(arguments.MaxTimeMs, (int)Math.Clamp(_executionTimeout.TotalMilliseconds, 1, 30_000));

    private static bool IsStateHash(string? value) =>
        value is { Length: 64 } && value.All(c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f');

    private static (string Document, string IdEjson) WithFixedId(string documentEjson)
    {
        using var document = JsonDocument.Parse(documentEjson, new JsonDocumentOptions
        {
            MaxDepth = AgentToolLiteralEjson.MaximumDepth
        });
        var buffer = new ArrayBufferWriter<byte>();
        string idEjson;
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions
               {
                   Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
               }))
        {
            if (document.RootElement.TryGetProperty("_id", out var existing))
            {
                document.RootElement.WriteTo(writer);
                writer.Flush();
                return (Encoding.UTF8.GetString(buffer.WrittenSpan), existing.GetRawText());
            }
            Span<byte> id = stackalloc byte[12];
            BinaryPrimitives.WriteUInt32BigEndian(id, (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            RandomNumberGenerator.Fill(id[4..]);
            idEjson = "{\"$oid\":\"" + Convert.ToHexStringLower(id) + "\"}";
            writer.WriteStartObject();
            writer.WritePropertyName("_id");
            writer.WriteRawValue(idEjson);
            foreach (var property in document.RootElement.EnumerateObject())
                property.WriteTo(writer);
            writer.WriteEndObject();
        }
        return (Encoding.UTF8.GetString(buffer.WrittenSpan), idEjson);
    }

    private async Task<AgentToolInvocationResult> DenyWriteAsync(WriteAuditState audit, string errorCode,
        AgentAuditDecisionReason reason)
    {
        await audit.AppendAsync(this, AgentAuditOutcome.Denied, AgentAuditDecision.Denied, reason)
            .ConfigureAwait(false);
        return AgentToolInvocationResult.Failure(errorCode, reason);
    }

    private static AgentAuditEvent CreateWriteIntent(AgentPrincipal principal, AgentInvocationContext context,
        AgentOutputDestination destination, AgentToolDescriptor descriptor, Guid? connectionId, bool hasNamespace,
        Guid approvalId)
    {
        var startedAt = DateTimeOffset.UtcNow;
        return new AgentAuditEvent(Guid.NewGuid(), AgentAuditEvent.CurrentSchemaVersion, startedAt, principal.Id,
            Guid.NewGuid(), context.SessionId, context.TurnId, AuditChannelOf(destination), destination.ProviderId,
            descriptor.Name, descriptor.Version, descriptor.Risk, descriptor.RequiredPermissions[0],
            AgentAuditDecision.Requested, AgentAuditOutcome.Intent, principal.PolicyRevision,
            connectionId is { } id && id != Guid.Empty ? id : null, 0, 0, 0)
        {
            // Never the raw namespace nor any content hash in the persistent ledger.
            NamespaceKind = hasNamespace ? AgentAuditNamespaceKind.Pseudonym : AgentAuditNamespaceKind.None,
            NamespacePseudonym = hasNamespace ? Guid.NewGuid().ToString("N") : null,
            DecisionReason = AgentAuditDecisionReason.NotEvaluated,
            ApprovalState = AgentAuditApprovalState.Pending,
            ApprovalId = approvalId,
            StartedAtUtc = startedAt
        }.Validate();
    }

    private static bool TryParseWriteArguments(string name, string? json, out WriteArguments? arguments)
    {
        arguments = null;
        if (json is null || Utf8ByteCount(json) > MaximumInputBytes) return false;
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            Guid connectionId = Guid.Empty;
            string? database = null, collection = null, id = null, payload = null, indexName = null;
            var unique = false;
            var sparse = false;
            var maxTimeMs = 5_000;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!seen.Add(property.Name)) return false;
                var value = property.Value;
                var text = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
                switch (property.Name)
                {
                    case "connectionId" when Guid.TryParseExact(text, "D", out var parsed) && parsed != Guid.Empty:
                        connectionId = parsed;
                        break;
                    case "database" when IsWritableDatabase(text):
                        database = text;
                        break;
                    case "collection" when IsWritableCollection(text):
                        collection = text;
                        break;
                    case "maxTimeMs" when value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var time) &&
                        time is >= 1 and <= 30_000:
                        maxTimeMs = time;
                        break;
                    case "documentEjson" when name == InsertOneToolName &&
                        AgentToolLiteralEjson.IsLiteralDocument(text):
                        payload = text;
                        break;
                    case "idEjson" when name is UpdateOneToolName or DeleteOneToolName &&
                        AgentToolLiteralEjson.IsLiteralIdentifier(text):
                        id = text;
                        break;
                    case "updateEjson" when name == UpdateOneToolName && AgentToolLiteralEjson.IsUpdateDocument(text):
                        payload = text;
                        break;
                    case "keysEjson" when name == CreateIndexToolName && AgentToolLiteralEjson.IsIndexKeys(text):
                        payload = text;
                        break;
                    case "name" when name == CreateIndexToolName && IsWritableIndexName(text):
                        indexName = text;
                        break;
                    case "unique" when name == CreateIndexToolName && value.ValueKind is JsonValueKind.True or JsonValueKind.False:
                        unique = value.GetBoolean();
                        break;
                    case "sparse" when name == CreateIndexToolName && value.ValueKind is JsonValueKind.True or JsonValueKind.False:
                        sparse = value.GetBoolean();
                        break;
                    case "indexName" when name == DropIndexToolName && IsWritableIndexName(text):
                        indexName = text;
                        break;
                    default:
                        return false;
                }
            }
            if (connectionId == Guid.Empty || database is null || collection is null) return false;
            var complete = name switch
            {
                InsertOneToolName => payload is not null,
                UpdateOneToolName => id is not null && payload is not null,
                DeleteOneToolName => id is not null,
                CreateIndexToolName => payload is not null,
                DropIndexToolName => indexName is not null,
                _ => false
            };
            if (!complete) return false;
            arguments = new WriteArguments(connectionId, database, collection, id, payload, indexName, unique, sparse,
                maxTimeMs);
            return true;
        }
        catch (JsonException) { return false; }
    }

    private static bool IsWritableDatabase(string? name) =>
        IsSafeMetadataName(name) && name!.Length <= 64 && !ProtectedDatabases.Contains(name) &&
        name.IndexOfAny(['.', '$', ' ', '"', '*', '<', '>', ':', '|', '?']) < 0;

    private static bool IsWritableCollection(string? name) =>
        IsSafeMetadataName(name) && !name!.Contains('$') && !name.StartsWith("system.", StringComparison.Ordinal);

    // _id_ is never dropped nor recreated by an agent; "*" would drop every index.
    private static bool IsWritableIndexName(string? name) =>
        IsSafeMetadataName(name) && name!.Length <= 128 && name != "_id_" && name != "*" && !name.Contains('$');

    private sealed record WriteArguments(Guid ConnectionId, string Database, string Collection, string? IdEjson,
        string? PayloadEjson, string? IndexName, bool Unique, bool Sparse, int MaxTimeMs);

    private sealed record WritePreparation(AgentWriteProposal? Proposal, ConnectionProfile? Profile,
        long PolicyRevision, AgentToolInvocationResult? Failure)
    {
        public static WritePreparation Deny(string code, AgentAuditDecisionReason reason) =>
            new(null, null, 0, AgentToolInvocationResult.Failure(code, reason));
    }

    private sealed record WriteResponse(
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("affectedCount")] int AffectedCount,
        [property: JsonPropertyName("insertedIdEjson"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? InsertedIdEjson,
        [property: JsonPropertyName("indexName"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? IndexName);

    /// <summary>Correlates the single terminal event with its intent; approval time is taken on the registry clock.</summary>
    /// <summary>
    /// Bounded, best effort: waits for the in-flight intent append (up to 5 s) and, only if it was persisted, appends its
    /// Cancelled terminal. An intent still unconfirmed after that stays pending, visible to reconciliation.
    /// </summary>
    private async Task CloseCancelledIntentAsync(AgentAuditEvent intent, Task intentAppend)
    {
        try
        {
            await intentAppend.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        catch
        {
            return;
        }

        await new WriteAuditState(intent).AppendAsync(this, AgentAuditOutcome.Cancelled, AgentAuditDecision.Requested,
            AgentAuditDecisionReason.Cancelled).ConfigureAwait(false);
    }

    private sealed class WriteAuditState(AgentAuditEvent intent)
    {
        private int _terminalWritten;

        public bool Approved { get; private set; }
        public DateTimeOffset? ApprovedAtUtc { get; private set; }

        public void MarkApproved()
        {
            var now = DateTimeOffset.UtcNow;
            ApprovedAtUtc = now < intent.StartedAtUtc ? intent.StartedAtUtc : now;
            Approved = true;
        }

        public async Task<bool> AppendAsync(AgentToolRegistry owner, AgentAuditOutcome outcome,
            AgentAuditDecision decision, AgentAuditDecisionReason reason, AgentAuditApprovalState? state = null,
            int itemCount = 0, int outputBytes = 0)
        {
            if (Interlocked.Exchange(ref _terminalWritten, 1) != 0) return false;
            var approvalState = state ?? (Approved ? AgentAuditApprovalState.ApprovedOnce : AgentAuditApprovalState.Pending);
            var approvedAt = approvalState == AgentAuditApprovalState.ApprovedOnce ? ApprovedAtUtc : null;
            try
            {
                var completedAt = DateTimeOffset.UtcNow;
                if (completedAt < intent.StartedAtUtc) completedAt = intent.StartedAtUtc;
                if (approvedAt is { } approved && completedAt < approved) completedAt = approved;
                var elapsed = completedAt - intent.StartedAtUtc;
                var terminal = intent with
                {
                    Id = Guid.NewGuid(),
                    OccurredAtUtc = completedAt,
                    Decision = decision,
                    Outcome = outcome,
                    DurationMilliseconds = (elapsed.Ticks + TimeSpan.TicksPerMillisecond - 1) / TimeSpan.TicksPerMillisecond,
                    ItemCount = itemCount,
                    OutputBytes = outputBytes,
                    DecisionReason = reason,
                    ApprovalState = approvalState,
                    ApprovedAtUtc = approvedAt,
                    CompletedAtUtc = completedAt
                };
                terminal.Validate();
                using var appendDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await owner.AwaitWithCancellationAsync(owner._audit.AppendAsync(terminal, appendDeadline.Token),
                    appendDeadline.Token).ConfigureAwait(false);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
