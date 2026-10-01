using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Application.Agents.Broker;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Infrastructure.Agents.ClaudeCode;
using EsilvaSoft.KapibaraStudio.Infrastructure;
using EsilvaSoft.KapibaraStudio.SystemAdapters;
using EsilvaSoft.KapibaraStudio.SystemAdapters.ClaudeCode;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests.ClaudeCode;

/// <summary>
/// Verificação opcional com o Claude Code REAL instalado e já logado pelo próprio usuário (nunca em CI). Faz no máximo
/// uma chamada ao modelo <c>haiku</c> com prompt sintético; consome a cota da assinatura. Não substitui a homologação
/// manual C-01..C-36 (GCL-8). Precisa de um ambiente sem as variáveis bloqueantes (ex.: fora de outra sessão Claude Code).
/// </summary>
[TestFixture]
[Explicit("Usa o Claude Code real e a assinatura do usuário (1 chamada haiku); somente execução manual autorizada.")]
[Category("ClaudeCodeReal")]
[CancelAfter(180_000)]
public sealed class ClaudeCodeRealCliTests
{
    [Test]
    public async Task RealCliAnswersOneSyntheticTurnThroughTheSubscription()
    {
        var provider = new ClaudeCodeAgentProvider(new ClaudeCodeAgentProviderOptions
        {
            AllowedModelIds = ["haiku"],
            DefaultModel = "haiku",
            MaxTurns = 1,
            ProbeTimeout = TimeSpan.FromSeconds(20),
        }, new LocalClaudeCodeSystem());
        var status = await provider.GetStatusAsync(CancellationToken.None);
        Assume.That(status.IsAvailable, Is.True, "Claude Code ausente, não logado por assinatura ou ambiente bloqueado: " + status.UnavailableCode);

        await using var session = (ClaudeCodeAgentSession)await provider.CreateSessionAsync(
            new AgentSessionOptions(ClaudeCodeAgentProvider.Id), CancellationToken.None);
        var events = await ClaudeCodeFixture.RunAsync(session, "Teste sintético do KapibaraStudio. Responda apenas: ok");

        Assert.Multiple(() =>
        {
            Assert.That(ClaudeCodeFixture.Error(events), Is.Null);
            Assert.That(ClaudeCodeFixture.Text(events).ToLowerInvariant(), Does.Contain("ok"));
            Assert.That(session.LastTurn!.Outcome, Is.EqualTo(AgentTurnOutcome.Completed));
        });
    }

    [Test]
    public async Task RealCliReadsSyntheticWorkspaceContextThroughProductionMcpRegistry()
    {
        var mcpServer = Environment.GetEnvironmentVariable("SLOP_CLAUDE_MCP_SERVER");
        Assume.That(string.IsNullOrWhiteSpace(mcpServer), Is.False,
            "Defina SLOP_CLAUDE_MCP_SERVER para o executável MCP publicado do produto.");
        var mcpServerPath = mcpServer ?? string.Empty;
        Assume.That(Path.IsPathFullyQualified(mcpServerPath), Is.True, "O caminho do proxy MCP deve ser absoluto.");
        Assume.That(File.Exists(mcpServerPath), Is.True, "O executável MCP publicado não foi encontrado.");
        if (!OperatingSystem.IsWindows())
        {
            var mode = File.GetUnixFileMode(mcpServerPath);
            Assume.That((mode & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0,
                Is.True, "O executável MCP publicado precisa ter permissão de execução.");
        }

        var root = Path.Combine(Path.GetTempPath(), "kapibarastudio-claude-mcp-", Guid.NewGuid().ToString("N"));
        var workspaceRoot = Path.Combine(root, "synthetic-workspace-" + Guid.NewGuid().ToString("N"));
        var cleanupVerified = true; // No channel/proof exists until CreateSessionAsync is reached.
        Directory.CreateDirectory(workspaceRoot);
        try
        {
            var services = new ServiceCollection();
            services.AddKapibaraStudioInfrastructure(Path.Combine(root, "workspace.db"),
                new AgentPlatformOptions { ToolExposureStage = AgentToolExposureStage.Metadata });
            services.AddSingleton<IAgentWorkspaceContextSource>(new SyntheticWorkspaceContextSource(workspaceRoot));
            await using var serviceProvider = services.BuildServiceProvider();

            // The override exists only in this explicit test. Desktop composition keeps its platform gate unchanged.
            await using var mcp = new AgentMcpChannelProvisioner(
                serviceProvider.GetRequiredService<IAgentToolRegistry>(),
                serviceProvider.GetRequiredService<IAgentPrincipalAuthority>(),
                serviceProvider.GetRequiredService<IAgentAuthorizationPolicyRepository>(),
                serviceProvider.GetRequiredService<IConnectionProfileRepository>(),
                serviceProvider.GetRequiredService<AgentMcpSessionRegistry>(),
                AgentToolExposureStage.Metadata,
                serviceProvider.GetRequiredService<IHostPlatformSnapshot>(),
                serviceProvider.GetRequiredService<IAgentWorkspacePathProbe>(),
                serverExecutable: mcpServerPath,
                platformSupported: true,
                brokerTransport: serviceProvider.GetRequiredService<IAgentBrokerLocalTransport>());

            var appData = Path.Combine(root, "appdata");
            var provider = new ClaudeCodeAgentProvider(new ClaudeCodeAgentProviderOptions
            {
                AllowedModelIds = ["haiku"],
                DefaultModel = "haiku",
                MaxTurns = 1,
                ProbeTimeout = TimeSpan.FromSeconds(20),
                MaxTurnDuration = TimeSpan.FromMinutes(2),
                DedicatedWorkingDirectory = Path.Combine(root, "claude-cwd"),
                AppDataDirectory = appData,
                // Protect the same single-owner database that the production registry uses below.
                DatabasePath = Path.Combine(root, "workspace.db"),
                DebugLogDirectory = null,
            }, new LocalClaudeCodeSystem(), mcp);
            var status = await provider.GetStatusAsync(CancellationToken.None);
            Assume.That(status.IsAvailable, Is.True,
                "Claude Code ausente, indisponível ou não autenticado pela CLI oficial: " + status.UnavailableCode);

            var permissions = AgentProviderPermissions.Default(ClaudeCodeAgentProvider.Id) with
            {
                ExternalDestinationConsentAt = DateTimeOffset.UtcNow,
                DataSending = new AgentDataSendingPermissions
                {
                    ActiveFile = false, WorkspaceFiles = false, TabMetadata = true, MongoDocuments = false
                },
                Workspace = new AgentWorkspacePermissions { UseFilesFolder = true, Exclusions = [] },
                NativeFileRead = false,
                NativeCommandExecution = false,
                NativeFileWrite = false,
                NativeNetwork = false,
                EditProposals = new AgentEditProposalPermissions { ActiveFile = false, OtherWorkspaceFiles = false },
                AutomaticContext = new AgentAutomaticContextPermissions { ActiveFile = false, TabMetadata = false },
                EnabledReadTools = [AgentProductToolNames.GetWorkspaceContext],
                KeepHistory = false,
            };
            var plan = AgentModePolicy.Plan(AgentOperationMode.Automatic, permissions,
                new AgentPlatformFacts(HasWorkspaceFolder: true, ProductToolsAvailable: true, NativeToolsAvailable: false));
            Assert.Multiple(() =>
            {
                Assert.That(plan.ProductTools, Is.EqualTo([AgentProductToolNames.GetWorkspaceContext]));
                Assert.That(plan.NativeTools, Is.Empty);
            });

            cleanupVerified = false;
            await using var session = (ClaudeCodeAgentSession)await provider.CreateSessionAsync(
                new AgentSessionOptions(ClaudeCodeAgentProvider.Id), CancellationToken.None);
            var request = new AgentTurnRequest(AgentTurnId.New(),
                "Use get_workspace_context e responda somente com o nome da pasta de workspace.", "synthetic-tab", 1)
            {
                Plan = plan,
                Permissions = permissions,
                WorkspaceContext = new AgentWorkspaceContext(DateTimeOffset.UtcNow, WorkspaceFolder: workspaceRoot),
                SystemPrompt = "Use apenas as ferramentas do produto indicadas no plano. A saída é sintética.",
            };
            var events = await ClaudeCodeFixture.RunAsync(session, request);
            var answer = ClaudeCodeFixture.Text(events);
            var expectedName = Path.GetFileName(workspaceRoot);
            var audit = serviceProvider.GetRequiredService<IAgentAuditRepository>();
            var ledger = await audit.GetRecentAsync(100);

            Assert.Multiple(() =>
            {
                Assert.That(ClaudeCodeFixture.Error(events), Is.Null, "O turno Claude/MCP falhou.");
                Assert.That(session.LastTurn?.Outcome, Is.EqualTo(AgentTurnOutcome.Completed));
                Assert.That(session.LastTurn?.ProductToolCalls, Is.EqualTo(1),
                    "A CLI precisa ter solicitado exatamente a única ferramenta autorizada.");
                Assert.That(events.Any(item => item.Kind == AgentEventKind.ToolCompleted &&
                    item.ToolName == AgentProductToolNames.GetWorkspaceContext), Is.True,
                    "A CLI precisa receber resultado da chamada MCP sem erro.");
                Assert.That(events.Any(item => item.Kind == AgentEventKind.ToolFailed &&
                    item.ToolName == AgentProductToolNames.GetWorkspaceContext), Is.False);
                Assert.That(answer.Contains(expectedName, StringComparison.Ordinal), Is.True,
                    "A resposta não refletiu o contexto sintético retornado pelo registry.");
                Assert.That(ledger.Any(item => item.ToolName == AgentProductToolNames.GetWorkspaceContext &&
                    item.Outcome == AgentAuditOutcome.Succeeded), Is.True,
                    "A auditoria durável do registry comprova a execução real da ferramenta.");
            });
            Assert.That(await audit.GetPendingAsync(), Is.Empty,
                "A chamada autorizada não pode deixar uma intenção de auditoria sem terminal.");

            // Session.DisposeAsync deliberately absorbs cleanup exceptions. Retry the durable marker and only erase
            // the workspace database after the OS-store proof has actually been removed.
            await session.DisposeAsync();
            await mcp.DisposeAsync();
            var pendingProofs = await serviceProvider.GetRequiredService<IAgentPrincipalAuthority>()
                .RecoverPendingChannelsAsync();
            Assert.That(pendingProofs, Is.Zero, "A prova do canal ainda requer recuperação; preservar o workspace.");
            cleanupVerified = true;
        }
        finally
        {
            if (cleanupVerified)
            {
                if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            }
            else if (Directory.Exists(root))
            {
                TestContext.Progress.WriteLine(
                    $"A limpeza/revogação não foi comprovada; workspace preservado para recuperação: {Path.Combine(root, "workspace.db")}");
            }
        }
    }

    private sealed class SyntheticWorkspaceContextSource(string workspaceRoot) : IAgentWorkspaceContextSource
    {
        public AgentWorkspaceContext Capture() => new(DateTimeOffset.UtcNow, WorkspaceFolder: workspaceRoot);
    }
}
