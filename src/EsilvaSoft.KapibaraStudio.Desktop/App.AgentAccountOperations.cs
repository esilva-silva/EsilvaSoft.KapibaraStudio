using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Infrastructure.Agents.ClaudeCode;
using EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Codex;
using EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Copilot;

namespace EsilvaSoft.KapibaraStudio.Desktop;

public partial class App
{
    private static async Task<AgentAccountStatus> CheckClaudeCodeAsync(ClaudeCodeAgentProvider provider, CancellationToken cancellationToken)
    {
        var installation = await provider.DetectAsync(cancellationToken).ConfigureAwait(false);
        var install = ClaudeCodeAccountHandler.MapInstall(installation.State);
        var version = installation.Version?.ToString();
        if (install != AgentAccountInstallState.Installed)
        {
            return new AgentAccountStatus(install, version, AgentAccountAuthState.NotChecked, ExecutablePath: installation.ExecutablePath);
        }

        ClaudeCodeAuthStatus auth;
        try
        {
            auth = await provider.GetAuthenticationStatusAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            auth = new ClaudeCodeAuthStatus(ClaudeCodeAuthKind.Unreadable);
        }

        return ClaudeCodeAccountHandler.Map(auth, version, installation.ExecutablePath);
    }

    private static async Task<AgentAccountCommandResult> SignInClaudeCodeAsync(ClaudeCodeAgentProvider provider, CancellationToken cancellationToken) =>
        ClaudeCodeAccountHandler.Map(await provider.LoginAsync(cancellationToken).ConfigureAwait(false));

    private static async Task<AgentAccountCommandResult> SignOutClaudeCodeAsync(ClaudeCodeAgentProvider provider, CancellationToken cancellationToken) =>
        ClaudeCodeAccountHandler.Map(await provider.LogoutAsync(userConfirmedGlobalLogout: true, cancellationToken).ConfigureAwait(false));

    // Provider-bound async operations stay inside the composition boundary; dispatch uses Application ports.
    private static async Task<AgentAccountStatus> CheckCopilotAsync(CopilotSubscriptionAgentProvider provider, CancellationToken token)
    {
        try
        {
            var account = await provider.CheckAccountAndModelsAsync(token).ConfigureAwait(false);
            var install = account.State switch
            {
                CopilotAccountState.Unavailable => AgentAccountInstallState.CheckFailed,
                CopilotAccountState.CliNotInstalled => AgentAccountInstallState.NotFound,
                _ => AgentAccountInstallState.Installed,
            };
            return MapCopilot(account, install);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception) { return new AgentAccountStatus(AgentAccountInstallState.CheckFailed, null, AgentAccountAuthState.Unreadable); }
    }

    private static async Task<AgentAccountCommandResult> SignInCopilotAsync(CopilotSubscriptionAgentProvider provider, CancellationToken token)
    {
        try { return MapCopilotCommand(await provider.LoginAsync(token).ConfigureAwait(false)); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception) { return new AgentAccountCommandResult(AgentAccountCommandOutcome.StartFailed, await CheckCopilotAsync(provider, token).ConfigureAwait(false)); }
    }

    private static async Task<AgentAccountCommandResult> SignOutCopilotAsync(CopilotSubscriptionAgentProvider provider, CancellationToken token)
    {
        try { return MapCopilotCommand(await provider.LogoutAsync(userConfirmedGlobalLogout: true, token).ConfigureAwait(false)); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception) { return new AgentAccountCommandResult(AgentAccountCommandOutcome.StartFailed, await CheckCopilotAsync(provider, token).ConfigureAwait(false)); }
    }

    private static AgentAccountStatus MapCopilot(CopilotAccountStatus account, AgentAccountInstallState install) =>
        new(install, null, account.State switch
        {
            CopilotAccountState.Subscription => AgentAccountAuthState.Subscription,
            CopilotAccountState.NotLoggedIn => AgentAccountAuthState.SignedOut,
            CopilotAccountState.OtherAuthentication => AgentAccountAuthState.UnsupportedMethod,
            CopilotAccountState.CliNotInstalled => AgentAccountAuthState.NotChecked,
            _ => AgentAccountAuthState.Unreadable,
        });

    private static AgentAccountCommandResult MapCopilotCommand(CopilotAccountCommandResult result) => new(
        result.State switch
        {
            CopilotAccountCommandState.Completed => AgentAccountCommandOutcome.Completed,
            CopilotAccountCommandState.StillRunning => AgentAccountCommandOutcome.StillRunning,
            CopilotAccountCommandState.CommandFailed => AgentAccountCommandOutcome.CommandFailed,
            CopilotAccountCommandState.RuntimeUnavailable => AgentAccountCommandOutcome.ExecutableUnavailable,
            CopilotAccountCommandState.NoVisibleTerminal => AgentAccountCommandOutcome.NoVisibleTerminal,
            _ => AgentAccountCommandOutcome.StartFailed,
        }, result.Account is { } account ? MapCopilot(account, AgentAccountInstallState.Installed) : null);

    private static async Task<AgentAccountStatus> CheckCodexAsync(CodexSubscriptionAgentProvider provider, CancellationToken token)
    {
        try
        {
            var status = await provider.GetSubscriptionStatusAsync(token).ConfigureAwait(false);
            return new AgentAccountStatus(AgentAccountInstallState.Installed, null, status.State switch
            {
                CodexSubscriptionState.Authenticated => AgentAccountAuthState.Subscription,
                CodexSubscriptionState.SignedOut => AgentAccountAuthState.SignedOut,
                CodexSubscriptionState.OtherAuthentication => AgentAccountAuthState.UnsupportedMethod,
                _ => AgentAccountAuthState.Unreadable,
            }, status.State == CodexSubscriptionState.Authenticated ? status.PlanType : null);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception) { return new AgentAccountStatus(AgentAccountInstallState.CheckFailed, null, AgentAccountAuthState.Unreadable); }
    }

    private static Task<AgentAccountCommandResult> SignInCodexAsync(CodexSubscriptionAgentProvider provider,
        IExternalUriLauncher browser, CancellationToken token) =>
        SignInCodexWithBrowserAsync(provider.LoginAsync, cancellationToken => CheckCodexAsync(provider, cancellationToken), browser, token);

    internal static async Task<AgentAccountCommandResult> SignInCodexWithBrowserAsync(
        Func<Func<Uri, CancellationToken, Task>, CancellationToken, Task<bool>> login,
        Func<CancellationToken, Task<AgentAccountStatus>> check,
        IExternalUriLauncher browser, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(login);
        ArgumentNullException.ThrowIfNull(check);
        ArgumentNullException.ThrowIfNull(browser);
        token.ThrowIfCancellationRequested();
        try
        {
            var completed = await login(browser.OpenAsync, token).ConfigureAwait(false);
            return new AgentAccountCommandResult(completed ? AgentAccountCommandOutcome.Completed : AgentAccountCommandOutcome.StartFailed,
                await check(token).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception) { return new AgentAccountCommandResult(AgentAccountCommandOutcome.StartFailed, await check(token).ConfigureAwait(false)); }
    }

    private static async Task<AgentAccountCommandResult> SignOutCodexAsync(CodexSubscriptionAgentProvider provider, CancellationToken token)
    {
        try
        {
            await provider.LogoutAsync(confirmed: true, token).ConfigureAwait(false);
            return new AgentAccountCommandResult(AgentAccountCommandOutcome.Completed,
                await CheckCodexAsync(provider, token).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception) { return new AgentAccountCommandResult(AgentAccountCommandOutcome.StartFailed, await CheckCodexAsync(provider, token).ConfigureAwait(false)); }
    }

}
