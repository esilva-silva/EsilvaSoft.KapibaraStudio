using EsilvaSoft.KapibaraStudio.Application.Agents.Broker;
using EsilvaSoft.KapibaraStudio.SystemAdapters;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EsilvaSoft.KapibaraStudio.McpServer;

/// <summary>
/// STDIO MCP proxy of Kapibara Studio. stdout carries only JSON-RPC; stderr carries only fixed diagnostic codes, never
/// identifiers, arguments, results or secrets. The proxy opens no database and receives no MongoDB URI: data reaches it
/// only as tool DTOs from the authenticated broker hosted by the IDE.
/// </summary>
public static class Program
{
    /// <summary>Largest accepted JSON-RPC line on stdin (64 KiB of arguments plus envelope and escaping).</summary>
    internal const int MaximumInputLineBytes = 256 * 1024;

    public static Task<int> Main(string[] args) => RunAsync(args, McpProxySystemAdapters.Create());

    internal static async Task<int> RunAsync(string[] args, McpProxySystemAdapterSet adapters)
    {
        ArgumentNullException.ThrowIfNull(adapters);
        var console = adapters.Console;
        var stderr = console.StandardError;
        var options = McpProxyOptions.TryParse(args ?? []);
        if (options is null)
        {
            await stderr.WriteLineAsync("slop-mcp: argumentos inválidos. " + McpProxyOptions.Usage).ConfigureAwait(false);
            return 2;
        }

        // Grab the raw stdout for the protocol and neutralize Console.Out so no library can add noise to it.
        var session = AgentMcpConsoleSession.Open(console);
        var stdout = session.Output;
        await using var stdin = new BoundedLineReadStream(session.Input, MaximumInputLineBytes);

        var localTransport = adapters.Transport;
        var credentials = adapters.Credentials;
        AgentBrokerEndpoint endpoint;
        try
        {
            endpoint = localTransport.GetEndpoint(options.WorkspaceId);
        }
        catch (PlatformNotSupportedException)
        {
            await stderr.WriteLineAsync("slop-mcp: plataforma sem endpoint local privado.").ConfigureAwait(false);
            return 3;
        }

        await using var broker = new AgentBrokerClient(endpoint, options.ChannelId, options.ProofReference, credentials,
            transport: localTransport);
        var adapter = new McpToolAdapter(broker);
        var serverOptions = new McpServerOptions
        {
            ServerInfo = new Implementation { Name = "esilvasoft-kapibarastudio", Version = "0.11.0" },
            ProtocolVersion = options.ProtocolVersion,
            Capabilities = new ServerCapabilities { Tools = new ToolsCapability() },
            Handlers = new McpServerHandlers
            {
                ListToolsHandler = (_, cancellationToken) => adapter.ListToolsAsync(cancellationToken),
                CallToolHandler = (context, cancellationToken) => adapter.CallToolAsync(context.Params, cancellationToken)
            }
        };

        await using var transport = new StreamServerTransport(stdin, stdout, "esilvasoft-kapibarastudio");
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
