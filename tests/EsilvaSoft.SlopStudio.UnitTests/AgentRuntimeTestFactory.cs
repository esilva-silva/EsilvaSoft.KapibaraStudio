using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Application.Agents;

namespace EsilvaSoft.SlopStudio.UnitTests;

/// <summary>Single construction point of a runtime that dispatches tools through the registry port.</summary>
internal static class AgentRuntimeTestFactory
{
    public static AgentRuntime Dispatch(
        IEnumerable<IAgentProvider> providers,
        IAgentToolRegistry registry,
        IAgentToolBindingProvider bindings,
        IAgentPrincipalAuthority principals,
        AgentRuntimeOptions? options = null,
        IAgentInteractionAuthority? authority = null) =>
        new(providers, authority, options, registry, bindings, principals);
}
