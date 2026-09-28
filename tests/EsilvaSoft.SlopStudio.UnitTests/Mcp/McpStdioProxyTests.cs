using System.IO.Pipes;
using System.Text.Json;
using System.Text.Json.Nodes;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Application.Agents.Broker;
using EsilvaSoft.SlopStudio.Infrastructure;

namespace EsilvaSoft.SlopStudio.UnitTests.Mcp;

/// <summary>
/// Lotes 3/4: the real proxy executable over real STDIO in a separate process, driven by the independent BCL client,
/// connected through the real named pipe to a real broker. The channel proof lives in the real Windows Credential
/// Manager (synthetic reference, removed by revocation in teardown); the proxy reads it from there, never from argv.
/// </summary>
[TestFixture]
[NonParallelizable]
[Platform("Win")]
public sealed class McpStdioProxyTests
{
    private static readonly string[] ReleasedTools =
        ["list_connections", "list_databases", "list_collections", "mongo_find", "mongo_count"];

    [TestCase(StdioMcpProcess.Legacy)]
    [TestCase(StdioMcpProcess.Current)]
    public async Task ExternalClientDiscoversAndExecutesTheReleasedStageWithoutSecrets(string era)
    {
        await using var fixture = await StartAsync();
        var channel = await EnrollAsync(fixture);
        var proof = await fixture.ProofAsync(channel);
        await using var proxy = StdioMcpProcess.Start(StdioMcpProcess.Arguments(fixture.WorkspaceId, channel));

        if (era == StdioMcpProcess.Legacy)
            await proxy.InitializeLegacyAsync();
        else
        {
            var discovery = await proxy.RequestAsync(0, "server/discover", era: era);
            Assert.That(discovery.GetProperty("result").GetProperty("supportedVersions").EnumerateArray()
                .Select(version => version.GetString()), Does.Contain(StdioMcpProcess.Current));
        }
        var tools = await proxy.RequestAsync(1, "tools/list", era: era);
        var connections = await proxy.RequestAsync(2, "tools/call", StdioMcpProcess.Call("list_connections", "{}"), era);
        var find = await proxy.RequestAsync(3, "tools/call", StdioMcpProcess.Call("mongo_find", fixture.FindArguments()), era);
        var javascript = await proxy.RequestAsync(4, "tools/call",
            StdioMcpProcess.Call("mongo_find", fixture.FindArguments("{\"$where\":\"sleep(1000)\"}")), era);
        var envFilter = await proxy.RequestAsync(5, "tools/call",
            StdioMcpProcess.Call("mongo_find", fixture.FindArguments("{\"a\":\"${ENV.SECRET}\"}")), era);
        var exitCode = await proxy.CloseAndWaitAsync();

        var listed = tools.GetProperty("result").GetProperty("tools").EnumerateArray().ToArray();
        var findResult = find.GetProperty("result");
        Assert.Multiple(() =>
        {
            Assert.That(listed.Select(tool => tool.GetProperty("name").GetString()), Is.EquivalentTo(ReleasedTools));
            Assert.That(listed.All(tool => tool.GetProperty("annotations").GetProperty("readOnlyHint").GetBoolean()), Is.True);
            Assert.That(connections.GetProperty("result").GetProperty("structuredContent").GetProperty("connections")[0]
                .GetProperty("name").GetString(), Is.EqualTo($"Conexão {fixture.Profile.Id:D}"));
            Assert.That(findResult.GetProperty("isError").GetBoolean(), Is.False);
            Assert.That(findResult.GetProperty("structuredContent").GetProperty("documentsEjson")[0].GetString(),
                Is.EqualTo(McpBrokerFixture.DocumentEjson), "Extended JSON (UUID subtipo 4, Int64) intacto.");
            Assert.That(findResult.GetProperty("content")[0].GetProperty("text").GetString(),
                Does.Not.Contain("9007199254740993"), "Texto não duplica documentos.");
            Assert.That(javascript.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString(),
                Does.StartWith("InvalidArguments"), "JavaScript no filtro é negado antes do MongoDB.");
            Assert.That(envFilter.GetProperty("result").GetProperty("isError").GetBoolean(), Is.False);
            Assert.That(fixture.Find.LastFilter, Does.Contain("${ENV.SECRET}"),
                "Marcador ENV chega como texto literal, sem resolução de variáveis.");
            Assert.That(fixture.Find.Calls, Is.EqualTo(2));
            Assert.That(exitCode, Is.Zero, "EOF do stdin encerra o proxy normalmente.");
            Assert.That(proxy.Stderr, Is.Empty);
            Assert.That(proxy.AllOutput, Does.Not.Contain(proof).And.Not.Contain(McpBrokerFixture.UriCanary)
                .And.Not.Contain("db.internal").And.Not.Contain("Produção interna"));
        });
        proxy.AssertCleanStdout();
    }

    [Test]
    public async Task ClientWithoutGrantSeesTheCatalogButEveryCallIsDenied()
    {
        await using var fixture = await StartAsync();
        var channel = await EnrollAsync(fixture, grant: false);
        await using var proxy = StdioMcpProcess.Start(StdioMcpProcess.Arguments(fixture.WorkspaceId, channel));
        await proxy.InitializeLegacyAsync();
        var tools = await proxy.RequestAsync(1, "tools/list");
        var call = await proxy.RequestAsync(2, "tools/call", StdioMcpProcess.Call("mongo_find", fixture.FindArguments()));

        Assert.Multiple(() =>
        {
            Assert.That(tools.GetProperty("result").GetProperty("tools").GetArrayLength(), Is.EqualTo(ReleasedTools.Length));
            Assert.That(call.GetProperty("result").GetProperty("isError").GetBoolean(), Is.True);
            Assert.That(call.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString(),
                Does.StartWith("PermissionDenied"));
            Assert.That(fixture.Find.Calls, Is.Zero);
        });
    }

    [Test]
    public async Task ClosedIdeGivesClearFailuresWhileStaticProtocolDiscoveryStillWorks()
    {
        await using var fixture = new McpBrokerFixture(RequireOsVault()); // host never started
        var channel = await EnrollAsync(fixture);
        await using var proxy = StdioMcpProcess.Start(StdioMcpProcess.Arguments(fixture.WorkspaceId, channel));
        await proxy.InitializeLegacyAsync();
        var tools = await proxy.RequestAsync(1, "tools/list");
        var call = await proxy.RequestAsync(2, "tools/call", StdioMcpProcess.Call("mongo_find", fixture.FindArguments()));
        var exitCode = await proxy.CloseAndWaitAsync();

        Assert.Multiple(() =>
        {
            Assert.That(tools.GetProperty("error").GetProperty("message").GetString(), Does.Contain("não está disponível"));
            Assert.That(call.GetProperty("result").GetProperty("isError").GetBoolean(), Is.True);
            Assert.That(call.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString(),
                Does.StartWith("HostUnavailable").And.Contain("não será repetida"));
            Assert.That(exitCode, Is.Zero);
        });
        proxy.AssertCleanStdout();
    }

    [Test]
    public async Task IncompatibleIpcMajorFailsClearlyBeforeTheProofIsRead()
    {
        await using var fixture = new McpBrokerFixture(RequireOsVault());
        var channel = await EnrollAsync(fixture);
        var proof = await fixture.ProofAsync(channel);
        var frames = new List<AgentBrokerMessage>();
        await using var server = new NamedPipeServerStream(AgentBrokerEndpoint.ForWorkspace(fixture.WorkspaceId).PipeName,
            PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var futureBroker = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync();
            if (await AgentBrokerFrameCodec.ReadAsync(server, AgentBrokerProtocol.MaximumRequestFrameBytes, default) is { } hello)
                frames.Add(hello);
            await AgentBrokerFrameCodec.WriteAsync(server, new AgentBrokerMessage
            {
                Type = AgentBrokerProtocol.MessageTypes.Error, ErrorCode = AgentBrokerProtocol.ErrorCodes.IncompatibleVersion,
                SupportedMajor = AgentBrokerProtocol.MajorVersion + 1
            }, AgentBrokerProtocol.MaximumResponseFrameBytes, default);
            try
            {
                while (await AgentBrokerFrameCodec.ReadAsync(server, AgentBrokerProtocol.MaximumRequestFrameBytes, default) is { } extra)
                    frames.Add(extra);
            }
            catch (IOException) { }
        });

        await using var proxy = StdioMcpProcess.Start(StdioMcpProcess.Arguments(fixture.WorkspaceId, channel));
        await proxy.InitializeLegacyAsync();
        var call = await proxy.RequestAsync(1, "tools/call", StdioMcpProcess.Call("list_connections", "{}"));
        await proxy.CloseAndWaitAsync();
        await futureBroker.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Multiple(() =>
        {
            Assert.That(call.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString(),
                Does.StartWith("IncompatibleVersion").And.Contain("mesma versão"));
            Assert.That(frames.Select(frame => frame.Type), Is.EqualTo(new[] { AgentBrokerProtocol.MessageTypes.Hello }),
                "Nenhum frame de autenticação após a recusa de versão.");
            Assert.That(frames.All(frame => frame.Proof is null), Is.True);
            Assert.That(proxy.AllOutput, Does.Not.Contain(proof));
        });
    }

    [Test]
    public async Task TwoProxiesRunConcurrentlyWithIsolatedPrincipals()
    {
        await using var fixture = await StartAsync();
        var granted = await EnrollAsync(fixture);
        var denied = await EnrollAsync(fixture, grant: false);
        await using var a = StdioMcpProcess.Start(StdioMcpProcess.Arguments(fixture.WorkspaceId, granted));
        await using var b = StdioMcpProcess.Start(StdioMcpProcess.Arguments(fixture.WorkspaceId, denied, StdioMcpProcess.Current));
        await Task.WhenAll(a.InitializeLegacyAsync(), b.RequestAsync(0, "server/discover", era: StdioMcpProcess.Current));

        var results = await Task.WhenAll(
            a.RequestAsync(1, "tools/call", StdioMcpProcess.Call("mongo_find", fixture.FindArguments())),
            b.RequestAsync(1, "tools/call", StdioMcpProcess.Call("mongo_find", fixture.FindArguments()), StdioMcpProcess.Current));

        Assert.Multiple(() =>
        {
            Assert.That(results[0].GetProperty("result").GetProperty("isError").GetBoolean(), Is.False);
            Assert.That(results[1].GetProperty("result").GetProperty("isError").GetBoolean(), Is.True);
            Assert.That(fixture.Find.Calls, Is.EqualTo(1));
            Assert.That(fixture.Host.ActiveConnectionCount, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task ClientCancellationCancelsOnlyThatCallAndIsNotReplayed()
    {
        await using var fixture = await StartAsync();
        var channel = await EnrollAsync(fixture);
        await using var proxy = StdioMcpProcess.Start(StdioMcpProcess.Arguments(fixture.WorkspaceId, channel));
        await proxy.InitializeLegacyAsync();
        fixture.Find.Block = true;
        await proxy.SendAsync(StdioMcpProcess.Request(5, "tools/call",
            StdioMcpProcess.Call("mongo_find", fixture.FindArguments()), StdioMcpProcess.Legacy));
        await fixture.Find.Started.Task.WaitAsync(TimeSpan.FromSeconds(15));
        await proxy.SendAsync(new JsonObject
        {
            ["jsonrpc"] = "2.0", ["method"] = "notifications/cancelled",
            ["params"] = new JsonObject { ["requestId"] = 5, ["reason"] = "teste" }
        });
        await WaitUntilAsync(() => fixture.Find.Cancelled == 1);
        fixture.Find.Block = false;
        var next = await proxy.RequestAsync(6, "tools/call", StdioMcpProcess.Call("list_connections", "{}"));
        await proxy.CloseAndWaitAsync();

        Assert.Multiple(() =>
        {
            Assert.That(fixture.Find.Cancelled, Is.EqualTo(1), "O token da chamada 5 chegou ao executor.");
            Assert.That(fixture.Find.Calls, Is.EqualTo(1), "Sem replay da chamada cancelada.");
            Assert.That(next.GetProperty("result").GetProperty("isError").GetBoolean(), Is.False);
            Assert.That(proxy.StdoutLines.Any(line => line.Contains("\"id\":5", StringComparison.Ordinal) &&
                line.Contains("documentsEjson", StringComparison.Ordinal)), Is.False, "Resultado tardio descartado.");
        });
    }

    [Test]
    public async Task BrokerCrashFailsThePendingCallAndRecoveryDoesNotReplayIt()
    {
        await using var fixture = await StartAsync();
        var channel = await EnrollAsync(fixture);
        await using var proxy = StdioMcpProcess.Start(StdioMcpProcess.Arguments(fixture.WorkspaceId, channel));
        await proxy.InitializeLegacyAsync();
        fixture.Find.Block = true;
        await proxy.SendAsync(StdioMcpProcess.Request(1, "tools/call",
            StdioMcpProcess.Call("mongo_find", fixture.FindArguments()), StdioMcpProcess.Legacy));
        await fixture.Find.Started.Task.WaitAsync(TimeSpan.FromSeconds(15));
        await fixture.Host.StopAsync(); // IDE/broker gone mid-call: EOF on the pipe
        var failed = await proxy.ReceiveAsync(1);

        fixture.Find.Block = false;
        await using var restarted = new AgentBrokerHost(fixture.Registry, fixture.Authority, fixture.Options);
        await restarted.StartAsync();
        await Task.Delay(TimeSpan.FromSeconds(1)); // bounded reconnection backoff
        var recovered = await proxy.RequestAsync(2, "tools/call", StdioMcpProcess.Call("mongo_find", fixture.FindArguments()));

        Assert.Multiple(() =>
        {
            Assert.That(failed.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString(),
                Does.StartWith("HostUnavailable"));
            Assert.That(recovered.GetProperty("result").GetProperty("isError").GetBoolean(), Is.False);
            Assert.That(fixture.Find.Calls, Is.EqualTo(2), "A chamada interrompida não foi reenviada.");
        });
    }

    [Test]
    public async Task ExecutionDeadlineEndsAHangingReadWithoutReplay()
    {
        var options = new AgentBrokerOptions
        {
            WorkspaceId = Guid.NewGuid(), Enabled = true, Stage = AgentToolExposureStage.LiteralQueries,
            ToolExecutionTimeout = TimeSpan.FromSeconds(1)
        };
        await using var fixture = new McpBrokerFixture(RequireOsVault(), options);
        await fixture.Host.StartAsync();
        var channel = await EnrollAsync(fixture);
        fixture.Find.Block = true;
        await using var proxy = StdioMcpProcess.Start(StdioMcpProcess.Arguments(fixture.WorkspaceId, channel));
        await proxy.InitializeLegacyAsync();
        var call = await proxy.RequestAsync(1, "tools/call", StdioMcpProcess.Call("mongo_find", fixture.FindArguments()));

        Assert.Multiple(() =>
        {
            Assert.That(call.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString(),
                Does.StartWith("DeadlineExceeded"));
            Assert.That(fixture.Find.Calls, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task OversizedInputLineEndsTheSessionWithoutNoiseOnStdout()
    {
        await using var fixture = await StartAsync();
        var channel = await EnrollAsync(fixture);
        await using var proxy = StdioMcpProcess.Start(StdioMcpProcess.Arguments(fixture.WorkspaceId, channel));
        await proxy.InitializeLegacyAsync();
        await proxy.SendRawAsync("{\"jsonrpc\":\"2.0\",\"id\":9,\"method\":\"tools/call\",\"params\":{\"name\":\"mongo_find\",\"arguments\":{\"filterEjson\":\"" +
            new string('x', 300 * 1024) + "\"}}}");
        var exitCode = await proxy.WaitForExitAsync();

        Assert.Multiple(() =>
        {
            Assert.That(exitCode, Is.EqualTo(4));
            Assert.That(proxy.Stderr, Does.Contain("acima do limite"));
            Assert.That(fixture.Find.Calls, Is.Zero);
        });
        proxy.AssertCleanStdout();
    }

    [Test]
    public async Task InvalidCommandLineIsRejectedWithoutEchoingValues()
    {
        await using var proxy = StdioMcpProcess.Start(["--stdio", "--channel-id", "segredo-nao-ecoado"]);
        var exitCode = await proxy.CloseAndWaitAsync();
        Assert.Multiple(() =>
        {
            Assert.That(exitCode, Is.EqualTo(2));
            Assert.That(proxy.StdoutLines, Is.Empty);
            Assert.That(proxy.Stderr, Does.Not.Contain("segredo-nao-ecoado"));
        });
    }

    private static Task<McpBrokerFixture.Channel> EnrollAsync(McpBrokerFixture fixture, bool grant = true) =>
        fixture.EnrollAsync(grant: grant);

    private static async Task<McpBrokerFixture> StartAsync()
    {
        var fixture = new McpBrokerFixture(RequireOsVault());
        await fixture.Host.StartAsync();
        return fixture;
    }

    private static WindowsCredentialSecretStore RequireOsVault()
    {
        var store = new WindowsCredentialSecretStore();
        var availability = store.GetAvailabilityAsync().GetAwaiter().GetResult();
        if (!availability.IsSuccess || availability.Value != SecretStoreAvailability.Available)
            Assert.Ignore("Credential Manager indisponível nesta sessão; a prova STDIO ponta a ponta exige o cofre real.");
        return store;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) Assert.Fail("Condição não atingida no prazo.");
            await Task.Delay(50);
        }
    }
}
