namespace EsilvaSoft.SlopStudio.Core.Agents;

/// <summary>
/// Who executed the tool call shown by a tool event. Set by the runtime only, never copied from provider output.
/// </summary>
public enum AgentToolOrigin
{
    /// <summary>Dispatched by the runtime through the shared registry (authorization, approval and audit apply).</summary>
    Registry,

    /// <summary>
    /// Native tool executed by the provider itself (e.g. Claude Code Read/Glob/Grep), outside the registry. Display
    /// only: name and state, no arguments or content; it never authorizes, answers or approves anything.
    /// </summary>
    ProviderObserved,
}
