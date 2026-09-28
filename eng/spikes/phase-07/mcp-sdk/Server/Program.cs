using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EsilvaSoft.SlopStudio.Spikes.McpServer;

public static class Program
{
    public static async Task Main(string[] args)
    {
        // Nenhum host gráfico, configuração de usuário, HTTP, MongoDB ou tool de dados.
        var options = new McpServerOptions
        {
            ServerInfo = new Implementation { Name = "slop-phase07-protocol-spike", Version = "0.1.0" },
            ProtocolVersion = args.Length == 1 ? args[0] : null,
            Capabilities = new ServerCapabilities { Tools = new ToolsCapability() },
            Handlers = new McpServerHandlers
            {
                ListToolsHandler = (_, _) => ValueTask.FromResult(new ListToolsResult { Tools = [] })
            }
        };

        await using var server = ModelContextProtocol.Server.McpServer.Create(
            new StdioServerTransport(options), options);
        await server.RunAsync();
    }
}
