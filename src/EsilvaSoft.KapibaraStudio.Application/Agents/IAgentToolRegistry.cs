using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Application.Agents;

/// <summary>
/// Shared metadata and invocation boundary for every agent ingress. Implementations own schema validation and
/// authorization; adapters must not execute handlers directly.
/// </summary>
public interface IAgentToolRegistry
{
    IReadOnlyList<AgentToolDescriptor> GetDescriptors();

    /// <summary>
    /// Tools the MCP broker may list, per-session tools included; the broker filters them per authenticated channel.
    /// Defaults to <see cref="GetDescriptors"/> for registries without per-session tools.
    /// </summary>
    IReadOnlyList<AgentToolDescriptor> GetChannelDescriptors() => GetDescriptors();
    AgentToolDescriptor? FindDescriptor(string? name);
    AgentToolDescriptor? FindInProcessDescriptor(string providerId, string? name) => null;
    string? GetInputSchemaJson(string? name);
    string? GetInProcessInputSchemaJson(string providerId, string? name) => null;
    string? GetOutputSchemaJson(string? name);
    Task<AgentToolInvocationResult> InvokeAsync(
        AgentPrincipal? principal,
        AgentInvocationContext? invocationContext,
        AgentOutputDestination? destination,
        AgentOutputDataScope? outputDataScope,
        string? name,
        string? argumentsJson,
        CancellationToken cancellationToken = default);
}
