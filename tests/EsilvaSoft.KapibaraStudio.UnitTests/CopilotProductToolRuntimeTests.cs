using System.Text.Json;
using Avalonia.Headless;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Desktop.Agents;
using EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Copilot;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;
using GitHub.Copilot;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

/// <summary>Checks that Copilot tool requests still pass through AgentRuntime's trusted binding and registry.</summary>
[TestFixture]
public sealed class CopilotProductToolRuntimeTests
{
    private const string ToolName = "get_workspace_context";
    private const string ToolPayload = "{\"context\":\"synthetic workspace\"}";

    [Test]
    public async Task FakeCopilotRuntimeRegistersActiveBufferProposalInProductionDesktopStore()
    {
        Assert.That(OperatingSystem.IsWindows(), Is.True, "The fake stdio adapter uses PowerShell on Windows.");
        var headless = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(UiTestApp).Assembly);
        await headless.Dispatch(async () =>
        {
            const string original = "db.syntheticItems.find({}).limit(10);\n";
            using var workspaceContext = new WorkspaceTestContext();
            var store = new AgentEditProposalStore();
            var workspaceSource = new DesktopAgentWorkspaceContextSource();
            using var workspace = new WorkspaceViewModel(workspaceContext.Workspace, workspaceContext.Repository,
                agentWorkspaceContext: workspaceSource, agentEditProposals: store);
            await workspace.InitializeAsync();
            var originTab = workspace.ActiveTab!;
            originTab.Text = original; // Deliberately unsaved: the proposal must use the editor buffer, not disk.
            var workspaceContextSnapshot = workspace.CaptureWorkspace();
            var tabId = originTab.Id.ToString("N");
            Assert.Multiple(() =>
            {
                Assert.That(workspaceContextSnapshot.TabId, Is.EqualTo(tabId));
                Assert.That(workspaceContextSnapshot.BufferText, Is.EqualTo(original));
                Assert.That(originTab.FilePath, Is.Null.Or.Empty);
            });
            var providerId = CopilotSubscriptionAgentProvider.Id;
            var permissions = AgentProviderPermissions.Default(providerId) with
            {
                ExternalDestinationConsentAt = DateTimeOffset.UtcNow,
                EnabledReadTools = [],
                DataSending = new AgentDataSendingPermissions { ActiveFile = true },
                EditProposals = new AgentEditProposalPermissions { ActiveFile = true, OtherWorkspaceFiles = false },
                Workspace = new AgentWorkspacePermissions { UseFilesFolder = false }
            };
            var plan = AgentModePolicy.Plan(AgentOperationMode.Agent, permissions,
                new AgentPlatformFacts(HasWorkspaceFolder: false, ProductToolsAvailable: true, NativeToolsAvailable: false));
            Assert.That(plan.ProductTools, Is.EqualTo([AgentToolRegistry.ProposeFileEditToolName]));

            var nativeScopes = new AgentNativeChatTurnScopeRegistry();
            var policy = new EmptyPolicyProvider();
            var authority = new SessionPrincipalAuthority();
            var audit = new MemoryAuditRepository();
            var registry = new AgentToolRegistry(new EmptyProfiles(), policy, new AgentPermissionEvaluator(policy), audit,
                exposure: AgentToolExposure.Through(AgentToolExposureStage.Metadata),
                principalAuthority: authority,
                sessionTools: new AgentSessionToolPorts(new AgentMcpSessionRegistry())
                {
                    NativeChatTurnScopes = nativeScopes,
                    WorkspaceContext = workspaceSource,
                    ProposalSink = store
                },
                copilotExposure: AgentToolExposure.Through(AgentToolExposureStage.Metadata));
            Assert.That(registry.FindInProcessDescriptor(providerId, AgentToolRegistry.ProposeFileEditToolName), Is.Not.Null);

            var principal = new AgentPrincipal(Guid.NewGuid(), AgentPrincipalOrigin.Internal, 1);
            var binding = new SessionToolBinding(principal, deny: false);
            await using var sessionStore = new CopilotVolatileSessionFsStore();
            var logPath = Path.Combine(Path.GetTempPath(), $"KapibaraCopilotSynthetic-{Guid.NewGuid():N}.jsonl");
            var provider = new SessionProvider(registry, logPath, sessionStore, editProposalToolRequest: true);
            await using var runtime = new AgentRuntime([provider], toolRegistry: registry, toolBindings: binding,
                principalAuthority: authority, nativeChatTurnScopes: nativeScopes);
            string? providerSessionId = null;
            var sessionId = await runtime.StartSessionAsync(new AgentSessionOptions(provider.ProviderId, "fake-model", Path.GetTempPath())
            {
                PersistProviderSession = false,
                ProviderSessionObserver = update =>
                {
                    if (update.Change == AgentProviderSessionChange.Established)
                        providerSessionId = update.ProviderSessionId;
                }
            }, CancellationToken.None);
            try
            {
                var conversationId = Guid.NewGuid();
                var request = new AgentTurnRequest(AgentTurnId.New(), "Use the only planned product tool for this synthetic query.",
                    tabId, workspaceContextSnapshot.DocumentVersion ?? 0)
                {
                    Plan = plan,
                    Permissions = permissions,
                    WorkspaceContext = workspaceContextSnapshot,
                    ConversationId = conversationId,
                    Attachments = [new AgentContextAttachment(AgentAttachmentKind.ActiveFile, workspaceContextSnapshot.ActiveFileName!, null,
                        original, System.Text.Encoding.UTF8.GetByteCount(original),
                        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(original))))]
                };
                var events = new List<AgentEvent>();
                await foreach (var item in runtime.RunTurnAsync(sessionId, request, CancellationToken.None)) events.Add(item);

                var proposals = store.ForConversation(conversationId);
                Assert.That(proposals, Has.Count.EqualTo(1), string.Join(" | ", events.Select(item => $"{item.Kind}:{item.ErrorCode}")));
                var proposal = proposals.Single().Proposal;
                Assert.Multiple(() =>
                {
                    Assert.That(proposal.TargetPath, Is.Null);
                    Assert.That(proposal.TabId, Is.EqualTo(tabId));
                    Assert.That(proposal.TabId, Is.EqualTo(originTab.Id.ToString("N")), "The proposal remains anchored to the originating active editor tab.");
                    Assert.That(proposal.OriginalText, Is.EqualTo(original));
                    Assert.That(proposal.ProposedText, Is.EqualTo("db.syntheticItems.find({}).limit(5);\n"));
                    Assert.That(proposals.Single().Status, Is.EqualTo(AgentEditProposalStatus.Registered));
                    Assert.That(originTab.Text, Is.EqualTo(original), "Registering a proposal does not apply it to the editor.");
                    Assert.That(binding.Resolutions, Is.GreaterThanOrEqualTo(1), "The planned proposal call is bound to the internal principal.");
                    Assert.That(nativeScopes.Find(Guid.ParseExact(sessionId.Value, "N"), Guid.ParseExact(request.TurnId.Value, "N")), Is.Null,
                        "The exact Copilot session/turn scope is removed when the runtime turn ends.");
                    Assert.That(events.Any(item => item.Kind == AgentEventKind.ToolCompleted &&
                        item.ToolName == AgentToolRegistry.ProposeFileEditToolName && item.ToolStatus == AgentToolResultStatus.Succeeded), Is.True);
                    Assert.That(events.Any(item => item.Kind == AgentEventKind.TaskCompleted), Is.True);
                });
            }
            finally
            {
                await runtime.CloseSessionAsync(sessionId, CancellationToken.None);
                if (providerSessionId is { } nativeId)
                    await runtime.DeleteProviderSessionAsync(provider.ProviderId, nativeId, CancellationToken.None);
                if (File.Exists(logPath)) File.Delete(logPath);
            }
            return true;
        }, CancellationToken.None);
    }

    [TestCase(false, "success", 1, null, "Succeeded")]
    [TestCase(true, "denied", 0, null, "Denied")]
    [TestCase(false, "denied", 1, "ConfirmationRejected", "Denied")]
    [TestCase(false, "denied", 1, "ConfirmationExpired", "Denied")]
    [TestCase(false, "failure", 1, "ConfirmationUnavailable", "Failed")]
    public async Task ProductToolResultPassesThroughRuntimeBindingAndRegistry(
        bool denyBinding, string expectedResultType, int expectedInvocations, string? registryErrorCode,
        string expectedStatus)
    {
        var logPath = Path.Combine(Path.GetTempPath(), $"KapibaraCopilotTool-{Guid.NewGuid():N}.jsonl");
        var sessionRoot = Path.Combine(Path.GetTempPath(), $"KapibaraCopilotSessions-{Guid.NewGuid():N}");
        try
        {
            await using var sessionStore = new CopilotPersistentSessionFsStore(sessionRoot);
            var registry = new SessionToolRegistry(registryErrorCode);
            var principal = new AgentPrincipal(Guid.NewGuid(), AgentPrincipalOrigin.Internal, 1);
            var binding = new SessionToolBinding(principal, denyBinding);
            var authority = new SessionPrincipalAuthority();
            var provider = new SessionProvider(registry, logPath, sessionStore);
            await using var runtime = new AgentRuntime([provider], toolRegistry: registry,
                toolBindings: binding, principalAuthority: authority);
            var sessionId = await runtime.StartSessionAsync(new AgentSessionOptions(provider.ProviderId, "fake-model")
            {
                ReservedProviderSessionId = Guid.NewGuid().ToString("D"),
                PersistProviderSession = true,
            }, CancellationToken.None);
            var request = new AgentTurnRequest(AgentTurnId.New(), "Leia o contexto autorizado", "tab-test", 1)
            {
                Plan = new AgentTurnPlan(AgentOperationMode.Agent, [], [], [], [ToolName],
                    AgentProposalHandling.Disabled, false, AgentConfirmationCategories.WorkspaceContextRead),
            };

            var events = new List<AgentEvent>();
            await foreach (var item in runtime.RunTurnAsync(sessionId, request, CancellationToken.None)) events.Add(item);

            Assert.That(File.Exists(logPath), Is.True,
                string.Join(" | ", events.Select(item => $"{item.Kind}:{item.ErrorCode}:{item.Text}")));
            var lines = File.ReadAllLines(logPath);
            var responseLine = lines.Single(line =>
            {
                using var document = JsonDocument.Parse(line);
                return document.RootElement.GetProperty("method").GetString() == "session.tools.handlePendingToolCall";
            });
            using var response = JsonDocument.Parse(responseLine);
            var result = response.RootElement.GetProperty("params").GetProperty("result");
            Assert.Multiple(() =>
            {
                Assert.That(events.Any(item => item.Kind == AgentEventKind.ToolRequested && item.ToolName == ToolName), Is.True);
                Assert.That(events.Any(item => item.Kind == AgentEventKind.TaskCompleted), Is.True);
                Assert.That(events.Single(item => item.Kind is AgentEventKind.ToolCompleted or AgentEventKind.ToolFailed)
                    .ToolStatus?.ToString(), Is.EqualTo(expectedStatus));
                Assert.That(registry.Invocations, Is.EqualTo(expectedInvocations));
                Assert.That(binding.Resolutions, Is.GreaterThanOrEqualTo(1));
                Assert.That(result.GetProperty("resultType").GetString(), Is.EqualTo(expectedResultType));
                if (!denyBinding && registryErrorCode is null)
                {
                    Assert.That(result.GetProperty("textResultForLlm").GetString(), Is.EqualTo(ToolPayload));
                    Assert.That(registry.LastDestination, Is.EqualTo(AgentOutputDestination.ProviderExternal(provider.ProviderId)));
                    Assert.That(registry.LastPrincipal, Is.EqualTo(principal));
                }
            });
        }
        finally
        {
            if (File.Exists(logPath)) File.Delete(logPath);
            if (Directory.Exists(sessionRoot)) Directory.Delete(sessionRoot, recursive: true);
        }
    }

    private sealed class SessionProvider(IAgentToolRegistry registry, string logPath,
        ICopilotSessionFsStore sessionStore, bool editProposalToolRequest = false) : IAgentProvider
    {
        public string ProviderId => CopilotSubscriptionAgentProvider.Id;

        public Task<IAgentSession> CreateSessionAsync(AgentSessionOptions options, CancellationToken cancellationToken)
        {
            var script = FindRuntimeScript();
            var powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell", "v1.0", "powershell.exe");
            Assert.That(File.Exists(powershell), Is.True);
            var client = new CopilotClient(new CopilotClientOptions
            {
                Connection = RuntimeConnection.ForStdio(powershell,
                    ["-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script,
                        "-MissingSession", editProposalToolRequest ? "-EditProposalToolRequest" : "-ProductToolRequest",
                        "-ContractLogPath", logPath]),
                UseLoggedInUser = false,
                Mode = CopilotClientMode.CopilotCli,
                BaseDirectory = Path.Combine(Path.GetTempPath(), "KapibaraStudioCopilotFakeTests"),
            });
            return Task.FromResult<IAgentSession>(new CopilotSubscriptionAgentSession(registry, options, client, sessionStore));
        }
    }

    private sealed class EmptyProfiles : IConnectionProfileRepository
    {
        public Task<IReadOnlyList<ConnectionProfile>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ConnectionProfile>>([]);
        public Task SaveAsync(ConnectionProfile profile, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(Guid profileId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class EmptyPolicyProvider : IAgentAuthorizationPolicyProvider
    {
        public Task<AgentAuthorizationPolicySnapshot?> LoadAsync(Guid principalId, CancellationToken cancellationToken) =>
            Task.FromResult<AgentAuthorizationPolicySnapshot?>(null);
    }

    private sealed class MemoryAuditRepository : IAgentAuditRepository
    {
        private readonly List<AgentAuditEvent> _events = [];
        public Task AppendAsync(AgentAuditEvent entry, CancellationToken cancellationToken = default)
        {
            lock (_events) _events.Add(entry);
            return Task.CompletedTask;
        }
        public Task<IReadOnlyList<AgentAuditEvent>> GetRecentAsync(int maximum = 100, CancellationToken cancellationToken = default)
        {
            lock (_events) return Task.FromResult<IReadOnlyList<AgentAuditEvent>>(_events.Take(maximum).ToArray());
        }
        public Task<IReadOnlyList<AgentAuditEvent>> GetPendingAsync(int maximum = 100, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AgentAuditEvent>>([]);
    }

    private sealed class SessionToolRegistry(string? errorCode = null) : IAgentToolRegistry
    {
        private static readonly AgentToolDescriptor Descriptor = new(ToolName, 1, AgentToolRisk.ReadOnly,
            [AgentPermission.ReadMetadata]);
        public int Invocations { get; private set; }
        public AgentPrincipal? LastPrincipal { get; private set; }
        public AgentOutputDestination? LastDestination { get; private set; }
        public IReadOnlyList<AgentToolDescriptor> GetDescriptors() => [Descriptor];
        public AgentToolDescriptor? FindDescriptor(string? name) => name == ToolName ? Descriptor : null;
        public AgentToolDescriptor? FindInProcessDescriptor(string providerId, string? name) =>
            providerId == CopilotSubscriptionAgentProvider.Id ? FindDescriptor(name) : null;
        public string? GetInputSchemaJson(string? name) => name == ToolName
            ? "{\"type\":\"object\",\"properties\":{\"scope\":{\"type\":\"string\"}}}" : null;
        public string? GetInProcessInputSchemaJson(string providerId, string? name) =>
            FindInProcessDescriptor(providerId, name) is null ? null
                : "{\"type\":\"object\",\"properties\":{\"scope\":{\"type\":\"string\"}}}";
        public string? GetOutputSchemaJson(string? name) => name == ToolName
            ? "{\"type\":\"object\",\"properties\":{\"context\":{\"type\":\"string\"}}}" : null;
        public Task<AgentToolInvocationResult> InvokeAsync(AgentPrincipal? principal,
            AgentInvocationContext? invocationContext, AgentOutputDestination? destination,
            AgentOutputDataScope? outputDataScope, string? name, string? argumentsJson,
            CancellationToken cancellationToken = default)
        {
            Assert.That(name, Is.EqualTo(ToolName));
            Assert.That(argumentsJson, Does.Contain("active"));
            Invocations++;
            LastPrincipal = principal;
            LastDestination = destination;
            return Task.FromResult(errorCode is null
                ? AgentToolInvocationResult.Success(ToolPayload)
                : AgentToolInvocationResult.Failure(errorCode));
        }
    }

    private sealed class SessionToolBinding(AgentPrincipal principal, bool deny) : IAgentToolBindingProvider
    {
        public int Resolutions { get; private set; }
        public Task<AgentToolBinding?> ResolveAsync(AgentSessionId sessionId, AgentTurnId turnId,
            string providerId, string toolName, CancellationToken cancellationToken)
        {
            Resolutions++;
            return Task.FromResult<AgentToolBinding?>(deny ? null : new AgentToolBinding(principal,
                AgentOutputDestination.ProviderExternal(providerId), AgentOutputDataScope.Metadata));
        }
    }

    private sealed class SessionPrincipalAuthority : IAgentPrincipalAuthority
    {
        public Task<bool> IsCurrentAsync(AgentPrincipal principal, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
        public Task<AgentPrincipalIssueResult> IssueInternalAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<Guid> GetInternalPrincipalIdAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentChannelEnrollmentResult> EnrollExternalChannelAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentPrincipalIssueResult> AuthenticateExternalAsync(Guid channelId, string proof,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentChannelRevocationStatus> RevokeExternalChannelAsync(Guid channelId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> RecoverPendingChannelsAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private static string FindRuntimeScript()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "tests",
                "EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Tests", "Copilot", "FakeCopilotRuntime.ps1");
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException("Fake Copilot runtime was not found.");
    }
}
