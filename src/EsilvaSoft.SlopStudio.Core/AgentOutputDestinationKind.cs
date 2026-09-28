namespace EsilvaSoft.SlopStudio.Core;

/// <summary>
/// Typed recipient class of tool output. Values are persisted in grants: new members are additive and an unknown value
/// read from storage fails closed.
/// </summary>
public enum AgentOutputDestinationKind
{
    /// <summary>In-process consumer that sends nothing off the machine (local provider).</summary>
    Local = 0,

    /// <summary>External chat provider driven by the agent runtime (OpenAI, Claude).</summary>
    ProviderExternal = 1,

    /// <summary>External MCP client connected through the authenticated local broker.</summary>
    McpExternal = 2
}
