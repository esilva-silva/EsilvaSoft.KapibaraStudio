using System.Buffers.Binary;
using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Application.Agents.Broker;
using EsilvaSoft.KapibaraStudio.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace EsilvaSoft.KapibaraStudio.UnitTests.Mcp;

/// <summary>
/// Lote 3: broker IPC on the real local endpoint (named pipe with current-user ACL on Windows) against the real LiteDB
/// channel authority, policy, audit and registry. Only MongoDB is simulated.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class AgentBrokerHostTests
{
    private static readonly string[] LiteralQueryTools =
        ["list_connections", "list_databases", "list_collections", "mongo_find", "mongo_count"];

    [Test]
    public async Task IncompatibleMajorIsRejectedBeforeAnyCredentialIsRequested()
    {
        await using var fixture = await StartAsync();
        await using var peer = await RawBrokerPeer.ConnectAsync(fixture.WorkspaceId);
        await peer.SendHelloAsync(major: AgentBrokerProtocol.MajorVersion + 1);
        var answer = await peer.ReceiveAsync();

        Assert.Multiple(async () =>
        {
            Assert.That(answer?.Type, Is.EqualTo(AgentBrokerProtocol.MessageTypes.Error));
            Assert.That(answer?.ErrorCode, Is.EqualTo(AgentBrokerProtocol.ErrorCodes.IncompatibleVersion));
            Assert.That(answer?.SupportedMajor, Is.EqualTo(AgentBrokerProtocol.MajorVersion));
            Assert.That(await peer.ReceiveAsync(), Is.Null, "Conexão encerrada após a recusa.");
            Assert.That(fixture.Authority.Authentications, Is.Zero);
        });
    }

    [Test]
    public async Task InvalidProofsAreRateLimitedWithoutReachingTheAuthority()
    {
        await using var fixture = await StartAsync();
        var channel = await fixture.EnrollAsync();
        for (var attempt = 0; attempt < fixture.Options.MaximumAuthenticationFailuresPerChannel; attempt++)
        {
            await using var peer = await RawBrokerPeer.ConnectAsync(fixture.WorkspaceId);
            var answer = await peer.AuthenticateAsync(channel.ChannelId, "prova-invalida-" + attempt);
            Assert.That(answer?.ErrorCode, Is.EqualTo(AgentBrokerProtocol.ErrorCodes.AuthenticationFailed));
        }
        var reached = fixture.Authority.Authentications;

        await using var limited = await RawBrokerPeer.ConnectAsync(fixture.WorkspaceId);
        var blocked = await limited.AuthenticateAsync(channel.ChannelId, await fixture.ProofAsync(channel));

        Assert.Multiple(() =>
        {
            Assert.That(blocked?.ErrorCode, Is.EqualTo(AgentBrokerProtocol.ErrorCodes.RateLimited),
                "Mesmo a prova correta é recusada enquanto a janela está saturada.");
            Assert.That(fixture.Authority.Authentications, Is.EqualTo(reached));
        });
    }

    [Test]
    public async Task SilentPeerIsDroppedAtTheHandshakeDeadline()
    {
        await using var fixture = await StartAsync();
        await using var peer = await RawBrokerPeer.ConnectAsync(fixture.WorkspaceId);
        var started = DateTime.UtcNow;
        Assert.That(await peer.ReceiveAsync(), Is.Null);
        Assert.That(DateTime.UtcNow - started, Is.LessThan(TimeSpan.FromSeconds(8)));
    }

    [Test]
    public async Task OversizedFrameIsRejectedWithoutBuffering()
    {
        await using var fixture = await StartAsync();
        await using var peer = await RawBrokerPeer.ConnectAsync(fixture.WorkspaceId);
        var header = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(header, 64 * 1024 * 1024);
        await peer.Pipe.WriteAsync(header);
        await peer.Pipe.FlushAsync();

        var answer = await peer.ReceiveAsync();
        Assert.Multiple(async () =>
        {
            Assert.That(answer?.ErrorCode, Is.EqualTo(AgentBrokerProtocol.ErrorCodes.ProtocolViolation));
            Assert.That(await peer.ReceiveAsync(), Is.Null);
        });
    }

    [Test]
    public async Task AuthenticatedChannelDiscoversOnlyTheReleasedReadOnlyStage()
    {
        await using var fixture = await StartAsync();
        var channel = await fixture.EnrollAsync();
        await using var peer = await RawBrokerPeer.ConnectAsync(fixture.WorkspaceId);
        Assert.That((await peer.AuthenticateAsync(channel.ChannelId, await fixture.ProofAsync(channel)))?.Type,
            Is.EqualTo(AgentBrokerProtocol.MessageTypes.Authenticated));
        await peer.SendAsync(new AgentBrokerMessage { Type = AgentBrokerProtocol.MessageTypes.ListTools, Id = 1 });
        var tools = await peer.ReceiveAsync();

        Assert.Multiple(() =>
        {
            Assert.That(tools?.Tools?.Select(tool => tool.Name), Is.EquivalentTo(LiteralQueryTools));
            Assert.That(tools!.Tools!.All(tool => tool.ReadOnly && !tool.Destructive), Is.True);
            Assert.That(JsonSerializer.Serialize(tools), Does.Not.Contain(McpBrokerFixture.UriCanary)
                .And.Not.Contain(fixture.Profile.Name), "Descoberta estática, sem nomes de conexões.");
        });
    }

    [Test]
    public async Task GrantedChannelReadsExtendedJsonUnchangedAndClientWithoutGrantIsDenied()
    {
        await using var fixture = await StartAsync();
        var granted = await fixture.EnrollAsync();
        var ungranted = await fixture.EnrollAsync(grant: false);
        var missingPolicy = await fixture.EnrollAsync(withPolicy: false);

        await using var a = await RawBrokerPeer.ConnectAsync(fixture.WorkspaceId);
        await using var b = await RawBrokerPeer.ConnectAsync(fixture.WorkspaceId);
        await using var c = await RawBrokerPeer.ConnectAsync(fixture.WorkspaceId);
        await a.AuthenticateAsync(granted.ChannelId, await fixture.ProofAsync(granted));
        await b.AuthenticateAsync(ungranted.ChannelId, await fixture.ProofAsync(ungranted));
        Assert.That((await c.AuthenticateAsync(missingPolicy.ChannelId, await fixture.ProofAsync(missingPolicy)))?.Type,
            Is.EqualTo(AgentBrokerProtocol.MessageTypes.Authenticated), "Prova válida sem política ainda autentica.");

        await Task.WhenAll(a.CallAsync(1, "mongo_find", fixture.FindArguments()),
            b.CallAsync(1, "mongo_find", fixture.FindArguments()), c.CallAsync(1, "list_connections", "{}"));
        var results = await Task.WhenAll(a.ReceiveAsync(), b.ReceiveAsync(), c.ReceiveAsync());

        Assert.Multiple(() =>
        {
            Assert.That(results[0]?.Status, Is.EqualTo(AgentBrokerMessage.SucceededStatus));
            var documents = results[0]!.StructuredContent!.Value.GetProperty("documentsEjson");
            Assert.That(documents[0].GetString(), Is.EqualTo(McpBrokerFixture.DocumentEjson),
                "UUID subtipo 4 e Int64 > 2^53 preservados como Extended JSON.");
            Assert.That(results[1]?.ErrorCode, Is.EqualTo(AgentBrokerProtocol.ErrorCodes.PermissionDenied));
            Assert.That(results[2]?.ErrorCode, Is.EqualTo(AgentBrokerProtocol.ErrorCodes.PermissionDenied));
            Assert.That(results[2]?.Dispatched, Is.False);
            Assert.That(fixture.Find.Calls, Is.EqualTo(1), "Só o canal com grant chega ao MongoDB.");
            Assert.That(JsonSerializer.Serialize(results), Does.Not.Contain(McpBrokerFixture.UriCanary));
        });
    }

    [Test]
    public async Task ExternalListConnectionsUsesTheIdAliasInsteadOfTheFreeTextName()
    {
        await using var fixture = await StartAsync();
        var channel = await fixture.EnrollAsync();
        await using var peer = await RawBrokerPeer.ConnectAsync(fixture.WorkspaceId);
        await peer.AuthenticateAsync(channel.ChannelId, await fixture.ProofAsync(channel));
        await peer.CallAsync(1, "list_connections", "{}");
        var result = await peer.ReceiveAsync();

        var connection = result!.StructuredContent!.Value.GetProperty("connections")[0];
        Assert.Multiple(() =>
        {
            Assert.That(connection.GetProperty("name").GetString(), Is.EqualTo($"Conexão {fixture.Profile.Id:D}"));
            Assert.That(result.StructuredContent.Value.GetRawText(), Does.Not.Contain("Produção interna")
                .And.Not.Contain(McpBrokerFixture.UriCanary).And.Not.Contain("db.internal"));
        });
    }

    [Test]
    public async Task CancellingOneCallLeavesTheOtherIntactAndNothingIsReplayed()
    {
        await using var fixture = await StartAsync();
        var channel = await fixture.EnrollAsync();
        await using var peer = await RawBrokerPeer.ConnectAsync(fixture.WorkspaceId);
        await peer.AuthenticateAsync(channel.ChannelId, await fixture.ProofAsync(channel));
        fixture.Find.Block = true;
        await peer.CallAsync(1, "mongo_find", fixture.FindArguments());
        await fixture.Find.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await peer.CallAsync(2, "list_connections", "{}");
        var second = await peer.ReceiveAsync();
        await peer.SendAsync(new AgentBrokerMessage { Type = AgentBrokerProtocol.MessageTypes.Cancel, Id = 1 });
        var first = await peer.ReceiveAsync();
        // A late or repeated cancel for a finished id must be ignored.
        await peer.SendAsync(new AgentBrokerMessage { Type = AgentBrokerProtocol.MessageTypes.Cancel, Id = 1 });
        fixture.Find.Block = false;
        await peer.CallAsync(3, "mongo_count", fixture.FindArguments().Replace(",\"limit\":5", "", StringComparison.Ordinal));
        var third = await peer.ReceiveAsync();

        Assert.Multiple(() =>
        {
            Assert.That(second?.Id, Is.EqualTo(2));
            Assert.That(second?.Status, Is.EqualTo(AgentBrokerMessage.SucceededStatus));
            Assert.That(first?.Id, Is.EqualTo(1));
            Assert.That(first?.ErrorCode, Is.EqualTo(AgentBrokerProtocol.ErrorCodes.Cancelled));
            Assert.That(fixture.Find.Cancelled, Is.EqualTo(1));
            Assert.That(third?.Id, Is.EqualTo(3));
            Assert.That(third?.StructuredContent?.GetProperty("countEjson").GetString(),
                Is.EqualTo("{\"$numberLong\":\"9007199254740993\"}"));
            Assert.That(fixture.Find.Calls, Is.EqualTo(2), "find uma vez (cancelado) + count; sem replay.");
        });
    }

    [Test]
    public async Task RepeatedRequestIdIsAProtocolViolationNotANewCall()
    {
        await using var fixture = await StartAsync();
        var channel = await fixture.EnrollAsync();
        await using var peer = await RawBrokerPeer.ConnectAsync(fixture.WorkspaceId);
        await peer.AuthenticateAsync(channel.ChannelId, await fixture.ProofAsync(channel));
        await peer.CallAsync(7, "mongo_find", fixture.FindArguments());
        Assert.That((await peer.ReceiveAsync())?.Status, Is.EqualTo(AgentBrokerMessage.SucceededStatus));
        await peer.CallAsync(7, "mongo_find", fixture.FindArguments());

        Assert.Multiple(async () =>
        {
            Assert.That((await peer.ReceiveAsync())?.ErrorCode, Is.EqualTo(AgentBrokerProtocol.ErrorCodes.ProtocolViolation));
            Assert.That(await peer.ReceiveAsync(), Is.Null);
            Assert.That(fixture.Find.Calls, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task RevocationEndsTheConnectionAndBlocksReauthentication()
    {
        await using var fixture = await StartAsync();
        var channel = await fixture.EnrollAsync();
        var proof = await fixture.ProofAsync(channel);
        await using var peer = await RawBrokerPeer.ConnectAsync(fixture.WorkspaceId);
        await peer.AuthenticateAsync(channel.ChannelId, proof);
        Assert.That(await ((Application.IAgentPrincipalAuthority)fixture.Owner)
            .RevokeExternalChannelAsync(channel.ChannelId), Is.EqualTo(Application.AgentChannelRevocationStatus.Revoked));

        await peer.CallAsync(1, "mongo_find", fixture.FindArguments());
        var denied = await peer.ReceiveAsync();
        await using var again = await RawBrokerPeer.ConnectAsync(fixture.WorkspaceId);
        var reauthentication = await again.AuthenticateAsync(channel.ChannelId, proof);

        Assert.Multiple(async () =>
        {
            Assert.That(denied?.ErrorCode, Is.EqualTo(AgentBrokerProtocol.ErrorCodes.AuthenticationRequired));
            Assert.That(await peer.ReceiveAsync(), Is.Null, "Revogação encerra a conexão.");
            Assert.That(reauthentication?.ErrorCode, Is.EqualTo(AgentBrokerProtocol.ErrorCodes.AuthenticationFailed));
            Assert.That(fixture.Find.Calls, Is.Zero);
        });
    }

    [Test]
    public async Task PolicyRevisionChangeIsPickedUpWithoutLeakingTheOldGrant()
    {
        await using var fixture = await StartAsync();
        var channel = await fixture.EnrollAsync();
        await using var peer = await RawBrokerPeer.ConnectAsync(fixture.WorkspaceId);
        await peer.AuthenticateAsync(channel.ChannelId, await fixture.ProofAsync(channel));
        await ((Application.Agents.IAgentAuthorizationPolicyRepository)fixture.Owner).SaveAsync(channel.PrincipalId, [], 1);

        await peer.CallAsync(1, "mongo_find", fixture.FindArguments());
        var denied = await peer.ReceiveAsync();
        await ((Application.Agents.IAgentAuthorizationPolicyRepository)fixture.Owner).SaveAsync(channel.PrincipalId,
            fixture.Grants(channel), 2);
        await peer.CallAsync(2, "mongo_find", fixture.FindArguments());
        var allowed = await peer.ReceiveAsync();

        Assert.Multiple(() =>
        {
            Assert.That(denied?.ErrorCode, Is.EqualTo(AgentBrokerProtocol.ErrorCodes.PermissionDenied));
            Assert.That(allowed?.Status, Is.EqualTo(AgentBrokerMessage.SucceededStatus));
            Assert.That(fixture.Find.Calls, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task StoppingTheHostClosesConnectionsAndFreesTheEndpoint()
    {
        await using var fixture = await StartAsync();
        var channel = await fixture.EnrollAsync();
        await using var peer = await RawBrokerPeer.ConnectAsync(fixture.WorkspaceId);
        await peer.AuthenticateAsync(channel.ChannelId, await fixture.ProofAsync(channel));
        fixture.Find.Block = true;
        await peer.CallAsync(1, "mongo_find", fixture.FindArguments());
        await fixture.Find.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await fixture.Host.StopAsync();

        Assert.Multiple(async () =>
        {
            Assert.That(await peer.ReceiveAsync(), Is.Null, "Sem resposta fabricada: EOF ao cliente.");
            Assert.That(fixture.Find.Cancelled, Is.EqualTo(1));
            Assert.That(fixture.Host.IsRunning, Is.False);
            Assert.ThrowsAsync<TimeoutException>(async () =>
            {
                await using var late = new System.IO.Pipes.NamedPipeClientStream(".", fixture.Host.Endpoint.PipeName,
                    System.IO.Pipes.PipeDirection.InOut, System.IO.Pipes.PipeOptions.CurrentUserOnly);
                await late.ConnectAsync(300);
            });
        });
    }

    [Test]
    public async Task SecondHostOnTheSameEndpointFailsVisibly()
    {
        await using var fixture = await StartAsync();
        await using var second = new AgentBrokerHost(fixture.Registry, fixture.Authority, fixture.Options);
        var error = Assert.ThrowsAsync<InvalidOperationException>(() => second.StartAsync());
        Assert.That(error!.Message, Does.Contain("em uso"));
    }

    [Test]
    public async Task ConnectionsOfOneChannelShareTheSessionQuotaAndSaturationIsBusyNotPermissionDenied()
    {
        await using var fixture = await StartAsync();
        var channel = await fixture.EnrollAsync();
        var proof = await fixture.ProofAsync(channel);
        await using var first = await RawBrokerPeer.ConnectAsync(fixture.WorkspaceId);
        await using var second = await RawBrokerPeer.ConnectAsync(fixture.WorkspaceId);
        await first.AuthenticateAsync(channel.ChannelId, proof);
        await second.AuthenticateAsync(channel.ChannelId, proof);
        fixture.Find.Block = true;
        await first.CallAsync(1, "mongo_find", fixture.FindArguments());
        await first.CallAsync(2, "mongo_find", fixture.FindArguments());
        await WaitUntilAsync(() => fixture.Find.Calls == 2);

        await first.CallAsync(3, "list_connections", "{}");
        var connectionLimit = await first.ReceiveAsync();
        await second.CallAsync(1, "list_connections", "{}");
        var sessionLimit = await second.ReceiveAsync();
        var blockedCalls = fixture.Find.Calls;

        await first.SendAsync(new AgentBrokerMessage { Type = AgentBrokerProtocol.MessageTypes.Cancel, Id = 1 });
        await first.SendAsync(new AgentBrokerMessage { Type = AgentBrokerProtocol.MessageTypes.Cancel, Id = 2 });
        var cancelled = new[] { await first.ReceiveAsync(), await first.ReceiveAsync() };
        fixture.Find.Block = false;
        // Busy is retryable: once the slots drain, the same channel is admitted again.
        AgentBrokerMessage? recovered = null;
        for (var id = 2L; id < 20 && recovered?.Status != AgentBrokerMessage.SucceededStatus; id++)
        {
            await second.CallAsync(id, "list_connections", "{}");
            recovered = await second.ReceiveAsync();
            if (recovered?.ErrorCode == AgentBrokerProtocol.ErrorCodes.Busy) await Task.Delay(50);
        }
        var audit = await ((Application.Agents.IAgentAuditRepository)fixture.Owner).GetRecentAsync(50);

        Assert.Multiple(() =>
        {
            Assert.That(connectionLimit?.ErrorCode, Is.EqualTo(AgentBrokerProtocol.ErrorCodes.Busy), "Limite da conexão.");
            Assert.That(connectionLimit?.Dispatched, Is.False);
            Assert.That(sessionLimit?.ErrorCode, Is.EqualTo(AgentBrokerProtocol.ErrorCodes.Busy),
                "Segunda conexão do mesmo canal compartilha a sessão; saturação não é PermissionDenied.");
            Assert.That(sessionLimit?.Dispatched, Is.False);
            Assert.That(blockedCalls, Is.EqualTo(2), "Nada saturado chega à fonte.");
            Assert.That(cancelled.Select(item => item?.ErrorCode),
                Has.All.EqualTo(AgentBrokerProtocol.ErrorCodes.Cancelled));
            Assert.That(recovered?.Status, Is.EqualTo(AgentBrokerMessage.SucceededStatus));
            // M1 (revisão 25/09): a saturação do canal é recusada pelo broker antes do registry, logo sem intenção
            // de auditoria; um flood de Busy não cresce o ledger nem ocupa o proprietário LiteDB.
            Assert.That(audit.Any(item => item.DecisionReason == Core.AgentAuditDecisionReason.LimitExceeded), Is.False,
                "Busy do canal não chega ao registry nem grava auditoria.");
            Assert.That(audit.Count(item => item.Outcome == Core.AgentAuditOutcome.Intent), Is.EqualTo(3),
                "Só as duas chamadas bloqueadas e a recuperada geraram intenção.");
        });
    }

    [Test]
    public async Task DefaultOptionsReleaseNothingAndCapConcurrencyAtTheSessionQuota()
    {
        var defaults = new AgentBrokerOptions { WorkspaceId = Guid.NewGuid() };
        await using var fixture = new McpBrokerFixture(new InMemoryProfileSecretStore(),
            defaults with { Enabled = true, HandshakeTimeout = TimeSpan.FromSeconds(2) });
        await fixture.Host.StartAsync();
        var channel = await fixture.EnrollAsync();
        await using var peer = await RawBrokerPeer.ConnectAsync(fixture.WorkspaceId);
        await peer.AuthenticateAsync(channel.ChannelId, await fixture.ProofAsync(channel));
        await peer.SendAsync(new AgentBrokerMessage { Type = AgentBrokerProtocol.MessageTypes.ListTools, Id = 1 });
        var tools = await peer.ReceiveAsync();
        await peer.CallAsync(2, "mongo_find", fixture.FindArguments());
        var find = await peer.ReceiveAsync();

        Assert.Multiple(() =>
        {
            Assert.That(defaults.Enabled, Is.False);
            Assert.That(defaults.Stage, Is.EqualTo(AgentToolExposureStage.None),
                "LiteralQueries só com configuração explícita e gate aprovado.");
            Assert.That(defaults.MaximumConcurrentCallsPerConnection,
                Is.EqualTo(AgentToolRegistry.MaximumConcurrentCallsPerSession));
            Assert.Throws<ArgumentException>(() => (defaults with
            {
                MaximumConcurrentCallsPerConnection = AgentToolRegistry.MaximumConcurrentCallsPerSession + 1
            }).Validate());
            Assert.That(tools?.Tools, Is.Empty, "Estágio padrão não descobre nada.");
            Assert.That(find?.Status, Is.EqualTo(AgentBrokerMessage.FailedStatus));
            Assert.That(find?.ErrorCode, Is.EqualTo("UnknownTool"));
            Assert.That(fixture.Find.Calls, Is.Zero);
        });
    }

    [Test]
    public void CompositionIsOptInAndCapsTheStage()
    {
        var workspace = Guid.NewGuid();
        var none = new ServiceCollection().AddKapibaraStudioAgentBroker(new AgentBrokerOptions { WorkspaceId = workspace });
        var noStage = new ServiceCollection().AddKapibaraStudioAgentBroker(new AgentBrokerOptions { WorkspaceId = workspace, Enabled = true });
        var metadataOptions = new AgentBrokerOptions
        {
            WorkspaceId = workspace, Enabled = true, Stage = AgentToolExposureStage.Metadata
        };
        var metadata = new ServiceCollection()
            .AddKapibaraStudioInfrastructure(Path.Combine(Path.GetTempPath(), $"kapibarastudio-broker-di-{Guid.NewGuid():N}.db"),
                new AgentPlatformOptions { ToolExposureStage = AgentToolExposureStage.Metadata })
            .AddKapibaraStudioAgentBroker(metadataOptions);

        Assert.Multiple(() =>
        {
            Assert.That(none, Is.Empty, "Sem opt-in nada é registrado; a IDE segue sem MCP.");
            Assert.That(noStage, Is.Empty);
            Assert.That(metadata.Count(service => service.ServiceType == typeof(IAgentToolRegistry)), Is.EqualTo(1),
                "O broker não compõe um registry próprio.");
            Assert.That(metadata.Any(service => service.ServiceType == typeof(AgentBrokerHost)), Is.True);
            Assert.Throws<ArgumentException>(() => new ServiceCollection().AddKapibaraStudioAgentBroker(new AgentBrokerOptions
            {
                WorkspaceId = workspace, Enabled = true, Stage = AgentToolExposureStage.DerivedReads
            }), "DerivedReads ainda não tem gate aprovado para MCP.");
            Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddKapibaraStudioAgentBroker(metadataOptions),
                "Sem o registry compartilhado da infraestrutura o broker não é composto.");
            Assert.Throws<InvalidOperationException>(() => metadata.AddKapibaraStudioAgentBroker(metadataOptions),
                "Um único broker por composição.");
            // The platform default is Metadata (ADR-056); a broker announcing another stage is refused.
            Assert.Throws<InvalidOperationException>(() => new ServiceCollection()
                .AddKapibaraStudioInfrastructure(Path.Combine(Path.GetTempPath(), $"kapibarastudio-broker-di-{Guid.NewGuid():N}.db"))
                .AddKapibaraStudioAgentBroker(metadataOptions with { Stage = AgentToolExposureStage.LiteralQueries }),
                "O broker não pode anunciar um estágio diferente do liberado ao registry compartilhado.");
        });
    }

    [Test]
    public async Task ComposedBrokerAndRegistryShareTheSingletonsOfTheWorkspaceOwner()
    {
        using var workspace = new ConnectionCredentialRecoveryTests.Workspace();
        var services = new ServiceCollection();
        services.AddKapibaraStudioInfrastructure(workspace.Path,
            new AgentPlatformOptions { ToolExposureStage = AgentToolExposureStage.LiteralQueries });
        services.AddSingleton<Application.ISecretStore>(new InMemoryProfileSecretStore());
        services.AddKapibaraStudioAgentBroker(new AgentBrokerOptions
        {
            WorkspaceId = Guid.NewGuid(), Enabled = true, Stage = AgentToolExposureStage.LiteralQueries
        });
        await using var provider = services.BuildServiceProvider();

        var registry = provider.GetRequiredService<IAgentToolRegistry>();
        Assert.Multiple(() =>
        {
            Assert.That(provider.GetRequiredService<IAgentToolRegistry>(), Is.SameAs(registry));
            // The broker can announce LiteralQueries while the in-process provider stays closed by default.
            Assert.That(registry.GetDescriptors(), Is.Empty,
                "The broker exposure ceiling does not implicitly expose tools to in-process providers.");
            Assert.That(provider.GetRequiredService<AgentBrokerHost>().IsRunning, Is.False, "Composição não abre o endpoint.");
            Assert.That(provider.GetRequiredService<AgentBrokerHost>().Registry, Is.SameAs(registry));
        });
    }

    private static async Task<McpBrokerFixture> StartAsync()
    {
        var fixture = new McpBrokerFixture(new InMemoryProfileSecretStore());
        await fixture.Host.StartAsync();
        return fixture;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) Assert.Fail("Condição não atingida no prazo.");
            await Task.Delay(20);
        }
    }
}
