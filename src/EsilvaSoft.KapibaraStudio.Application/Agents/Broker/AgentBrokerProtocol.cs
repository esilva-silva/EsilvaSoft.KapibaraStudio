namespace EsilvaSoft.KapibaraStudio.Application.Agents.Broker;

/// <summary>
/// Versioned local IPC contract between the MCP proxy and the broker hosted by the IDE. Messages are UTF-8 JSON
/// frames with a 4-byte big-endian length prefix. The contract carries tool DTOs only: no driver objects, no MongoDB
/// URI and no provider secret. A major mismatch is rejected during the handshake, before any credential is sent.
/// </summary>
public static class AgentBrokerProtocol
{
    public const string Name = "slop-agent-broker";
    public const int MajorVersion = 1;
    public const int MinorVersion = 0;

    /// <summary>Client-to-broker frame ceiling: 64 KiB of tool arguments plus a bounded envelope.</summary>
    public const int MaximumRequestFrameBytes = 96 * 1024;

    /// <summary>Broker-to-client frame ceiling: 256 KiB of structured content plus schemas/envelope.</summary>
    public const int MaximumResponseFrameBytes = 384 * 1024;

    public const int MaximumArgumentsBytes = 64 * 1024;
    public const int NonceBytes = 32;
    public const int MaximumProofLength = 128;
    public const int MaximumToolNameLength = 96;
    public const int DefaultCallTimeoutMilliseconds = 30_000;
    public const int MaximumCallTimeoutMilliseconds = 35_000;

    /// <summary>
    /// Ceiling of the permission-prompt tool (<c>approve</c>), which waits for a human decision: the approval window
    /// (at most 120 s) plus a margin. Only that tool may ask for it; every other call keeps
    /// <see cref="MaximumCallTimeoutMilliseconds"/>.
    /// </summary>
    public const int MaximumApprovalCallTimeoutMilliseconds = 125_000;

    /// <summary>Name of the per-session permission-prompt tool (see <c>AgentToolRegistry.ApproveToolName</c>).</summary>
    public const string ApproveToolName = "approve";

    /// <summary>
    /// Provider identifier used for the typed output destination of MCP calls. It routes the output grant; the
    /// principal still comes from the authenticated channel.
    /// </summary>
    public const string McpProviderId = "mcp";

    public static class MessageTypes
    {
        public const string Hello = "hello";
        public const string HelloAck = "hello_ack";
        public const string Authenticate = "authenticate";
        public const string Authenticated = "authenticated";
        public const string ListTools = "list_tools";
        public const string Tools = "tools";
        public const string CallTool = "call_tool";
        public const string Cancel = "cancel";
        public const string Result = "result";
        public const string Error = "error";
    }

    /// <summary>Stable, sanitized codes. They never carry exception text, paths, URIs or secrets.</summary>
    public static class ErrorCodes
    {
        public const string IncompatibleVersion = "IncompatibleVersion";
        public const string ProtocolViolation = "ProtocolViolation";
        public const string AuthenticationRequired = "AuthenticationRequired";
        public const string AuthenticationFailed = "AuthenticationFailed";
        public const string RateLimited = "RateLimited";
        public const string HostUnavailable = "HostUnavailable";
        public const string Busy = "Busy";
        public const string Cancelled = "Cancelled";
        public const string DeadlineExceeded = "DeadlineExceeded";
        public const string PermissionDenied = "PermissionDenied";
        public const string InvalidArguments = "InvalidArguments";
        public const string OutcomeUnknown = "OutcomeUnknown";
    }
}
