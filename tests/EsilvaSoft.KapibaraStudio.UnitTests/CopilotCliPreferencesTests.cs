using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Desktop.Agents;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture, Category("Unit")]
public sealed class CopilotCliPreferencesTests
{
    private static string Cli(string name) => Path.GetFullPath(Path.Combine(Path.DirectorySeparatorChar.ToString(), "kapibara-test-cli", name, "copilot.exe"));
    private static AgentChatServicesFactory Factory(Configuration configuration,
        AgentProviderAvailabilityService? availability = null) =>
        new(() => AgentChatServices.Unavailable, availability) { CopilotCliConfiguration = configuration };

    [Test]
    public async Task FailedSaveKeepsActiveAndDurableSelectionAndRetryRecovers()
    {
        using var context = new WorkspaceTestContext();
        var oldPath = Cli("previous-cli");
        var newPath = Cli("replacement-cli");
        await context.Repository.SaveSessionAsync(new() { Preferences = new() { CopilotCliExecutablePath = oldPath } });
        var configuration = new Configuration();
        using var workspace = new WorkspaceViewModel(context.Workspace, context.Repository,
            agentChat: Factory(configuration), timeProvider: new ManualTimeProvider());
        await workspace.InitializeAsync();
        context.Repository.SessionWriteFailure = new IOException("synthetic save failure");

        Assert.ThrowsAsync<IOException>(() => workspace.SaveCopilotCliExecutablePathAsync(newPath));
        Assert.That(configuration.ExecutablePath, Is.EqualTo(oldPath));
        Assert.That((await context.Repository.LoadSessionAsync()).Preferences.CopilotCliExecutablePath, Is.EqualTo(oldPath));
        Assert.That(workspace.SessionStatus, Does.Contain("synthetic save failure"));

        context.Repository.SessionWriteFailure = null;
        await workspace.SaveCopilotCliExecutablePathAsync(newPath);
        Assert.That(configuration.ExecutablePath, Is.EqualTo(newPath));
        Assert.That((await context.Repository.LoadSessionAsync()).Preferences.CopilotCliExecutablePath, Is.EqualTo(newPath));

        var recoveredConfiguration = new Configuration();
        using var recovered = new WorkspaceViewModel(context.Workspace, context.Repository,
            agentChat: Factory(recoveredConfiguration), timeProvider: new ManualTimeProvider());
        await recovered.InitializeAsync();
        Assert.That(recoveredConfiguration.ExecutablePath, Is.EqualTo(newPath));
    }

    [Test]
    public async Task PendingPersistenceDoesNotActivateSelection()
    {
        using var context = new WorkspaceTestContext();
        var original = Cli("active-cli");
        var replacement = Cli("pending-cli");
        await context.Repository.SaveSessionAsync(new() { Preferences = new() { CopilotCliExecutablePath = original } });
        var sessions = new ControlledSessions(context.Repository);
        var configuration = new Configuration();
        using var workspace = new WorkspaceViewModel(context.Workspace, sessions,
            agentChat: Factory(configuration), timeProvider: new ManualTimeProvider());
        await workspace.InitializeAsync();
        var step = sessions.Enqueue();
        var saving = workspace.SaveCopilotCliExecutablePathAsync(replacement);
        var pending = await step.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(pending.Preferences.CopilotCliExecutablePath, Is.EqualTo(replacement));
        Assert.That(configuration.ExecutablePath, Is.EqualTo(original));
        Assert.That((await context.Repository.LoadSessionAsync()).Preferences.CopilotCliExecutablePath, Is.EqualTo(original));
        step.Completion.SetResult();
        await saving.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(configuration.ExecutablePath, Is.EqualTo(replacement));
    }

    [Test]
    public async Task ConcurrentFailedSaveCannotRollBackLaterSuccessfulSelection()
    {
        using var context = new WorkspaceTestContext();
        var original = Cli("original-cli");
        var rejected = Cli("rejected-cli");
        var accepted = Cli("accepted-cli");
        await context.Repository.SaveSessionAsync(new() { Preferences = new() { CopilotCliExecutablePath = original } });
        var configuration = new Configuration();
        var sessions = new ControlledSessions(context.Repository);
        using var workspace = new WorkspaceViewModel(context.Workspace, sessions,
            agentChat: Factory(configuration), timeProvider: new ManualTimeProvider());
        await workspace.InitializeAsync();
        var first = sessions.Enqueue();
        var second = sessions.Enqueue();
        var firstSave = workspace.SaveCopilotCliExecutablePathAsync(rejected);
        await first.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var secondSave = workspace.SaveCopilotCliExecutablePathAsync(accepted);
        Assert.That(second.Entered.Task.IsCompleted, Is.False);
        Assert.That(configuration.ExecutablePath, Is.EqualTo(original));

        first.Completion.SetException(new IOException("synthetic first save failure"));
        Assert.ThrowsAsync<IOException>(async () => await firstSave.WaitAsync(TimeSpan.FromSeconds(5)));
        var pendingSecond = await second.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(pendingSecond.Preferences.CopilotCliExecutablePath, Is.EqualTo(accepted));
        Assert.That(configuration.ExecutablePath, Is.EqualTo(original));
        second.Completion.SetResult();
        await secondSave.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(configuration.ExecutablePath, Is.EqualTo(accepted));
        await workspace.SaveSessionAsync();
        Assert.That((await context.Repository.LoadSessionAsync()).Preferences.CopilotCliExecutablePath, Is.EqualTo(accepted),
            "A later regular autosave must retain the successful second selection.");
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task RestoreAppliesSelectionBeforeSavedProviderAccountCheck(bool hasOverride)
    {
        using var context = new WorkspaceTestContext();
        var saved = hasOverride ? Cli("restored-cli") : null;
        await context.Repository.SaveSessionAsync(new()
        {
            Preferences = new()
            {
                CopilotCliExecutablePath = saved,
                AgentPanel = new() { SelectedProviderId = AgentProviderIds.GitHubCopilotSubscription, IsOpen = false },
            },
        });
        var configuration = new Configuration { ExecutablePath = Cli("stale-process-selection") };
        var accounts = new Accounts(configuration);
        var availability = new AgentProviderAvailabilityService(new Catalog(), accounts: accounts);
        var factory = Factory(configuration, availability);
        using var workspace = new WorkspaceViewModel(context.Workspace, context.Repository,
            agentChat: factory, timeProvider: new ManualTimeProvider());
        await workspace.InitializeAsync();
        var observed = await accounts.CheckedPath.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(observed, Is.EqualTo(saved));
        Assert.That(configuration.ExecutablePath, Is.EqualTo(saved));
        Assert.That(configuration.Validations, Is.Zero, "Restore must not require the installed file to exist.");
        Assert.That(factory.IsCreated, Is.False, "A collapsed panel does not compose chat services.");
    }

    private sealed class Configuration : ICopilotCliConfiguration
    {
        public string? ExecutablePath { get; set; }
        public int Validations { get; private set; }
        public string? ValidateExecutablePath(string? path) { Validations++; return path?.Trim(); }
        public string? ResolveExecutablePath() => ExecutablePath;
    }

    private sealed class SaveStep
    {
        public TaskCompletionSource<WorkspaceSession> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class ControlledSessions(IWorkspaceSessionRepository backing) : IWorkspaceSessionRepository
    {
        private readonly Queue<SaveStep> _steps = new();
        public SaveStep Enqueue() { var step = new SaveStep(); _steps.Enqueue(step); return step; }
        public Task<WorkspaceSession> LoadSessionAsync(CancellationToken cancellationToken = default) => backing.LoadSessionAsync(cancellationToken);
        public async Task SaveSessionAsync(WorkspaceSession session, CancellationToken cancellationToken = default)
        {
            if (_steps.TryDequeue(out var step))
            {
                step.Entered.SetResult(session);
                await step.Completion.Task.WaitAsync(cancellationToken);
            }
            await backing.SaveSessionAsync(session, cancellationToken);
        }
    }

    private sealed class Catalog : IAgentProviderCatalog
    {
        public IReadOnlyList<AgentProviderPresentation> List() =>
        [new(AgentProviderIds.GitHubCopilotSubscription, "Copilot", AgentDataDestinationKind.External, true,
            ["auto"], [AgentAuthenticationMethod.OfficialCliDelegated], AgentProviderAuthState.Configured)];
    }

    private sealed class Accounts(Configuration configuration) : IAgentAccountManager
    {
        public TaskCompletionSource<string?> CheckedPath { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public AgentAccountCheckPolicy DescribeCheckPolicy(string providerId) => new(true, true);
        public Task<AgentAccountStatus> CheckAsync(string providerId, CancellationToken cancellationToken)
        {
            CheckedPath.TrySetResult(configuration.ExecutablePath);
            return Task.FromResult(new AgentAccountStatus(AgentAccountInstallState.Installed, "synthetic", AgentAccountAuthState.Subscription));
        }
        public Task<AgentAccountCommandResult> SignInAsync(string providerId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AgentAccountCommandResult> SignOutAsync(string providerId, bool userConfirmedGlobalSignOut, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
