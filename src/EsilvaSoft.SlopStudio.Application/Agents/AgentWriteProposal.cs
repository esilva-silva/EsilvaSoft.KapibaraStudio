using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Core.Agents;

namespace EsilvaSoft.SlopStudio.Application.Agents;

/// <summary>
/// Immutable write proposal frozen by the registry after validation, authorization and the before-state read. Only
/// the registry creates it (internal constructor); no field comes from model text other than the validated literal
/// operation. <see cref="OperationHash"/> binds the canonical operation (tool/version, provider route, connection and
/// source generation, policy revision, namespace, target, literal payload, options and approved before-state hash).
/// The hash lives only in memory; the audit ledger never stores it.
/// </summary>
public sealed class AgentWriteProposal
{
    private const string HashDomain = "esilvasoft.slopstudio.agent-write.v1";

    internal AgentWriteProposal(
        AgentWriteOperationKind operation, string toolName, int toolVersion, AgentToolRisk risk,
        AgentPermission permission, Guid principalId, AgentPrincipalOrigin principalOrigin, Guid sessionId,
        Guid turnId, Guid invocationId, Guid approvalId, string? providerId, Guid connectionId,
        Guid sourceGenerationId, long policyRevision, string connectionLabel, string database, string collection,
        string? idEjson, string? payloadEjson, string? indexName, bool unique, bool sparse, int maxTimeMs,
        string? expectedStateHash, string? beforeEjson)
    {
        if (!Enum.IsDefined(operation) || string.IsNullOrEmpty(toolName) || toolVersion < 1 ||
            risk is not (AgentToolRisk.Write or AgentToolRisk.Destructive) ||
            !AgentPermissionRiskCompatibility.IsCompatible(permission, risk) || principalId == Guid.Empty ||
            sessionId == Guid.Empty || turnId == Guid.Empty || invocationId == Guid.Empty ||
            approvalId == Guid.Empty || connectionId == Guid.Empty || sourceGenerationId == Guid.Empty ||
            policyRevision < 1 || string.IsNullOrEmpty(database) || string.IsNullOrEmpty(collection) ||
            maxTimeMs is < 1 or > 30_000)
            throw new ArgumentException("Proposta de escrita inválida.");
        Operation = operation;
        ToolName = toolName;
        ToolVersion = toolVersion;
        Risk = risk;
        Permission = permission;
        PrincipalId = principalId;
        PrincipalOrigin = principalOrigin;
        SessionId = sessionId;
        TurnId = turnId;
        InvocationId = invocationId;
        ApprovalId = approvalId;
        ProviderId = providerId;
        ConnectionId = connectionId;
        SourceGenerationId = sourceGenerationId;
        PolicyRevision = policyRevision;
        ConnectionLabel = connectionLabel;
        Database = database;
        Collection = collection;
        IdEjson = idEjson is null ? null : Compact(idEjson);
        PayloadEjson = payloadEjson is null ? null : Compact(payloadEjson);
        IndexName = indexName;
        Unique = unique;
        Sparse = sparse;
        MaxTimeMs = maxTimeMs;
        ExpectedStateHash = expectedStateHash;
        BeforeEjson = beforeEjson;
        OperationHash = ComputeOperationHash();
    }

    public AgentWriteOperationKind Operation { get; }
    public string ToolName { get; }
    public int ToolVersion { get; }
    public AgentToolRisk Risk { get; }
    public AgentPermission Permission { get; }
    public Guid PrincipalId { get; }
    public AgentPrincipalOrigin PrincipalOrigin { get; }
    public Guid SessionId { get; }
    public Guid TurnId { get; }
    public Guid InvocationId { get; }
    public Guid ApprovalId { get; }
    public string? ProviderId { get; }
    public Guid ConnectionId { get; }
    public Guid SourceGenerationId { get; }
    public long PolicyRevision { get; }

    /// <summary>Local display label of the connection; shown only to the local approver.</summary>
    public string ConnectionLabel { get; }
    public string Database { get; }
    public string Collection { get; }
    public string? IdEjson { get; }

    /// <summary>Document to insert, update-operator document or index keys, compact and in original field order.</summary>
    public string? PayloadEjson { get; }
    public string? IndexName { get; }
    public bool Unique { get; }
    public bool Sparse { get; }
    public int MaxTimeMs { get; }

    /// <summary>Before-state hash returned by the write source and approved by the human (update/delete/drop).</summary>
    public string? ExpectedStateHash { get; }

    /// <summary>Bounded before-state shown in the approval; not part of the hash (its hash is).</summary>
    public string? BeforeEjson { get; }

    /// <summary>Lowercase hex SHA-256 of the canonical operation. Two proposals authorize the same effect only if equal.</summary>
    public string OperationHash { get; }

    internal AgentApprovalId PublicApprovalId => new(ApprovalId.ToString("N"));
    internal AgentSessionId PublicSessionId => new(SessionId.ToString("N"));
    internal AgentTurnId PublicTurnId => new(TurnId.ToString("N"));

    // Length-prefixed fields: no separator ambiguity. Field order in documents is preserved because BSON order is
    // meaningful for update documents and index keys; only insignificant whitespace is normalized.
    private string ComputeOperationHash()
    {
        var buffer = new ArrayBufferWriter<byte>();
        Append(buffer, HashDomain);
        Append(buffer, ((int)Operation).ToString(CultureInfo.InvariantCulture));
        Append(buffer, ToolName);
        Append(buffer, ToolVersion.ToString(CultureInfo.InvariantCulture));
        Append(buffer, ((int)Risk).ToString(CultureInfo.InvariantCulture));
        Append(buffer, ((int)Permission).ToString(CultureInfo.InvariantCulture));
        Append(buffer, ProviderId);
        Append(buffer, ConnectionId.ToString("N"));
        Append(buffer, SourceGenerationId.ToString("N"));
        Append(buffer, PolicyRevision.ToString(CultureInfo.InvariantCulture));
        Append(buffer, Database);
        Append(buffer, Collection);
        Append(buffer, IdEjson);
        Append(buffer, PayloadEjson);
        Append(buffer, IndexName);
        Append(buffer, Unique ? "1" : "0");
        Append(buffer, Sparse ? "1" : "0");
        Append(buffer, MaxTimeMs.ToString(CultureInfo.InvariantCulture));
        Append(buffer, ExpectedStateHash);
        return Convert.ToHexStringLower(SHA256.HashData(buffer.WrittenSpan));
    }

    private static void Append(ArrayBufferWriter<byte> buffer, string? value)
    {
        if (value is null)
        {
            buffer.Write<byte>([0xFF, 0xFF, 0xFF, 0xFF]);
            return;
        }
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        buffer.Write(length);
        buffer.Write(bytes);
    }

    private static string Compact(string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = AgentToolLiteralEjson.MaximumDepth });
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions
               {
                   Indented = false,
                   Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                   SkipValidation = false
               }))
        {
            document.RootElement.WriteTo(writer);
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
}
