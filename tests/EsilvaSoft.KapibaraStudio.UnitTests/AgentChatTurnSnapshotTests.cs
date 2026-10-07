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

    [TestCase(false)]
    [TestCase(true)]
    public async Task AttachmentPreparationCompletingAfterConversationSwitchCannotChangeNewDraft(bool fileRemoved)
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(UiTestApp).Assembly);
        await session.Dispatch(async () =>
        {
            LocalizationViewModel.Current.Language = "pt-BR";
            var root = SyntheticPaths.Combine("kapibara-memory", "origin-preparation");
            var nextRoot = SyntheticPaths.Combine("kapibara-memory", "next-preparation");
            var path = Path.Combine(root, "origin.txt");
            var files = new MemoryAgentFiles();
            files.AddDirectory(root);
            files.AddDirectory(nextRoot);
            files.Set(path, Encoding.UTF8.GetBytes("origin attachment"));
            files.Set(Path.Combine(nextRoot, "next.txt"), Encoding.UTF8.GetBytes("next attachment"));
            var reader = new GatedFileReader(files);
            var permissions = AgentProviderPermissions.Default("local") with
            {
                ExternalDestinationConsentAt = DateTimeOffset.UnixEpoch,
                AutomaticContext = new AgentAutomaticContextPermissions { ActiveFile = false, TabMetadata = false },
            };
            var runtime = new ChannelAgentRuntime();
            var presentation = FakeAgentCatalog.Local("local", "Local de teste") with
                { SupportsTurnPlan = true, SupportsToolCalling = true, SupportsNativeTools = true };
            var host = new AgentChatTabFixture { WorkspaceFolder = root, Version = 7 };
            await using var chat = new AgentChatViewModel(new AgentChatServices(runtime, new FakeAgentCatalog(presentation), null)
                { Permissions = new FakeAgentPermissionsRepository(permissions), FileReader = reader, PathProbe = files }, host);
            await chat.Initialization;
            chat.Items.Add(new AgentChatMessageItem(AgentChatRole.User, "earlier message"));
            var origin = chat.ActiveConversation;
            var originChip = chat.AddWorkspaceFile(path)!;
            Assert.That(originChip, Is.Not.Null);
            chat.ComposerText = "origin prompt";
            var send = chat.SendCommand.ExecuteAsync(null);
            await reader.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            try
            {
                chat.NewConversationCommand.Execute(null);
                Assert.That(chat.ActiveConversation, Is.Not.SameAs(origin));
                var next = chat.ActiveConversation;
                chat.RemoveChipCommand.Execute(originChip);
                host.WorkspaceFolder = nextRoot;
                host.Version = 8;
                chat.OnWorkspaceContextChanged();
                var nextChip = chat.AddWorkspaceFile(Path.Combine(nextRoot, "next.txt"))!;
                Assert.That(nextChip, Is.Not.Null);
                chat.ComposerText = "next draft";
                chat.ActiveConversation = origin;
                Assert.That(chat.State, Is.EqualTo(AgentChatState.Connecting),
                    "Returning to a conversation with a pending attachment read must restore its in-flight state.");
                chat.ActiveConversation = next;
                Assert.That(chat.State, Is.EqualTo(AgentChatState.Ready),
                    "Switching away again must restore the new conversation's idle state.");
                if (fileRemoved) files.Remove(path);
                reader.Release.TrySetResult();
                if (!fileRemoved)
                {
                    await AgentChatWait.UntilAsync(() => runtime.LastRequest is not null);
                    var request = runtime.LastRequest!;
                    Assert.That(request.ConversationId, Is.EqualTo(origin.Id));
                    Assert.That(request.WorkspaceContext!.WorkspaceFolder, Is.EqualTo(root));
                    Assert.That(request.DocumentVersion, Is.EqualTo(7));
                    Assert.That(request.Attachments.Single().Content, Is.EqualTo("origin attachment"));
                    Assert.That(chat.State, Is.EqualTo(AgentChatState.Ready),
                        "Preparing the background turn must not put the new conversation into Generating.");
                    runtime.Push(request.TurnId, AgentEventKind.MessageDelta, "origin response", message: AgentMessageId.New());
                    runtime.Push(request.TurnId, AgentEventKind.TaskCompleted, outcome: AgentTurnOutcome.Completed);
                }
                await send;
                Assert.Multiple(() =>
                {
                    Assert.That(chat.Chips, Does.Contain(nextChip), "A completed older read cannot consume the new draft attachment.");
                    Assert.That(nextChip.Error, Is.Null, "An older attachment failure cannot annotate the new draft chip by index.");
                    Assert.That(chat.ComposerText, Is.EqualTo("next draft"));
                    Assert.That(next.Items, Is.Empty, "Results remain in the originating conversation.");
                    Assert.That(next.IsBusy, Is.False);
                    if (fileRemoved) Assert.That(runtime.LastRequest, Is.Null);
                    else Assert.That(origin.Items.OfType<AgentChatMessageItem>().Last().Content, Is.EqualTo("origin response"));
                });
            }
            finally
            {
                reader.Release.TrySetResult();
                if (runtime.LastRequest is { } pending)
                    runtime.Push(pending.TurnId, AgentEventKind.TaskCompleted, outcome: AgentTurnOutcome.Completed);
                await send.WaitAsync(TimeSpan.FromSeconds(5));
            }
            return true;
        }, CancellationToken.None);
    }

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
