using EsilvaSoft.SlopStudio.Application.Agents.Broker;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EsilvaSoft.SlopStudio.McpServer;

/// <summary>
/// STDIO MCP proxy of Slop Studio. stdout carries only JSON-RPC; stderr carries only fixed diagnostic codes, never
/// identifiers, arguments, results or secrets. The proxy opens no database and receives no MongoDB URI: data reaches it
/// only as tool DTOs from the authenticated broker hosted by the IDE.
/// </summary>
public static class Program
{
    /// <summary>Largest accepted JSON-RPC line on stdin (64 KiB of arguments plus envelope and escaping).</summary>
    internal const int MaximumInputLineBytes = 256 * 1024;

    public static async Task<int> Main(string[] args)
    {
        var stderr = Console.Error;
        var options = McpProxyOptions.TryParse(args ?? []);
        if (options is null)
        {
            await stderr.WriteLineAsync("slop-mcp: argumentos inválidos. " + McpProxyOptions.Usage).ConfigureAwait(false);
            return 2;
        }

        // Grab the raw stdout for the protocol and neutralize Console.Out so no library can add noise to it.
        var stdout = Console.OpenStandardOutput();
        Console.SetOut(TextWriter.Null);
        await using var stdin = new BoundedLineReadStream(Console.OpenStandardInput(), MaximumInputLineBytes);

        AgentBrokerEndpoint endpoint;
        try
        {
            endpoint = AgentBrokerEndpoint.ForWorkspace(options.WorkspaceId);
        }
        catch (PlatformNotSupportedException)
        {
            await stderr.WriteLineAsync("slop-mcp: plataforma sem endpoint local privado.").ConfigureAwait(false);
            return 3;
        }

        IClientTransportCredentialStore credentials = OperatingSystem.IsWindows()
            ? new WindowsClientTransportCredentialStore()
            : new UnavailableClientTransportCredentialStore();
        await using var broker = new AgentBrokerClient(endpoint, options.ChannelId, options.ProofReference, credentials);
        var adapter = new McpToolAdapter(broker);
        var serverOptions = new McpServerOptions
        {
            ServerInfo = new Implementation { Name = "esilvasoft-slopstudio", Version = "0.11.0" },
            ProtocolVersion = options.ProtocolVersion,
            Capabilities = new ServerCapabilities { Tools = new ToolsCapability() },
            Handlers = new McpServerHandlers
            {
                ListToolsHandler = (_, cancellationToken) => adapter.ListToolsAsync(cancellationToken),
                CallToolHandler = (context, cancellationToken) => adapter.CallToolAsync(context.Params, cancellationToken)
            }
        };

        await using var transport = new StreamServerTransport(stdin, stdout, "esilvasoft-slopstudio");
        await using var server = ModelContextProtocol.Server.McpServer.Create(transport, serverOptions);
        try
        {
            await server.RunAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        if (stdin.LimitExceeded)
        {
            await stderr.WriteLineAsync("slop-mcp: mensagem de entrada acima do limite; sessão encerrada.").ConfigureAwait(false);
            return 4;
        }
        return 0;
    }
}
