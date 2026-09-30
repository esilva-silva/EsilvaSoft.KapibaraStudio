using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Desktop.Agents;
using EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Codex;

namespace EsilvaSoft.KapibaraStudio.Desktop;

public partial class App
{
    /// <summary>Experimental official Codex App Server account operations, always initiated explicitly.</summary>
    public sealed class CodexAccountHandler(CodexSubscriptionAgentProvider provider, IExternalUriLauncher browser) :
        IAgentAccountHandler, IAgentCliAccountPresentationHandler
    {
        private readonly CodexSubscriptionAgentProvider _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        private readonly IExternalUriLauncher _browser = browser ?? throw new ArgumentNullException(nameof(browser));
        public string ProviderId => CodexSubscriptionAgentProvider.Id;
        public AgentAccountCheckPolicy CheckPolicy { get; } = new(
            SupportsAutomaticCheck: false, MayUseNetwork: true, MayShowCredentialDialog: true);
        public AgentCliProviderProfile Profile { get; } = new(
            CliName: "Codex App Server", RecipientName: "ChatGPT", SignInCommand: "Sign in through Codex App Server",
            TranscriptLocation: "Codex App Server managed session", CredentialLocation: "OS keyring (Codex-managed)",
            ConfigLocation: "KapibaraStudio private CODEX_HOME", UsesBrowserAppServerLogin: true);

        public AgentCliReadScope DescribeReadScope(string? candidateWorkspace) => AgentCliReadScope.None;
        public Task<AgentAccountStatus> CheckAsync(CancellationToken cancellationToken) => CheckCodexAsync(_provider, cancellationToken);
        public Task<AgentAccountCommandResult> SignInAsync(CancellationToken cancellationToken) => SignInCodexAsync(_provider, _browser, cancellationToken);
        public Task<AgentAccountCommandResult> SignOutAsync(bool userConfirmedGlobalSignOut, CancellationToken cancellationToken)
        {
            if (!userConfirmedGlobalSignOut) throw new InvalidOperationException("O logout global exige confirmação explícita.");
            return SignOutCodexAsync(_provider, cancellationToken);
        }
    }
}
