namespace EsilvaSoft.KapibaraStudio.Application.Agents;

/// <summary>
/// I/O-free policy for account checks. Unknown providers and runtimes that cannot guarantee a non-interactive check
/// must reject automatic checks. Network access, when declared, is restricted to official account/model discovery;
/// it does not permit inference, login or forwarding application context.
/// </summary>
public sealed record AgentAccountCheckPolicy(
    bool SupportsAutomaticCheck,
    bool MayUseNetwork,
    bool MayShowCredentialDialog = false)
{
    public static AgentAccountCheckPolicy Unsupported { get; } = new(false, false);

    public bool AllowsAutomaticCheck => SupportsAutomaticCheck && !MayShowCredentialDialog;
}

/// <summary>
/// Provider-neutral operations for accounts delegated to an official runtime. Checks recognize authentication
/// maintained by that runtime, never open interactive login and never call a model or create a chat session.
/// Presentation metadata and workspace read-scope previews are separate contracts. Providers using API credentials
/// or local models remain on their existing credential/status ports, without artificial CLI account operations.
/// </summary>
public interface IAgentAccountManager
{
    /// <summary>Static check policy, without process, network, filesystem or credential-store access.</summary>
    AgentAccountCheckPolicy DescribeCheckPolicy(string providerId);

    /// <summary>
    /// Actively queries official runtime installation/account status and, where supported, publishes available models
    /// through the existing provider catalog. It never exposes credentials or sends prompts, files or MongoDB context.
    /// </summary>
    Task<AgentAccountStatus> CheckAsync(string providerId, CancellationToken cancellationToken);

    /// <summary>Starts the official interactive sign-in only after the user's explicit action.</summary>
    Task<AgentAccountCommandResult> SignInAsync(string providerId, CancellationToken cancellationToken);

    /// <summary>Signs out the official runtime globally; the user must explicitly confirm its scope.</summary>
    Task<AgentAccountCommandResult> SignOutAsync(
        string providerId, bool userConfirmedGlobalSignOut, CancellationToken cancellationToken);
}
