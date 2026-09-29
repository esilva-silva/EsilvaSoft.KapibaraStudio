using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Copilot;
using System.Text.Json;
using GitHub.Copilot;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

#pragma warning disable GHCP001 // Homologação manual do SDK experimental pinado, em sessão isolada.
[TestFixture]
internal sealed class CopilotProductToolManualTests
{
    private const string ToolName = "get_workspace_context";
    private const string ToolPayload = "{\"context\":\"synthetic workspace context; no real paths or files\"}";

    [Test, Explicit("Homologação manual Windows com Copilot: o modelo chama uma ferramenta sintética, sem banco nem dados do workspace real.")]
    public async Task WindowsOfficialModelCallsSyntheticProductToolThroughAgentRuntime()
    {
        Assert.That(OperatingSystem.IsWindows(), Is.True, "A homologação P7-COP está limitada ao Windows.");

        string modelId;
        await using (var accountClient = new CopilotClient(CopilotRuntimeSettings.AccountClientOptions()))
        {
            await accountClient.StartAsync(CancellationToken.None);
            var auth = await accountClient.GetAuthStatusAsync(CancellationToken.None);
            Assert.That(auth.IsAuthenticated && string.Equals(auth.AuthType, "user", StringComparison.Ordinal),
                Is.True, "A CLI oficial precisa reportar uma conta Copilot de usuário.");
            modelId = (await accountClient.ListModelsAsync(CancellationToken.None))
                .Select(static model => model.Id)
                .FirstOrDefault(static id => id is { Length: > 0 and <= 128 } && !id.Any(char.IsControl))
                ?? throw new AssertionException("Nenhum modelo Copilot elegível foi retornado.");
        }

        var store = new CopilotVolatileSessionFsStore();
        var registry = new SyntheticWorkspaceToolRegistry();
        var principal = new AgentPrincipal(Guid.NewGuid(), AgentPrincipalOrigin.Internal, 1);
        var binding = new TrustedTestBinding(principal);
        var provider = new OfficialSessionProvider(registry, store);
        await using var runtime = new AgentRuntime([provider], toolRegistry: registry,
            toolBindings: binding, principalAuthority: new TestPrincipalAuthority());
        string? providerSessionId = null;
        var sessionId = await runtime.StartSessionAsync(new AgentSessionOptions(provider.ProviderId, modelId,
            Environment.CurrentDirectory)
        {
            PersistProviderSession = false,
            ProviderSessionObserver = update =>
            {
                if (update.Change == AgentProviderSessionChange.Established)
                    providerSessionId = update.ProviderSessionId;
            },
        }, CancellationToken.None);

        try
        {
            using var turnTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            var request = new AgentTurnRequest(AgentTurnId.New(),
                "Use the get_workspace_context tool now with scope 'active'. Do not invent context. After the tool returns, state only that the synthetic context was received.",
                "synthetic-copilot-tool-check", 1)
            {
                Plan = new AgentTurnPlan(AgentOperationMode.Agent, [], [], [], [ToolName],
                    AgentProposalHandling.Disabled, false, AgentConfirmationCategories.WorkspaceContextRead),
            };

            var events = new List<AgentEvent>();
            await foreach (var item in runtime.RunTurnAsync(sessionId, request, turnTimeout.Token))
                events.Add(item);

            Assert.Multiple(() =>
            {
                Assert.That(registry.Invocations, Is.EqualTo(1), "The real model must request the declared synthetic product tool once.");
                Assert.That(binding.Resolutions, Is.GreaterThanOrEqualTo(1), "Tool dispatch must pass through the trusted runtime binding.");
                Assert.That(registry.LastPrincipal, Is.EqualTo(principal));
                Assert.That(registry.LastDestination, Is.EqualTo(AgentOutputDestination.ProviderExternal(provider.ProviderId)));
                Assert.That(events.Any(item => item.Kind == AgentEventKind.ToolRequested && item.ToolName == ToolName), Is.True);
                Assert.That(events.Any(item => item.Kind == AgentEventKind.ToolCompleted && item.ToolName == ToolName), Is.True);
                Assert.That(events.Any(item => item.Kind == AgentEventKind.TaskCompleted), Is.True);
            });
            TestContext.Progress.WriteLine("Runtime Copilot oficial chamou a ferramenta sintética através de AgentRuntime; nenhum texto de prompt/resposta, caminho ou dado real foi registrado.");
        }
        finally
        {
            try
            {
                await runtime.CloseSessionAsync(sessionId, CancellationToken.None);
                if (providerSessionId is { } nativeId)
                    await runtime.DeleteProviderSessionAsync(provider.ProviderId, nativeId, CancellationToken.None);
            }
            finally
            {
                await store.DisposeAsync();
            }
        }
    }

    [Test, Explicit("Homologação manual Windows com conta Copilot autenticada: modelo oficial chama get_workspace_context no registry real usando somente uma pasta temporária sintética.")]
    public async Task WindowsOfficialModelCallsWorkspaceContextThroughProductionRegistry()
    {
        Assert.That(OperatingSystem.IsWindows(), Is.True, "A homologação P7-COP está limitada ao Windows.");
        using var rig = new AgentSessionToolsTestRig();
        var permissions = AgentProviderPermissions.Default(CopilotSubscriptionAgentProvider.Id) with
        {
            ExternalDestinationConsentAt = DateTimeOffset.UtcNow,
            DataSending = new AgentDataSendingPermissions { TabMetadata = true },
            Workspace = new AgentWorkspacePermissions { UseFilesFolder = true }
        };
        var plan = new AgentTurnPlan(AgentOperationMode.Agent, [], [], [], [ToolName],
            AgentProposalHandling.Disabled, false, AgentConfirmationCategories.WorkspaceContextRead);
        string modelId;
        await using (var accountClient = new CopilotClient(CopilotRuntimeSettings.AccountClientOptions()))
        {
            await accountClient.StartAsync(CancellationToken.None);
            var auth = await accountClient.GetAuthStatusAsync(CancellationToken.None);
            Assert.That(auth.IsAuthenticated && string.Equals(auth.AuthType, "user", StringComparison.Ordinal),
                Is.True, "A CLI oficial precisa reportar uma conta Copilot de usuário.");
            modelId = (await accountClient.ListModelsAsync(CancellationToken.None))
                .Select(static model => model.Id)
                .FirstOrDefault(static id => id is { Length: > 0 and <= 128 } && !id.Any(char.IsControl))
                ?? throw new AssertionException("Nenhum modelo Copilot elegível foi retornado.");
        }

        var store = new CopilotVolatileSessionFsStore();
        var principal = new AgentPrincipal(Guid.NewGuid(), AgentPrincipalOrigin.Internal, 1);
        var binding = new TrustedTestBinding(principal);
        var provider = new OfficialSessionProvider(rig.Registry, store);
        await using var runtime = new AgentRuntime([provider], toolRegistry: rig.Registry,
            toolBindings: binding, principalAuthority: new TestPrincipalAuthority(),
            nativeChatTurnScopes: rig.NativeChatScopes);
        string? providerSessionId = null;
        var sessionId = await runtime.StartSessionAsync(new AgentSessionOptions(provider.ProviderId, modelId,
            rig.WorkspaceFolder)
        {
            PersistProviderSession = false,
            ProviderSessionObserver = update =>
            {
                if (update.Change == AgentProviderSessionChange.Established)
                    providerSessionId = update.ProviderSessionId;
            },
        }, CancellationToken.None);

        try
        {
            using var turnTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            var request = new AgentTurnRequest(AgentTurnId.New(),
                "Call get_workspace_context exactly once. It contains only a temporary synthetic workspace path and no database connection. After it returns, say only that the synthetic context was received.",
                "synthetic-copilot-workspace-tool-check", 1)
            {
                Plan = plan,
                Permissions = permissions,
                WorkspaceContext = new AgentWorkspaceContext(DateTimeOffset.UtcNow, rig.WorkspaceFolder),
                ConversationId = Guid.NewGuid()
            };

            var events = new List<AgentEvent>();
            await foreach (var item in runtime.RunTurnAsync(sessionId, request, turnTimeout.Token))
                events.Add(item);

            Assert.Multiple(() =>
            {
                Assert.That(binding.Resolutions, Is.GreaterThanOrEqualTo(1));
                Assert.That(events.Any(item => item.Kind == AgentEventKind.ToolRequested && item.ToolName == ToolName), Is.True);
                Assert.That(events.Any(item => item.Kind == AgentEventKind.ToolCompleted && item.ToolName == ToolName), Is.True,
                    "The production AgentToolRegistry must authorize and execute the planned tool.");
                Assert.That(rig.Audit.Events, Is.Not.Empty, "The production registry must audit the tool invocation.");
                Assert.That(events.Any(item => item.Kind == AgentEventKind.TaskCompleted), Is.True);
            });
            TestContext.Progress.WriteLine("Modelo oficial Copilot chamou get_workspace_context no registry real com plano, permissão e snapshot temporário sintético; nenhum banco ou arquivo real foi usado.");
        }
        finally
        {
            try
            {
                await runtime.CloseSessionAsync(sessionId, CancellationToken.None);
                if (providerSessionId is { } nativeId)
                    await runtime.DeleteProviderSessionAsync(provider.ProviderId, nativeId, CancellationToken.None);
            }
            finally
            {
                await store.DisposeAsync();
            }
        }
    }

    [Test, Explicit("Homologação manual Windows: modelo Copilot chama list_connections no registry real; somente perfil sintético autorizado, sem abrir MongoDB.")]
    public async Task WindowsOfficialModelCallsMongoMetadataToolWithScopedPermission()
    {
        Assert.That(OperatingSystem.IsWindows(), Is.True, "A homologação P7-COP está limitada ao Windows.");
        using var rig = new AgentSessionToolsTestRig();
        var providerId = CopilotSubscriptionAgentProvider.Id;
        var outputDestination = AgentOutputDestination.ProviderExternal(providerId);
        var permissions = AgentProviderPermissions.Default(providerId) with
        {
            ExternalDestinationConsentAt = DateTimeOffset.UtcNow,
            DataSending = new AgentDataSendingPermissions { TabMetadata = true }
        };
        var plan = new AgentTurnPlan(AgentOperationMode.Agent, [], [], [], [AgentToolRegistry.ListConnectionsToolName],
            AgentProposalHandling.Disabled, false, AgentConfirmationCategories.MongoMetadataRead);
        string modelId;
        await using (var accountClient = new CopilotClient(CopilotRuntimeSettings.AccountClientOptions()))
        {
            await accountClient.StartAsync(CancellationToken.None);
            var auth = await accountClient.GetAuthStatusAsync(CancellationToken.None);
            Assert.That(auth.IsAuthenticated && string.Equals(auth.AuthType, "user", StringComparison.Ordinal),
                Is.True, "A CLI oficial precisa reportar uma conta Copilot de usuário.");
            modelId = (await accountClient.ListModelsAsync(CancellationToken.None))
                .Select(static model => model.Id)
                .FirstOrDefault(static id => id is { Length: > 0 and <= 128 } && !id.Any(char.IsControl))
                ?? throw new AssertionException("Nenhum modelo Copilot elegível foi retornado.");
        }

        var store = new CopilotVolatileSessionFsStore();
        var principal = new AgentPrincipal(Guid.NewGuid(), AgentPrincipalOrigin.Internal, 1);
        var binding = new TrustedTestBinding(principal);
        var provider = new OfficialSessionProvider(rig.Registry, store);
        await using var runtime = new AgentRuntime([provider], toolRegistry: rig.Registry,
            toolBindings: binding, principalAuthority: new TestPrincipalAuthority());
        string? providerSessionId = null;
        var sessionId = await runtime.StartSessionAsync(new AgentSessionOptions(provider.ProviderId, modelId,
            rig.WorkspaceFolder)
        {
            PersistProviderSession = false,
            ProviderSessionObserver = update =>
            {
                if (update.Change == AgentProviderSessionChange.Established)
                    providerSessionId = update.ProviderSessionId;
            },
        }, CancellationToken.None);

        try
        {
            var sessionKey = Guid.ParseExact(sessionId.Value, "N");
            rig.Policies.Set(principal.Id, 1,
            [
                new AgentPermissionGrant(principal.Id, AgentInvocationScope.ForSession(sessionKey),
                    rig.Profile.SourceGenerationId!.Value, AgentPermission.ReadMetadata,
                    AgentNamespaceScope.ForConnection(rig.Profile.Id), outputDestination, AgentOutputDataScope.Metadata)
            ]);
            var authorizedProfiles = await rig.Registry.InvokeAsync(principal,
                new AgentInvocationContext(provider.ProviderId, null, sessionKey, Guid.NewGuid()), outputDestination,
                AgentOutputDataScope.Metadata, AgentToolRegistry.ListConnectionsToolName, "{}");
            Assert.That(authorizedProfiles.Succeeded, Is.True, authorizedProfiles.ErrorCode);
            using (var authorizedJson = JsonDocument.Parse(authorizedProfiles.StructuredContentJson!))
            {
                var visible = authorizedJson.RootElement.GetProperty("connections").EnumerateArray().ToArray();
                Assert.That(visible, Has.Length.EqualTo(1), "Perfil sem grant precisa ficar invisível.");
                Assert.That(Guid.Parse(visible[0].GetProperty("id").GetString()!), Is.EqualTo(rig.Profile.Id));
            }
            using var turnTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            var request = new AgentTurnRequest(AgentTurnId.New(),
                "Use list_connections exactly once. The output is synthetic and may contain only the authorized connection. Do not call any database or collection tool. Then report the number of visible connections.",
                "synthetic-copilot-list-connections", 1)
            {
                Plan = plan,
                Permissions = permissions,
                ConversationId = Guid.NewGuid()
            };
            var events = new List<AgentEvent>();
            await foreach (var item in runtime.RunTurnAsync(sessionId, request, turnTimeout.Token))
                events.Add(item);

            Assert.Multiple(() =>
            {
                Assert.That(binding.Resolutions, Is.GreaterThanOrEqualTo(1));
                Assert.That(events.Any(item => item.Kind == AgentEventKind.ToolRequested &&
                    item.ToolName == AgentToolRegistry.ListConnectionsToolName), Is.True);
                Assert.That(events.Any(item => item.Kind == AgentEventKind.ToolCompleted &&
                    item.ToolName == AgentToolRegistry.ListConnectionsToolName), Is.True,
                    "The production registry must apply the session-scoped permission grant.");
                Assert.That(rig.Audit.Events, Is.Not.Empty, "The production registry must audit the metadata read.");
                Assert.That(events.Any(item => item.Kind == AgentEventKind.TaskCompleted), Is.True);
            });
            TestContext.Progress.WriteLine("Modelo oficial Copilot executou list_connections pelo registry de produção; grant restrito ao perfil sintético, sem conexão MongoDB nem URI na resposta.");
        }
        finally
        {
            try
            {
                await runtime.CloseSessionAsync(sessionId, CancellationToken.None);
                if (providerSessionId is { } nativeId)
                    await runtime.DeleteProviderSessionAsync(provider.ProviderId, nativeId, CancellationToken.None);
            }
            finally
            {
                await store.DisposeAsync();
            }
        }
    }

    private sealed class OfficialSessionProvider(IAgentToolRegistry registry, CopilotVolatileSessionFsStore store)
        : IAgentProvider, IAgentProviderSessionCleanup
    {
        public string ProviderId => CopilotSubscriptionAgentProvider.Id;

        public Task<IAgentSession> CreateSessionAsync(AgentSessionOptions options, CancellationToken cancellationToken)
        {
            var sessionFs = CopilotVolatileSessionFsStore.CreateConfiguration(
                CopilotRuntimeSettings.ResolveWorkingDirectory(options.WorkingDirectory));
            var client = new CopilotClient(CopilotRuntimeSettings.SessionClientOptions(options.WorkingDirectory, sessionFs));
            return Task.FromResult<IAgentSession>(new CopilotSubscriptionAgentSession(registry, options, client, store));
        }

        public async Task DeleteProviderSessionAsync(string providerSessionId, CancellationToken cancellationToken)
        {
            var sessionFs = CopilotVolatileSessionFsStore.CreateConfiguration(Environment.CurrentDirectory);
            await using var client = new CopilotClient(CopilotRuntimeSettings.SessionClientOptions(
                Environment.CurrentDirectory, sessionFs));
            await client.StartAsync(cancellationToken);
            if (await client.GetSessionMetadataAsync(providerSessionId, cancellationToken) is not null)
                await client.DeleteSessionAsync(providerSessionId, cancellationToken);
            await store.DeleteSessionAsync(providerSessionId, cancellationToken);
        }
    }

    private sealed class SyntheticWorkspaceToolRegistry : IAgentToolRegistry
    {
        private static readonly AgentToolDescriptor Descriptor = new(ToolName, 1, AgentToolRisk.ReadOnly,
            [AgentPermission.ReadMetadata]);
        public int Invocations { get; private set; }
        public AgentPrincipal? LastPrincipal { get; private set; }
        public AgentOutputDestination? LastDestination { get; private set; }
        public IReadOnlyList<AgentToolDescriptor> GetDescriptors() => [Descriptor];
        public AgentToolDescriptor? FindDescriptor(string? name) => name == ToolName ? Descriptor : null;
        public string? GetInputSchemaJson(string? name) => name == ToolName
            ? "{\"type\":\"object\",\"properties\":{\"scope\":{\"type\":\"string\"}},\"required\":[\"scope\"]}" : null;
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

    private sealed class TrustedTestBinding(AgentPrincipal principal) : IAgentToolBindingProvider
    {
        public int Resolutions { get; private set; }
        public Task<AgentToolBinding?> ResolveAsync(AgentSessionId sessionId, AgentTurnId turnId,
            string providerId, string toolName, CancellationToken cancellationToken)
        {
            Resolutions++;
            return Task.FromResult<AgentToolBinding?>(new AgentToolBinding(principal,
                AgentOutputDestination.ProviderExternal(providerId), AgentOutputDataScope.Metadata));
        }
    }

    private sealed class TestPrincipalAuthority : IAgentPrincipalAuthority
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
}
#pragma warning restore GHCP001
