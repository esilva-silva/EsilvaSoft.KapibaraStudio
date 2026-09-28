using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Infrastructure;

/// <summary>
/// Trusted classification of the data each catalog tool releases. It is chosen by the broker, never by the client,
/// and the registry still requires a grant for exactly this scope and destination. The table itself is
/// <see cref="AgentToolOutputScopes"/>, shared with the native runtime binding so both ingresses ask for the same grant.
/// </summary>
internal static class AgentBrokerOutputScopes
{
    public static AgentOutputDataScope? For(string? toolName) => AgentToolOutputScopes.For(toolName);
}
