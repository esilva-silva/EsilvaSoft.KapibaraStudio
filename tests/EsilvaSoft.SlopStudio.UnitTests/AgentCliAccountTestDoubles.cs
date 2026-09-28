using EsilvaSoft.SlopStudio.Core.Agents;
using EsilvaSoft.SlopStudio.Desktop.Agents;

namespace EsilvaSoft.SlopStudio.UnitTests;

/// <summary>
/// Scripted CLI-delegated account manager: no process, no network, no model. Every call is counted so tests can prove
/// that opening screens runs nothing and that sign-out only happens after an explicit confirmation.
/// </summary>
internal sealed class FakeCliAccountManager : IAgentCliAccountManager
{
    public const string ProviderId = "cli-sub";

    public static readonly AgentCliProviderProfile TestProfile = new("Claude Code", "Anthropic", "claude auth login",
        "~/.claude/projects", "~/.claude/.credentials.json", "~/.claude");

    public AgentCliAccountStatus Status { get; set; } = new(AgentCliInstallState.NotFound, null, AgentCliAuthState.NotChecked);

    public AgentCliCommandResult? SignInResult { get; set; }

    public AgentCliCommandResult? SignOutResult { get; set; }

    /// <summary>When set, sign-in waits for it (simulates the visible CLI window still open).</summary>
    public TaskCompletionSource? SignInGate { get; set; }

    public string? WorkspaceDirectory { get; set; }

    public int Checks { get; private set; }

    public int SignIns { get; private set; }

    public int SignOuts { get; private set; }

    public bool? LastSignOutConfirmation { get; private set; }

    public AgentCliProviderProfile? Describe(string providerId) => providerId == ProviderId ? TestProfile : null;

    /// <summary>Scripted provider decision; null derives it from the candidate (accepted when given, none otherwise).</summary>
    public Func<string?, AgentCliReadScope>? ScopeFor { get; set; }

    public List<string?> ScopeCandidates { get; } = [];

    /// <summary>Candidate used when the caller gives none (settings/chat without a UI capture delegate).</summary>
    public AgentCliReadScope DescribeReadScope(string providerId, string? candidateWorkspace)
    {
        if (providerId != ProviderId)
        {
            return AgentCliReadScope.None;
        }

        var candidate = candidateWorkspace ?? WorkspaceDirectory;
        ScopeCandidates.Add(candidate);
        return ScopeFor?.Invoke(candidate) ?? (candidate is null
            ? new AgentCliReadScope(null, DedicatedFolder, false, AgentCliReadScopeRejection.NotProvided)
            : new AgentCliReadScope(candidate, candidate, true, AgentCliReadScopeRejection.None));
    }

    public static string DedicatedFolder => Path.Combine(Path.GetTempPath(), "SlopStudio.ClaudeCode", "empty");

    public Task<AgentCliAccountStatus> CheckAsync(string providerId, CancellationToken cancellationToken)
    {
        Checks++;
        return Task.FromResult(Status);
    }

    public async Task<AgentCliCommandResult> SignInAsync(string providerId, CancellationToken cancellationToken)
    {
        SignIns++;
        if (SignInGate is { } gate)
        {
            await gate.Task.WaitAsync(cancellationToken);
        }

        return SignInResult ?? new AgentCliCommandResult(AgentCliCommandOutcome.Completed, Status);
    }

    public Task<AgentCliCommandResult> SignOutAsync(string providerId, bool userConfirmedGlobalSignOut, CancellationToken cancellationToken)
    {
        SignOuts++;
        LastSignOutConfirmation = userConfirmedGlobalSignOut;
        return Task.FromResult(SignOutResult ?? new AgentCliCommandResult(AgentCliCommandOutcome.Completed,
            new AgentCliAccountStatus(AgentCliInstallState.Installed, null, AgentCliAuthState.SignedOut)));
    }

    public static AgentCliAccountStatus Subscription(string tier = "pro") =>
        new(AgentCliInstallState.Installed, "2.1.268", AgentCliAuthState.Subscription, tier, ExecutablePath: FakeExecutablePath);

    public static AgentCliAccountStatus SignedOut() =>
        new(AgentCliInstallState.Installed, "2.1.268", AgentCliAuthState.SignedOut, ExecutablePath: FakeExecutablePath);

    public static AgentCliAccountStatus BlockedByApiKey() =>
        new(AgentCliInstallState.Installed, "2.1.268", AgentCliAuthState.BlockedEnvironment, BlockingSource: "ANTHROPIC_API_KEY",
            ExecutablePath: FakeExecutablePath);

    public static string FakeExecutablePath => OperatingSystem.IsWindows()
        ? @"C:\Ferramentas\claude-teste\claude.exe"
        : "/opt/claude-teste/bin/claude";
}

/// <summary>Catalog double whose entries tests rewrite between explicit refreshes; counts both refresh kinds.</summary>
internal sealed class MutableAgentCatalog(params AgentProviderPresentation[] providers) : IAgentProviderCatalog
{
    public List<AgentProviderPresentation> Providers { get; } = [.. providers];

    public int FullRefreshes { get; private set; }

    public List<string> ProviderRefreshes { get; } = [];

    /// <summary>Applied on the next per-provider refresh (what the provider would report after the action).</summary>
    public Func<AgentProviderPresentation, AgentProviderPresentation>? OnRefresh { get; set; }

    public IReadOnlyList<AgentProviderPresentation> List() => [.. Providers];

    public Task RefreshAsync(CancellationToken cancellationToken)
    {
        FullRefreshes++;
        return Task.CompletedTask;
    }

    public Task RefreshProviderAsync(string providerId, CancellationToken cancellationToken)
    {
        ProviderRefreshes.Add(providerId);
        if (OnRefresh is { } update)
        {
            for (var i = 0; i < Providers.Count; i++)
            {
                if (Providers[i].ProviderId == providerId)
                {
                    Providers[i] = update(Providers[i]);
                }
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>"Claude (assinatura)"-like entry: CLI-delegated, external, family label "Claude".</summary>
    public static AgentProviderPresentation Subscription(bool available = true, AgentProviderAuthState auth = AgentProviderAuthState.Configured,
        string? reason = null) =>
        new(FakeCliAccountManager.ProviderId, "Claude (assinatura)", AgentDataDestinationKind.External, available,
            available ? ["sonnet", "opus", "haiku"] : [], [AgentAuthenticationMethod.OfficialCliDelegated], auth,
            UnavailableReason: available ? null : reason, FamilyName: "Claude");

    /// <summary>"Claude (Anthropic API)"-like entry: API key, same family label.</summary>
    public static AgentProviderPresentation Api(AgentProviderAuthState auth = AgentProviderAuthState.Configured) =>
        new("api-key", "Claude (Anthropic API)", AgentDataDestinationKind.External, auth == AgentProviderAuthState.Configured,
            ["claude-sonnet"], [AgentAuthenticationMethod.ApiKey], auth, SupportsToolCalling: true, FamilyName: "Claude");
}
