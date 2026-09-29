using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Copilot;
using GitHub.Copilot;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

/// <summary>Checks that Copilot tool requests still pass through AgentRuntime's trusted binding and registry.</summary>
[TestFixture]
public sealed class CopilotProductToolRuntimeTests
{
    private const string ToolName = "get_workspace_context";
    private const string ToolPayload = "{\"context\":\"synthetic workspace\"}";

    [TestCase(false, "success", 1)]
    [TestCase(true, "denied", 0)]
    public async Task ProductToolResultPassesThroughRuntimeBindingAndRegistry(
        bool denyBinding, string expectedResultType, int expectedInvocations)
    {
        var logPath = Path.Combine(Path.GetTempPath(), $"KapibaraCopilotTool-{Guid.NewGuid():N}.jsonl");
        try
        {
            var registry = new SessionToolRegistry();
            var principal = new AgentPrincipal(Guid.NewGuid(), AgentPrincipalOrigin.Internal, 1);
            var binding = new SessionToolBinding(principal, denyBinding);
            var authority = new SessionPrincipalAuthority();
            var provider = new SessionProvider(registry, logPath);
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
                Assert.That(registry.Invocations, Is.EqualTo(expectedInvocations));
                Assert.That(binding.Resolutions, Is.GreaterThanOrEqualTo(1));
                Assert.That(result.GetProperty("resultType").GetString(), Is.EqualTo(expectedResultType));
                if (!denyBinding)
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
        }
    }

    private sealed class SessionProvider(IAgentToolRegistry registry, string logPath) : IAgentProvider
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
                        "-MissingSession", "-ProductToolRequest", "-ContractLogPath", logPath]),
                UseLoggedInUser = false,
                Mode = CopilotClientMode.CopilotCli,
                BaseDirectory = Path.Combine(Path.GetTempPath(), "KapibaraStudioCopilotFakeTests"),
            });
            return Task.FromResult<IAgentSession>(new CopilotSubscriptionAgentSession(registry, options, client));
        }
    }

    private sealed class SessionToolRegistry : IAgentToolRegistry
    {
        private static readonly AgentToolDescriptor Descriptor = new(ToolName, 1, AgentToolRisk.ReadOnly,
            [AgentPermission.ReadMetadata]);
        public int Invocations { get; private set; }
        public AgentPrincipal? LastPrincipal { get; private set; }
        public AgentOutputDestination? LastDestination { get; private set; }
        public IReadOnlyList<AgentToolDescriptor> GetDescriptors() => [Descriptor];
        public AgentToolDescriptor? FindDescriptor(string? name) => name == ToolName ? Descriptor : null;
        public string? GetInputSchemaJson(string? name) => name == ToolName
            ? "{\"type\":\"object\",\"properties\":{\"scope\":{\"type\":\"string\"}}}" : null;
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
            return Task.FromResult(AgentToolInvocationResult.Success(ToolPayload));
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
