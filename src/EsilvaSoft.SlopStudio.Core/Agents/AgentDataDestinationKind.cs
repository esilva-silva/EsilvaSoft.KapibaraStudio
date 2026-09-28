namespace EsilvaSoft.SlopStudio.Core.Agents;

/// <summary>
/// Where the data of a session or tool call is processed, for presentation. Trusted code derives it from the provider's
/// <c>IsLocal</c> declaration (the same source as the tool output destination), never from provider output.
/// </summary>
public enum AgentDataDestinationKind
{
    Local,
    External,
}
