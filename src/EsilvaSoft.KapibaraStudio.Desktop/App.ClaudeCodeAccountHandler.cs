using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Desktop.Agents;
using EsilvaSoft.KapibaraStudio.Infrastructure.Agents.ClaudeCode;

namespace EsilvaSoft.KapibaraStudio.Desktop;

public partial class App
{
    /// <summary>Adapts Claude Code's official account and workspace policy to the shared contracts.</summary>
    public sealed class ClaudeCodeAccountHandler(ClaudeCodeAgentProvider provider) :
        IAgentAccountHandler, IAgentCliAccountPresentationHandler
    {
        private readonly ClaudeCodeAgentProvider _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        public string ProviderId => ClaudeCodeAgentProvider.Id;
        public AgentAccountCheckPolicy CheckPolicy { get; } = new(SupportsAutomaticCheck: true, MayUseNetwork: false);
        public AgentCliProviderProfile Profile { get; } = new(
            CliName: "Claude Code", RecipientName: "Anthropic", SignInCommand: "claude auth login",
            TranscriptLocation: "~/.claude/projects", CredentialLocation: "~/.claude/.credentials.json", ConfigLocation: "~/.claude");

        public Task<AgentAccountStatus> CheckAsync(CancellationToken cancellationToken) =>
            CheckClaudeCodeAsync(_provider, cancellationToken);
        public Task<AgentAccountCommandResult> SignInAsync(CancellationToken cancellationToken) =>
            SignInClaudeCodeAsync(_provider, cancellationToken);
        public Task<AgentAccountCommandResult> SignOutAsync(bool userConfirmedGlobalSignOut, CancellationToken cancellationToken)
        {
            if (!userConfirmedGlobalSignOut) throw new InvalidOperationException("O logout é global e exige confirmação explícita.");
            return SignOutClaudeCodeAsync(_provider, cancellationToken);
        }
        /// <summary>
        /// The provider's own working-directory decision for the candidate captured in the UI
        /// (<see cref="ClaudeCodeAgentProvider.PreviewWorkingDirectory"/>): synchronous, no process, and exactly what
        /// CreateSessionAsync uses with the same AgentSessionOptions.WorkingDirectory.
        /// </summary>
        public AgentCliReadScope DescribeReadScope(string? candidateWorkspace)
        {
            var candidate = string.IsNullOrWhiteSpace(candidateWorkspace) ? null : candidateWorkspace;
            ClaudeCodeWorkingDirectoryPreview preview;
            try
            {
                preview = _provider.PreviewWorkingDirectory(candidate);
            }
            catch (ClaudeCodeUnavailableException)
            {
                return new AgentCliReadScope(candidate, null, false, AgentCliReadScopeRejection.ConfigurationInvalid, DedicatedDirectoryUsable: false);
            }

            return new AgentCliReadScope(candidate, preview.Directory,
                preview.Kind == ClaudeCodeWorkingDirectoryKind.Workspace,
                preview.Rejection switch
                {
                    ClaudeCodeWorkspaceRejection.None => AgentCliReadScopeRejection.None,
                    ClaudeCodeWorkspaceRejection.NotProvided => AgentCliReadScopeRejection.NotProvided,
                    ClaudeCodeWorkspaceRejection.InvalidPath => AgentCliReadScopeRejection.InvalidPath,
                    ClaudeCodeWorkspaceRejection.NotFound => AgentCliReadScopeRejection.NotFound,
                    ClaudeCodeWorkspaceRejection.Unreadable => AgentCliReadScopeRejection.Unreadable,
                    ClaudeCodeWorkspaceRejection.VolumeRoot => AgentCliReadScopeRejection.VolumeRoot,
                    ClaudeCodeWorkspaceRejection.UserProfile => AgentCliReadScopeRejection.UserProfile,
                    _ => AgentCliReadScopeRejection.ProtectedArea,
                },
                preview.ProtectedArea switch
                {
                    ClaudeCodeProtectedArea.AppData => AgentCliProtectedArea.AppData,
                    ClaudeCodeProtectedArea.Database => AgentCliProtectedArea.Database,
                    ClaudeCodeProtectedArea.ClaudeConfig => AgentCliProtectedArea.CliConfig,
                    ClaudeCodeProtectedArea.SshKeys => AgentCliProtectedArea.SshKeys,
                    _ => AgentCliProtectedArea.None,
                },
                preview.Relation switch
                {
                    ClaudeCodeProtectedRelation.SameOrInside => AgentCliProtectedRelation.SameOrInside,
                    ClaudeCodeProtectedRelation.Contains => AgentCliProtectedRelation.Contains,
                    _ => AgentCliProtectedRelation.None,
                },
                preview.DedicatedDirectoryUsable);
        }

        internal static AgentAccountInstallState MapInstall(ClaudeCodeInstallationState state) => state switch
        {
            ClaudeCodeInstallationState.Found => AgentAccountInstallState.Installed,
            ClaudeCodeInstallationState.NotFound => AgentAccountInstallState.NotFound,
            ClaudeCodeInstallationState.UnsupportedExecutable => AgentAccountInstallState.UnsupportedExecutable,
            ClaudeCodeInstallationState.VersionTooLow => AgentAccountInstallState.VersionTooLow,
            ClaudeCodeInstallationState.VersionUnreadable => AgentAccountInstallState.VersionUnreadable,
            ClaudeCodeInstallationState.ProbeTimedOut => AgentAccountInstallState.TimedOut,
            _ => AgentAccountInstallState.CheckFailed,
        };

        internal static AgentAccountStatus Map(ClaudeCodeAuthStatus auth, string? version, string? executablePath) => new(
            AgentAccountInstallState.Installed, version,
            auth.Kind switch
            {
                ClaudeCodeAuthKind.Subscription => AgentAccountAuthState.Subscription,
                ClaudeCodeAuthKind.NotLoggedIn => AgentAccountAuthState.SignedOut,
                ClaudeCodeAuthKind.ApiKey => AgentAccountAuthState.ApiKey,
                ClaudeCodeAuthKind.ApiKeyHelper => AgentAccountAuthState.ApiKeyHelper,
                ClaudeCodeAuthKind.EnvironmentToken => AgentAccountAuthState.EnvironmentToken,
                ClaudeCodeAuthKind.CloudProvider => AgentAccountAuthState.CloudProvider,
                ClaudeCodeAuthKind.BlockedEnvironment => AgentAccountAuthState.BlockedEnvironment,
                ClaudeCodeAuthKind.UnsupportedMethod => AgentAccountAuthState.UnsupportedMethod,
                _ => AgentAccountAuthState.Unreadable,
            },
            // Allowlisted tokens only: the tier ("pro") and the *name* of the variable or key source.
            auth.Kind == ClaudeCodeAuthKind.Subscription ? auth.SubscriptionType : null,
            auth.EnvironmentVariableName ?? auth.ApiKeySource,
            executablePath);

        internal static AgentAccountCommandResult Map(ClaudeCodeAccountCommandResult result) => new(
            result.State switch
            {
                ClaudeCodeAccountCommandState.Completed => AgentAccountCommandOutcome.Completed,
                ClaudeCodeAccountCommandState.StillRunning => AgentAccountCommandOutcome.StillRunning,
                ClaudeCodeAccountCommandState.ExecutableUnavailable => AgentAccountCommandOutcome.ExecutableUnavailable,
                ClaudeCodeAccountCommandState.NoVisibleTerminal => AgentAccountCommandOutcome.NoVisibleTerminal,
                _ => AgentAccountCommandOutcome.StartFailed,
            },
            result.AuthStatus is { } auth ? Map(auth, null, null) : null);
    }
}
