namespace EsilvaSoft.KapibaraStudio.Application.Agents;

/// <summary>
/// Official account adapter for one provider, registered once at composition. The dispatcher selects it by the
/// opaque provider ID; account operations never need a brand switch. Metadata is static and I/O-free.
/// </summary>
public interface IAgentAccountHandler
{
    string ProviderId { get; }
    AgentAccountCheckPolicy CheckPolicy { get; }
    Task<AgentAccountStatus> CheckAsync(CancellationToken cancellationToken);
    Task<AgentAccountCommandResult> SignInAsync(CancellationToken cancellationToken);
    Task<AgentAccountCommandResult> SignOutAsync(bool userConfirmedGlobalSignOut, CancellationToken cancellationToken);
}
