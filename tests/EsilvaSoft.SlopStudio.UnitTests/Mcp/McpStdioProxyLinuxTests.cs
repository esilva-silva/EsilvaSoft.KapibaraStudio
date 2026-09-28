namespace EsilvaSoft.SlopStudio.UnitTests.Mcp;

/// <summary>
/// Linux has no homologated proxy-side reader for the channel proof yet (Secret Service pending). The real proxy
/// process must fail closed against a real broker on the Unix socket: no proof crosses the channel, the authority is
/// never reached, data calls report <c>AuthenticationRequired</c>, and stdout stays pure JSON-RPC. This is not a
/// Linux homologation of MCP; it only pins the safe failure until the native reader exists.
/// </summary>
[TestFixture]
[NonParallelizable]
[Platform("Linux")]
public sealed class McpStdioProxyLinuxTests
{
    [Test]
    public async Task ProxyWithoutATransportCredentialReaderFailsClosedWithoutReachingTheAuthority()
    {
        await using var fixture = new McpBrokerFixture(new InMemoryProfileSecretStore());
        await fixture.Host.StartAsync();
        var channel = await fixture.EnrollAsync();
        var proof = await fixture.ProofAsync(channel);
        await using var proxy = StdioMcpProcess.Start(StdioMcpProcess.Arguments(fixture.WorkspaceId, channel));

        await proxy.InitializeLegacyAsync();
        var tools = await proxy.RequestAsync(1, "tools/list");
        var call = await proxy.RequestAsync(2, "tools/call", StdioMcpProcess.Call("mongo_find", fixture.FindArguments()));
        var exitCode = await proxy.CloseAndWaitAsync();

        Assert.Multiple(() =>
        {
            Assert.That(tools.TryGetProperty("error", out _), Is.True, "Sem prova não há descoberta vinda do broker.");
            Assert.That(call.GetProperty("result").GetProperty("isError").GetBoolean(), Is.True);
            Assert.That(call.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString(),
                Does.StartWith("AuthenticationRequired"));
            Assert.That(fixture.Authority.Authentications, Is.Zero, "Nenhuma credencial chegou ao broker.");
            Assert.That(fixture.Find.Calls, Is.Zero);
            Assert.That(exitCode, Is.Zero, "Falha segura, sem crash.");
            Assert.That(proxy.Stderr, Is.Empty);
            Assert.That(proxy.AllOutput, Does.Not.Contain(proof).And.Not.Contain(McpBrokerFixture.UriCanary));
        });
        proxy.AssertCleanStdout();
    }
}
