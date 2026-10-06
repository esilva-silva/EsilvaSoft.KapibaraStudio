using System.Collections.Concurrent;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Threading;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Application.Agents.Editing;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Desktop;
using EsilvaSoft.KapibaraStudio.Desktop.Agents;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;
using EsilvaSoft.KapibaraStudio.Testing;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

[TestFixture, NonParallelizable, Category("Ui"), Category("Integration")]
public sealed class AgentProposalModeSnapshotTests
{
    private static readonly ThemeVariant[] Themes = [ThemeVariant.Light, ThemeVariant.Dark];

    [TestCase(AgentOperationMode.Agent, AgentOperationMode.Automatic, false, false)]
    [TestCase(AgentOperationMode.AskConfirmations, AgentOperationMode.Automatic, false, false)]
    [TestCase(AgentOperationMode.Automatic, AgentOperationMode.Agent, true, false)]
    [TestCase(AgentOperationMode.Automatic, AgentOperationMode.Automatic, true, false)]
    [TestCase(AgentOperationMode.Agent, AgentOperationMode.Automatic, false, true,
        TestName = "LegacyProposalRequiresReviewWhenConversationIsAutomatic")]
    public Task DelayedProposalUsesOriginatingTurnHandlingAndCapturedBuffer(
        AgentOperationMode origin, AgentOperationMode current, bool shouldApply, bool legacy)
        => HeadlessUnitTestSession.GetOrStartForAssembly(typeof(IntegrationUiTestApp).Assembly).Dispatch(async () =>
        {
            LocalizationViewModel.Current.Language = "pt-BR";
            var notifications = new ConcurrentQueue<Action>();
            var store = new AgentEditProposalStore(notifications.Enqueue);
            var host = new BufferHost();
            using var resolver = store.AttachTextResolver((_, tabId) => tabId == BufferHost.OriginTab ? host.Editor.Text : null);
            await using var chat = new AgentChatViewModel(AgentChatServices.Unavailable with { Proposals = store }, host);
            await chat.Initialization;
            chat.SelectedMode = chat.Modes.Single(option => option.Mode == origin);
            var rig = new ProposalRig(origin, chat.ActiveConversation.Id, store, host.CaptureWorkspace());
            if (legacy)
            {
                Assert.That(LineDiff.TryCompute(BufferHost.Original, BufferHost.Proposed, out var hunks), Is.True);
                var proposal = new AgentEditProposal(Guid.NewGuid(), chat.ActiveConversation.Id, null, BufferHost.OriginTab,
                    AgentEditProposalStore.Sha256(BufferHost.Original), BufferHost.Original, BufferHost.Proposed,
                    hunks, DateTimeOffset.UtcNow) { TargetName = "Consulta sintética" };
                Assert.That(proposal.Handling, Is.EqualTo(AgentProposalHandling.ReviewRequired));
                Assert.That(store.Submit(proposal).Registered, Is.True);
            }
            else
            {
                var result = await rig.InvokeAsync();
                Assert.That(result.Succeeded, Is.True, result.ErrorCode);
            }
            Assert.That(notifications, Has.Count.EqualTo(1));
            Assert.That(host.Editor.Text, Is.EqualTo(BufferHost.Original), "The sink only registers before UI dispatch.");
            Assert.That(host.OpenCalls, Is.Zero);

            // The originating turn has finished. Mode and Explorer/active tab now belong to the next request.
            chat.SelectedMode = chat.Modes.Single(option => option.Mode == current);
            host.ActiveTab = "different-tab";
            while (notifications.TryDequeue(out var notification)) notification();
            var card = chat.Items.OfType<AgentEditProposalCardItem>().Single();
            Assert.Multiple(() =>
            {
                Assert.That(card.IsAutomatic, Is.EqualTo(shouldApply));
                Assert.That(host.OpenCalls, Is.EqualTo(shouldApply ? 1 : 0));
                Assert.That(host.LastOpenedTab, Is.EqualTo(shouldApply ? BufferHost.OriginTab : null));
                Assert.That(host.LastOpenedPath, Is.Null);
                Assert.That(host.Editor.Text, Is.EqualTo(shouldApply ? BufferHost.Proposed : BufferHost.Original));
                Assert.That(host.Editor.EditGroups, Is.EqualTo(shouldApply ? 1 : 0));
                Assert.That(card.Entry!.HunkStates.Single(), Is.EqualTo(shouldApply
                    ? AgentEditHunkState.Applied : AgentEditHunkState.Pending));
                Assert.That(card.Entry.Proposal.TabId, Is.EqualTo(BufferHost.OriginTab));
                Assert.That(card.Entry.Proposal.TargetPath, Is.Null);
                Assert.That(rig.MetadataCalls, Is.Zero);
            });

            var window = new Window { Width = 420, Height = 620, Content = new AgentChatPanel { DataContext = chat } };
            window.Show();
            try
            {
                foreach (var theme in Themes)
                {
                    Avalonia.Application.Current!.RequestedThemeVariant = theme;
                    window.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                    var directory = UiEvidenceDirectory.Current();
                    Directory.CreateDirectory(directory);
                    using var frame = window.CaptureRenderedFrame();
                    Assert.That(frame, Is.Not.Null);
                    frame!.Save(Path.Combine(directory, $"agent-proposal-origin-{origin}-current-{current}-legacy-{legacy}-{theme}.png"),
                        new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
                }
            }
            finally
            {
                window.Close();
                Avalonia.Application.Current!.RequestedThemeVariant = ThemeVariant.Default;
            }
            AgentEditProposalReviewViewModel? review = null;
            chat.ProposalReviewRequested += (_, requested) => review = requested;
            await card.ReviewCommand.ExecuteAsync(null);
            while (notifications.TryDequeue(out var notification)) notification();
            Assert.Multiple(() =>
            {
                Assert.That(review, Is.Not.Null);
                Assert.That(review!.IsAutomatic, Is.EqualTo(shouldApply));
                Assert.That(review.Entry.Proposal.TabId, Is.EqualTo(BufferHost.OriginTab));
                Assert.That(host.OpenCalls, Is.EqualTo(shouldApply ? 2 : 1));
                Assert.That(host.LastOpenedTab, Is.EqualTo(BufferHost.OriginTab));
                Assert.That(host.LastOpenedPath, Is.Null);
                Assert.That(host.Editor.Text, Is.EqualTo(shouldApply ? BufferHost.Proposed : BufferHost.Original));
                Assert.That(host.Editor.EditGroups, Is.EqualTo(shouldApply ? 1 : 0));
            });
            return true;
        }, CancellationToken.None);

    private sealed class ProposalRig : AgentSessionToolDoubles
    {
        private readonly Guid _session = Guid.NewGuid();
        private readonly Guid _turn = Guid.NewGuid();
        private readonly AgentPrincipal _principal = new(Guid.NewGuid(), AgentPrincipalOrigin.Internal, 1);
        private readonly AgentToolRegistry _registry;
        private readonly ThrowingMetadataSource _metadata = new();
        public int MetadataCalls => _metadata.Calls;

        public ProposalRig(AgentOperationMode mode, Guid conversation, AgentEditProposalStore store, AgentWorkspaceContext snapshot)
        {
            const string provider = AgentProviderIds.GitHubCopilotSubscription;
            var permissions = AgentProviderPermissions.Default(provider) with
            {
                ExternalDestinationConsentAt = DateTimeOffset.UtcNow,
                DataSending = new AgentDataSendingPermissions { ActiveFile = true },
                EditProposals = new AgentEditProposalPermissions { ActiveFile = true }
            };
            var plan = AgentModePolicy.Plan(mode, permissions, new AgentPlatformFacts(false, true, false));
            var scopes = new AgentNativeChatTurnScopeRegistry();
            Assert.That(scopes.Register(new(_session, _turn, provider, plan, permissions, snapshot)
            {
                ConversationId = conversation,
                ActiveFileAttachmentResolved = true,
                ActiveFileAttachmentMatchesSnapshot = true
            }), Is.True);
            var policies = new MapPolicies();
            policies.Set(_principal.Id, 1, []);
            _registry = new AgentToolRegistry(new NoProfiles(), policies, new AgentPermissionEvaluator(policies),
                new MemoryAudit(), metadata: _metadata, principalAuthority: new TestAgentPrincipalAuthority(),
                exposure: AgentToolExposure.Through(AgentToolExposureStage.Metadata),
                copilotExposure: AgentToolExposure.Through(AgentToolExposureStage.Metadata),
                sessionTools: new(new AgentMcpSessionRegistry())
                {
                    NativeChatTurnScopes = scopes, ProposalSink = store, WorkspaceContext = new FakeWorkspace(),
                    ConfirmationPrompt = new FakeConfirmation()
                });
        }

        public Task<AgentToolInvocationResult> InvokeAsync() => _registry.InvokeAsync(_principal,
            new AgentInvocationContext(AgentProviderIds.GitHubCopilotSubscription, null, _session, _turn),
            AgentOutputDestination.ProviderExternal(AgentProviderIds.GitHubCopilotSubscription),
            AgentToolOutputScopes.For(AgentToolRegistry.ProposeFileEditToolName), AgentToolRegistry.ProposeFileEditToolName,
            JsonSerializer.Serialize(new { target = "active_buffer", new_content = BufferHost.Proposed }));
    }

    private sealed class NoProfiles : IConnectionProfileRepository
    {
        public Task<IReadOnlyList<ConnectionProfile>> GetAllAsync(CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("A buffer proposal must not read the connection catalog.");
        public Task SaveAsync(ConnectionProfile profile, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(Guid profileId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class BufferHost : IAgentChatHost
    {
        public const string OriginTab = "origin-tab";
        public const string Original = "const value = 1;\n";
        public const string Proposed = "const value = 2;\n";
        public string ActiveTab { get; set; } = OriginTab;
        public string? WorkspaceFolder => null;
        public Buffer Editor { get; } = new();
        public int OpenCalls { get; private set; }
        public string? LastOpenedTab { get; private set; }
        public string? LastOpenedPath { get; private set; }
        public AgentWorkspaceContext CaptureWorkspace() => new(DateTimeOffset.UtcNow, TabId: ActiveTab,
            DocumentVersion: 1, ActiveFileName: "Consulta sintética", BufferText: Editor.Text);
        public IReadOnlyList<AgentConnectionChoice> ListConnections() => [];
        public Task<IAgentBufferEditor?> OpenEditorAsync(string? targetPath, string? tabId)
        {
            OpenCalls++;
            LastOpenedTab = tabId;
            LastOpenedPath = targetPath;
            return Task.FromResult<IAgentBufferEditor?>(tabId == OriginTab && targetPath is null ? Editor : null);
        }
        public void OnPanelPreferencesChanged() { }
    }

    private sealed class Buffer : IAgentBufferEditor
    {
        public string Text { get; private set; } = BufferHost.Original;
        public int EditGroups { get; private set; }
        public bool TryApply(IReadOnlyList<LineDiffTextEdit> edits)
        {
            foreach (var edit in edits) Text = Text.Remove(edit.Offset, edit.Length).Insert(edit.Offset, edit.Replacement);
            EditGroups++;
            return true;
        }
    }
}
