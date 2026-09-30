using EsilvaSoft.KapibaraStudio.Application.Agents.Broker;

namespace EsilvaSoft.KapibaraStudio.SystemAdapters;

/// <summary>Native console adapter for the standalone MCP executable.</summary>
public sealed class LocalAgentMcpConsole : IAgentMcpConsole
{
    public TextWriter StandardError => Console.Error;
    public Stream OpenStandardInput() => Console.OpenStandardInput();
    public Stream OpenStandardOutput() => Console.OpenStandardOutput();
    public void SuppressStandardTextOutput() => Console.SetOut(TextWriter.Null);
}
