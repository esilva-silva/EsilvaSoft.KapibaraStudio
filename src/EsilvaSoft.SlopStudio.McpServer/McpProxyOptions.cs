using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.McpServer;

/// <summary>
/// Parsed command line. Every value is a non-secret identifier: the channel proof is read from the OS store under
/// <see cref="ProofReference"/>. Unknown or repeated options are rejected without echoing their values.
/// </summary>
internal sealed record McpProxyOptions(Guid WorkspaceId, Guid ChannelId, SecretReference ProofReference,
    string? ProtocolVersion)
{
    public const string LegacyProtocolRevision = "2025-11-25";
    public const string CurrentProtocolRevision = "2026-07-28";

    public const string Usage =
        "Uso: EsilvaSoft.SlopStudio.McpServer --stdio --workspace-id <uuid> --channel-id <uuid> " +
        "--proof-ref <uuid> [--proof-ref-version <n>] [--protocol-version 2025-11-25|2026-07-28]";

    public static McpProxyOptions? TryParse(IReadOnlyList<string> args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var stdio = false;
        for (var index = 0; index < args.Count; index++)
        {
            var name = args[index];
            if (name == "--stdio")
            {
                if (stdio) return null;
                stdio = true;
                continue;
            }
            if (name is not ("--workspace-id" or "--channel-id" or "--proof-ref" or "--proof-ref-version" or
                "--protocol-version") || index + 1 >= args.Count || !values.TryAdd(name, args[++index]))
                return null;
        }

        if (!stdio || !TryGuid(values, "--workspace-id", out var workspace) ||
            !TryGuid(values, "--channel-id", out var channel) || !TryGuid(values, "--proof-ref", out var proof))
            return null;
        var version = 1;
        if (values.TryGetValue("--proof-ref-version", out var rawVersion) &&
            (!int.TryParse(rawVersion, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out version) || version < 1))
            return null;
        values.TryGetValue("--protocol-version", out var protocol);
        if (protocol is not (null or LegacyProtocolRevision or CurrentProtocolRevision)) return null;
        return new McpProxyOptions(workspace, channel, new SecretReference(proof, version), protocol);
    }

    private static bool TryGuid(Dictionary<string, string> values, string name, out Guid value)
    {
        value = Guid.Empty;
        return values.TryGetValue(name, out var raw) && Guid.TryParseExact(raw, "D", out value) && value != Guid.Empty;
    }
}
