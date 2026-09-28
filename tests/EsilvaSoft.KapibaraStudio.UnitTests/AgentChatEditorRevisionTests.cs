using Avalonia.Headless;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Desktop.Agents;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

/// <summary>
/// Regression for ADR-055: the editor revision used by the agent chat lived in the removed per-tab assistant partial.
/// These tests use the real <see cref="WorkspaceTabViewModel"/> (no tab fixture) so the revision source is the tab itself.
/// </summary>
[TestFixture, NonParallelizable]
public sealed class AgentChatEditorRevisionTests
{
    private const string Selection = "db.orders.find({})";

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
    public void EveryEditorChangeAdvancesTheRevisionPublishedToTheAgentSnapshot()
    {
        using var context = new WorkspaceTestContext();
        using var tab = new WorkspaceTabViewModel(context.Workspace) { Text = Selection };
        var before = tab.EditorRevision;
        var snapshotBefore = tab.CaptureAgentChatSnapshot();

        tab.Text = Selection + " ";
        tab.Text = Selection; // Restoring the same text is still a new revision.

        Assert.That(snapshotBefore.DocumentVersion, Is.EqualTo(before));
        Assert.That(tab.EditorRevision, Is.EqualTo(before + 2));
        Assert.That(tab.CaptureAgentChatSnapshot().DocumentVersion, Is.EqualTo(tab.EditorRevision));
    }

    [Test]
    public async Task DirectSendCapturesTheCurrentRevisionOfTheRealEditor()
    {
        await RunOnUiAsync(async () =>
        {
            using var context = new WorkspaceTestContext();
            using var tab = new WorkspaceTabViewModel(context.Workspace) { Text = Selection };
            var provider = new ScriptedAgentProvider("local");
            await using var runtime = new AgentRuntime([provider], new AllowingInteractionAuthority());
            await using var chat = new AgentChatViewModel(
                new AgentChatServices(runtime, new FakeAgentCatalog(FakeAgentCatalog.Local("local", "Local de teste")),
                    new FakeAgentContextProvider(), null, null, null)
                {
                    Permissions = new FakeAgentPermissionsRepository(),
                },
                new RealTabHost(tab));
            await chat.Initialization;

            chat.ComposerText = "revise a seleção";
            tab.Text = Selection + "\n// editado";
            var revisionAtSend = tab.EditorRevision;
            await chat.SendCommand.ExecuteAsync(null);

            Assert.That(provider.Sessions.Single().Requests.Single().DocumentVersion, Is.EqualTo(revisionAtSend));
        });
    }

    private sealed class RealTabHost(WorkspaceTabViewModel tab) : IAgentChatHost
    {
        public AgentWorkspaceContext CaptureWorkspace() => tab.CaptureAgentChatSnapshot();
        public string? WorkspaceFolder => null;
        public IReadOnlyList<AgentConnectionChoice> ListConnections() => [];
        public Task<IAgentBufferEditor?> OpenEditorAsync(string targetPath, string? tabId) =>
            Task.FromResult<IAgentBufferEditor?>(null);
        public void OnPanelPreferencesChanged() { }
    }
}
