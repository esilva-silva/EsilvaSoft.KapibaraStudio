namespace EsilvaSoft.KapibaraStudio.Core.Agents;

/// <summary>
/// Officially supported authentication methods of an agent provider. The app never implements a subscription login
/// (ChatGPT, Claude.ai, Codex) itself: a UI cannot offer a method that has no member here. New members require an ADR.
/// </summary>
public enum AgentAuthenticationMethod
{
    /// <summary>Local provider; no external account.</summary>
    None,

    /// <summary>User-provided API key stored in the OS vault by an explicit action.</summary>
    ApiKey,

    /// <summary>
    /// Authentication fully delegated to an official, unmodified CLI installed by the user (ADR-053: the Claude Code
    /// binary). The app only starts the CLI's own login/logout commands in a window visible to the user and reads the
    /// CLI's status output through a field allowlist; it never reads, stores or forwards tokens or credential files.
    /// </summary>
    OfficialCliDelegated,

    /// <summary>
    /// Authentication delegated to the official Codex App Server over its local protocol. Login UI, OAuth callbacks,
    /// refresh and sign-out remain owned by Codex; the host never reads, stores or forwards credentials. Experimental
    /// surface, introduced by ADR-057.
    /// </summary>
    OfficialAppServerDelegated,
}
