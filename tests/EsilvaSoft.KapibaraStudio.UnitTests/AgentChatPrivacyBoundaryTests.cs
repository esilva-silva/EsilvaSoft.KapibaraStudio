using Avalonia.Headless;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Application.Agents.Editing;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Desktop.Agents;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;
using EsilvaSoft.KapibaraStudio.Testing;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture, Category("Unit"), NonParallelizable]
public sealed class AgentChatPrivacyBoundaryTests
{
    private static string MemoryFolder => SyntheticPaths.Combine("kapibara-memory", "privacy");
    private static Task<bool> RunOnUiAsync(Func<Task> body)
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(UiTestApp).Assembly);
        return session.Dispatch(async () =>
        {
            LocalizationViewModel.Current.Language = "pt-BR";
            await body();
            return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task DisabledFilesFolderAndActiveFileStayOutOfSessionAndSystemPrompt()
    {
        await RunOnUiAsync(async () =>
        {
            var runtime = new ChannelAgentRuntime();
            var host = new PromptHost(@"C:\Users\PrivateUserCanary\PrivateFolderCanary", "PrivateFileCanary.js");
            var permissions = AgentProviderPermissions.Default("local") with
            {
                Workspace = new AgentWorkspacePermissions { UseFilesFolder = false },
                DataSending = new AgentDataSendingPermissions { ActiveFile = false },
                AutomaticContext = new AgentAutomaticContextPermissions { ActiveFile = false, TabMetadata = false },
            };
            await using var chat = new AgentChatViewModel(
                new AgentChatServices(runtime, new FakeAgentCatalog(FakeAgentCatalog.Local("local", "Local")), null)
                {
                    Permissions = new FakeAgentPermissionsRepository(permissions),
                }, host);
            await chat.Initialization;
            chat.ComposerText = "Explique";

            var send = chat.SendCommand.ExecuteAsync(null);
            await AgentChatWait.UntilAsync(() => runtime.LastRequest is not null);
            var request = runtime.LastRequest!;
            Assert.Multiple(() =>
            {
                Assert.That(runtime.SessionOptions.Single().WorkingDirectory, Is.Null);
                Assert.That(request.SystemPrompt, Does.Contain("Workspace: nenhuma pasta aberta")
                    .And.Contain("Arquivo ativo: nenhum"));
                Assert.That(request.SystemPrompt, Does.Not.Contain("PrivateUserCanary")
                    .And.Not.Contain("PrivateFolderCanary").And.Not.Contain("PrivateFileCanary"));
                Assert.That(request.Plan?.NativeTools, Is.Empty);
            });
            runtime.Push(request.TurnId, AgentEventKind.TaskCompleted, outcome: AgentTurnOutcome.Completed);
            await send;
        });
    }

    [Test]
    public async Task RevokingFilesFolderStartsANewSessionWithoutThePreviousDirectory()
    {
        await RunOnUiAsync(async () =>
        {
            var runtime = new ChannelAgentRuntime();
            var folder = MemoryFolder;
            var files = new MemoryAgentFiles();
            files.AddDirectory(folder);
            var host = new PromptHost(folder, null);
            var permissions = AgentProviderPermissions.Default("local") with
            {
                AutomaticContext = new AgentAutomaticContextPermissions { ActiveFile = false, TabMetadata = false },
            };
            await using var chat = new AgentChatViewModel(
                new AgentChatServices(runtime, new FakeAgentCatalog(FakeAgentCatalog.Local("local", "Local")), null)
                {
                    Permissions = new FakeAgentPermissionsRepository(permissions),
                    PathProbe = files,
                    FileReader = files,
                }, host);
            await chat.Initialization;
            chat.ComposerText = "Primeiro";
            var first = chat.SendCommand.ExecuteAsync(null);
            await AgentChatWait.UntilAsync(() => runtime.LastRequest is not null);
            var firstRequest = runtime.LastRequest!;
            Assert.That(runtime.SessionOptions.Single().WorkingDirectory, Is.EqualTo(folder));
            runtime.Push(firstRequest.TurnId, AgentEventKind.TaskCompleted, outcome: AgentTurnOutcome.Completed);
            await first;

            chat.OnPermissionsSaved(permissions with { Workspace = new AgentWorkspacePermissions { UseFilesFolder = false } });
            chat.ComposerText = "Segundo";
            var second = chat.SendCommand.ExecuteAsync(null);
            await AgentChatWait.UntilAsync(() => runtime.LastRequest is { } request && request.TurnId != firstRequest.TurnId);
            var secondRequest = runtime.LastRequest!;
            Assert.Multiple(() =>
            {
                Assert.That(runtime.SessionOptions.Select(static option => option.WorkingDirectory), Is.EqualTo(new[] { folder, null }));
                Assert.That(secondRequest.SystemPrompt, Does.Contain("Workspace: nenhuma pasta aberta"));
            });
            runtime.Push(secondRequest.TurnId, AgentEventKind.TaskCompleted, outcome: AgentTurnOutcome.Completed);
            await second;
        });
    }

    [Test]
    public async Task CapturedTabIdCannotFallBackToAnotherTabShowingTheSameFile()
    {
        await RunOnUiAsync(async () =>
        {
            var file = Path.Combine(MemoryFolder, "query.js");
            var textFiles = new MemoryTextFiles();
            await textFiles.SaveAsync(file, "");
            var paths = new MemoryAgentFiles();
            paths.Set(file, []);
            {
                using var context = new WorkspaceTestContext(textFiles: textFiles);
                using var workspace = new WorkspaceViewModel(context.Workspace, context.Repository, agentPaths: paths);
                await workspace.InitializeAsync();
                var tab = await workspace.OpenTextFileAsync(file);
                var editor = new StubEditor();
                tab.EditorBufferProvider = () => editor;
                var count = workspace.Tabs.Count;

                var missing = await workspace.OpenEditorAsync(file, Guid.NewGuid().ToString("N"));
                Assert.Multiple(() =>
                {
                    Assert.That(missing, Is.Null);
                    Assert.That(workspace.Tabs, Has.Count.EqualTo(count));
                    Assert.That(workspace.ActiveTab, Is.SameAs(tab));
                });
                Assert.That(await workspace.OpenEditorAsync(file, tab.Id.ToString("N")), Is.SameAs(editor));
            }
        });
    }

    private sealed class PromptHost(string folder, string? activeFileName) : IAgentChatHost
    {
        public string? WorkspaceFolder => folder;

        public AgentWorkspaceContext CaptureWorkspace() => new(DateTimeOffset.UtcNow,
            WorkspaceFolder: folder, ActiveFileName: activeFileName, TabId: "tab-a", DocumentVersion: 1);

        public IReadOnlyList<AgentConnectionChoice> ListConnections() => [];

        public Task<IAgentBufferEditor?> OpenEditorAsync(string? targetPath, string? tabId) =>
            Task.FromResult<IAgentBufferEditor?>(null);

        public void OnPanelPreferencesChanged() { }
    }

    private sealed class StubEditor : IAgentBufferEditor
    {
        public string Text => "";

        public bool TryApply(IReadOnlyList<LineDiffTextEdit> edits) => true;
    }
}
