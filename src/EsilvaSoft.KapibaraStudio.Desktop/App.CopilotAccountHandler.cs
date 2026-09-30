using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Desktop.Agents;
using EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Copilot;

namespace EsilvaSoft.KapibaraStudio.Desktop;

public partial class App
{
    /// <summary>Official Copilot account/model discovery; Linux keyring inspection stays an explicit action.</summary>
    public sealed class CopilotAccountHandler(CopilotSubscriptionAgentProvider provider) :
        IAgentAccountHandler, IAgentCliAccountPresentationHandler
    {
        private readonly CopilotSubscriptionAgentProvider _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        public string ProviderId => CopilotSubscriptionAgentProvider.Id;
        public AgentAccountCheckPolicy CheckPolicy { get; } = new(
            SupportsAutomaticCheck: OperatingSystem.IsWindows(), MayUseNetwork: true,
            MayShowCredentialDialog: OperatingSystem.IsLinux());
        public AgentCliProviderProfile Profile { get; } = new(
            CliName: "GitHub Copilot CLI", RecipientName: "GitHub Copilot", SignInCommand: "copilot login",
            TranscriptLocation: "~/.copilot/session-state (managed by Copilot CLI)",
            CredentialLocation: "Windows Credential Manager; Linux libsecret keyring; local config fallback may be offered",
            ConfigLocation: "Copilot CLI managed configuration", SignOutMessageKey: "agentCliCopilotSignOutMessage",
            CredentialNoticeKey: "agentCliCopilotCredentialNotice", SubscriptionReadyMessageKey: "agentCliCopilotAccountChecked",
            SignInCompletedMessageKey: "agentCliCopilotAccountChecked");

        public AgentCliReadScope DescribeReadScope(string? candidateWorkspace) => AgentCliReadScope.None;
        public Task<AgentAccountStatus> CheckAsync(CancellationToken cancellationToken) => CheckCopilotAsync(_provider, cancellationToken);
        public Task<AgentAccountCommandResult> SignInAsync(CancellationToken cancellationToken) => SignInCopilotAsync(_provider, cancellationToken);
        public Task<AgentAccountCommandResult> SignOutAsync(bool userConfirmedGlobalSignOut, CancellationToken cancellationToken)
        {
            if (!userConfirmedGlobalSignOut) throw new InvalidOperationException("O logout global do Copilot exige confirmação explícita.");
            return SignOutCopilotAsync(_provider, cancellationToken);
        }
    }
}
