using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.Agents;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

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
