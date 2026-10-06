using System.Text;
using Avalonia.Headless;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Desktop.Agents;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;
using EsilvaSoft.KapibaraStudio.Testing;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture, Category("Unit"), NonParallelizable]
public sealed class AgentChatTurnSnapshotTests
{
    private static readonly string[] ExpectedExclusions = ["blocked.txt"];
    private static readonly string[] ExpectedTools = [AgentToolRegistry.ListConnectionsToolName];

    [Test]
    public async Task RemovedWorkspaceFileAfterChipValidationIsNotSentToRuntime()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(UiTestApp).Assembly);
        await session.Dispatch(async () =>
        {
            LocalizationViewModel.Current.Language = "pt-BR";
            var root = SyntheticPaths.Combine("kapibara-memory", "turn-snapshot-removed-file");
            var path = Path.Combine(root, "query.txt");
            var files = new MemoryAgentFiles();
            files.AddDirectory(root);
            files.Set(path, Encoding.UTF8.GetBytes("file existed when chip was added"));
            var reader = new RemovedAfterGateFileReader(files);
            var permissions = AgentProviderPermissions.Default("local") with
            {
                ExternalDestinationConsentAt = DateTimeOffset.UnixEpoch,
                DataSending = new AgentDataSendingPermissions { WorkspaceFiles = true },
            };
            var runtime = new ChannelAgentRuntime();
            var presentation = FakeAgentCatalog.Local("local", "Local de teste") with
            {
                SupportsTurnPlan = true, SupportsToolCalling = true, SupportsNativeTools = true,
            };
            await using var chat = new AgentChatViewModel(
                new AgentChatServices(runtime, new FakeAgentCatalog(presentation), null)
                {
                    Permissions = new FakeAgentPermissionsRepository(permissions),
                    FileReader = reader,
                    PathProbe = files,
                }, new AgentChatTabFixture { WorkspaceFolder = root });
            await chat.Initialization;
            var chip = chat.AddWorkspaceFile(path);
            Assert.That(chip, Is.Not.Null);
            Assert.That(chip!.Error, Is.Null, "The file exists and is inside the workspace when attached.");

            chat.ComposerText = "Confira o arquivo";
            var send = chat.SendCommand.ExecuteAsync(null);
            await reader.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            try
            {
                // The chip passed path validation, but the bounded read observes that the file disappeared.
                files.Remove(path);
            }
            finally { reader.Release.TrySetResult(); }

            await send;
            Assert.Multiple(() =>
            {
                Assert.That(runtime.LastRequest, Is.Null, "A failed attachment resolution must stop before runtime dispatch.");
                Assert.That(chat.Chips.Single(static item => item.Kind == AgentAttachmentKind.WorkspaceFile).Error,
                    Is.EqualTo(AgentAttachmentError.NotFound));
            });
            return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task FileReadAwaitCannotChangeTheCapturedPermissionsPlanOrWorkspace()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(UiTestApp).Assembly);
        await session.Dispatch(async () =>
        {
            LocalizationViewModel.Current.Language = "pt-BR";
            var root = SyntheticPaths.Combine("kapibara-memory", "turn-snapshot");
            var connection = Guid.NewGuid();
            var exclusions = new List<string> { "blocked.txt" };
            var tools = new List<string> { AgentToolRegistry.ListConnectionsToolName };
            var connections = new List<Guid> { connection };
            var permissions = AgentProviderPermissions.Default("local") with
            {
                ExternalDestinationConsentAt = DateTimeOffset.UnixEpoch,
                Workspace = new AgentWorkspacePermissions { Exclusions = exclusions },
                EnabledReadTools = tools,
                ConnectionScope = AgentConnectionScope.Selected,
                SelectedConnectionIds = connections,
                AutomaticContext = new AgentAutomaticContextPermissions { ActiveFile = false, TabMetadata = false },
                EditProposals = new AgentEditProposalPermissions { ActiveFile = false, OtherWorkspaceFiles = false },
            };
            var files = new MemoryAgentFiles();
            files.AddDirectory(root);
            files.Set(Path.Combine(root, "query.txt"), Encoding.UTF8.GetBytes("captured attachment"));
            var reader = new GatedFileReader(files);
            var host = new AgentChatTabFixture
            {
                WorkspaceFolder = root,
                ConnectionId = connection.ToString("D"),
                Database = "origin-database",
                Collection = "origin-collection",
                Version = 7,
            };
            var runtime = new ChannelAgentRuntime();
            var presentation = FakeAgentCatalog.Local("local", "Local de teste") with
            {
                SupportsTurnPlan = true, SupportsToolCalling = true, SupportsNativeTools = true,
            };
            await using var chat = new AgentChatViewModel(
                new AgentChatServices(runtime, new FakeAgentCatalog(presentation), null)
                {
                    Permissions = new FakeAgentPermissionsRepository(permissions),
                    FileReader = reader,
                    PathProbe = files,
                }, host);
            await chat.Initialization;
            Assert.That(chat.AddWorkspaceFile(Path.Combine(root, "query.txt")), Is.Not.Null);
            chat.ComposerText = "original prompt";
            var send = chat.SendCommand.ExecuteAsync(null);
            await reader.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            try
            {
                exclusions.Clear();
                tools.Clear();
                connections.Clear();
                host.WorkspaceFolder = SyntheticPaths.Combine("kapibara-memory", "another-root");
                host.ConnectionId = Guid.NewGuid().ToString("D");
                host.Database = "new-database";
                host.Collection = "new-collection";
                host.Version = 8;
                chat.ComposerText = "next prompt";
            }
            finally { reader.Release.TrySetResult(); }

            await AgentChatWait.UntilAsync(() => runtime.LastRequest is not null);
            var request = runtime.LastRequest!;
            runtime.Push(request.TurnId, AgentEventKind.TaskCompleted, outcome: AgentTurnOutcome.Completed);
            await send;
            Assert.Multiple(() =>
            {
                Assert.That(request.UserMessage, Is.EqualTo("original prompt"));
                Assert.That(request.SystemPrompt, Does.Contain("Workspace: turn-snapshot")
                    .And.Contain(AgentToolRegistry.ListConnectionsToolName).And.Not.Contain("another-root"));
                Assert.That(request.DocumentVersion, Is.EqualTo(7));
                Assert.That(request.WorkspaceContext?.WorkspaceFolder, Is.EqualTo(root));
                Assert.That(request.WorkspaceContext?.ConnectionId, Is.EqualTo(connection.ToString("D")));
                Assert.That(request.WorkspaceContext?.DatabaseName, Is.EqualTo("origin-database"));
                Assert.That(request.WorkspaceContext?.CollectionName, Is.EqualTo("origin-collection"));
                Assert.That(runtime.SessionOptions.Single().WorkingDirectory, Is.EqualTo(root));
                Assert.That(request.Permissions!.Workspace.EffectiveExclusions, Is.EquivalentTo(ExpectedExclusions));
                Assert.That(request.Permissions.EnabledReadTools, Is.EquivalentTo(ExpectedTools));
                Assert.That(request.Permissions.SelectedConnectionIds, Is.EquivalentTo(new[] { connection }));
                Assert.That(request.Plan!.ProductTools, Is.EquivalentTo(request.Permissions.EnabledReadTools));
                Assert.That(request.Plan.AllowedConnectionIds, Is.EquivalentTo(request.Permissions.SelectedConnectionIds));
                Assert.That(request.Attachments.Single().Content, Is.EqualTo("captured attachment"));
            });
            return true;
        }, CancellationToken.None);
    }

    private sealed class GatedFileReader(MemoryAgentFiles files) : IAgentBoundedFileReader
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public AgentFileReadResult Read(string fullPath, int maximumBytes) => files.Read(fullPath, maximumBytes);
        public async Task<AgentFileReadResult> ReadAsync(string fullPath, int maximumBytes, CancellationToken token)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(token);
            return await files.ReadAsync(fullPath, maximumBytes, token);
        }
    }

    private sealed class RemovedAfterGateFileReader(MemoryAgentFiles files) : IAgentBoundedFileReader
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public AgentFileReadResult Read(string fullPath, int maximumBytes) => files.Read(fullPath, maximumBytes);

        public async Task<AgentFileReadResult> ReadAsync(string fullPath, int maximumBytes, CancellationToken token)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(token);
            return await files.ReadAsync(fullPath, maximumBytes, token);
        }
    }
}
