using System.Diagnostics;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Application.Agents.Broker;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Infrastructure;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests.Mcp;

/// <summary>
/// M1 (revisão independente de 25/09/2026): an MCP client that reconnects repeatedly on the same enrolled channel must
/// be throttled before the registry, so the flood neither grows the audit ledger per attempt nor monopolizes the single
/// LiteDB owner that every other facet shares. Real broker, registry, channel authority and LiteDB; MongoDB simulated.
/// </summary>
[TestFixture]
[NonParallelizable]
[Category("Integration")]
public sealed class AgentBrokerFloodTests
{
    private const int Burst = 12;
    private const int Connections = 8;
    private const int CallsPerConnection = 10;

    [Test]
    public async Task SequentialReconnectFloodOfOneChannelIsRateLimitedBeforeTheRegistryAndAudit()
    {
        var options = new AgentBrokerOptions
        {
            WorkspaceId = Guid.NewGuid(), Enabled = true, Stage = AgentToolExposureStage.LiteralQueries,
            HandshakeTimeout = TimeSpan.FromSeconds(2), CallBurstPerChannel = Burst, CallsPerMinutePerChannel = 60
        };
        await using var fixture = new McpBrokerFixture(new InMemoryProfileSecretStore(), options);
        await fixture.Host.StartAsync();
        var channel = await fixture.EnrollAsync();
        var proof = await fixture.ProofAsync(channel);
        var audit = (IAgentAuditRepository)fixture.Owner;

        var succeeded = 0;
        var rateLimited = 0;
        var other = new List<string?>();
        var facetLatencies = new List<TimeSpan>();
        var clock = Stopwatch.StartNew();
        for (var connection = 0; connection < Connections; connection++)
        {
            // Every reconnect authenticates again: the budget belongs to the channel, not to the pipe.
            await using var peer = await RawBrokerPeer.ConnectAsync(fixture.WorkspaceId);
            Assert.That((await peer.AuthenticateAsync(channel.ChannelId, proof))?.Type,
                Is.EqualTo(AgentBrokerProtocol.MessageTypes.Authenticated));
            for (var id = 1L; id <= CallsPerConnection; id++)
            {
                await peer.CallAsync(id, "get_indexes", fixture.IndexArguments());
                var answer = await peer.ReceiveAsync();
                if (answer?.Status == AgentBrokerMessage.SucceededStatus) succeeded++;
                else if (answer?.ErrorCode == AgentBrokerProtocol.ErrorCodes.RateLimited)
                {
                    rateLimited++;
                    Assert.That(answer.Dispatched, Is.False, "RateLimited nunca despacha.");
                }
                else other.Add(answer?.ErrorCode);

                // Another facet of the same LiteDB owner keeps answering while the flood runs.
                var facet = Stopwatch.StartNew();
                await fixture.Owner.GetAllAsync();
                facetLatencies.Add(facet.Elapsed);
            }
        }
        clock.Stop();

        var events = await audit.GetRecentAsync(500);
        var refillAllowance = (int)Math.Ceiling(clock.Elapsed.TotalSeconds) + 1;
        TestContext.Out.WriteLine(
            $"flood: {Connections * CallsPerConnection} chamadas, {succeeded} aceitas, {rateLimited} RateLimited, " +
            $"{events.Count} eventos de auditoria, {clock.Elapsed.TotalMilliseconds:F0} ms, " +
            $"faceta LiteDB p95={Percentile(facetLatencies, 0.95).TotalMilliseconds:F1} ms " +
            $"max={facetLatencies.Max().TotalMilliseconds:F1} ms");
        Assert.Multiple(() =>
        {
            Assert.That(other, Is.Empty, "Só sucesso ou RateLimited.");
            Assert.That(succeeded, Is.GreaterThanOrEqualTo(Burst).And.LessThanOrEqualTo(Burst + refillAllowance),
                "Rajada inicial mais a recarga de 1/s durante o teste.");
            Assert.That(rateLimited, Is.EqualTo(Connections * CallsPerConnection - succeeded));
            Assert.That(fixture.Indexes.Calls, Is.EqualTo(succeeded), "Nada limitado chega ao MongoDB.");
            Assert.That(events.Count, Is.EqualTo(2 * succeeded),
                "Auditoria cresce só com chamadas admitidas (intenção + desfecho), não com cada tentativa.");
            Assert.That(events.All(item => item.Channel == AgentAuditChannel.McpExternal), Is.True);
            Assert.That(facetLatencies.Max(), Is.LessThan(TimeSpan.FromSeconds(2)),
                "Outra faceta do proprietário LiteDB continua responsiva durante o flood.");
        });
    }

    private static TimeSpan Percentile(List<TimeSpan> values, double percentile)
    {
        var ordered = values.OrderBy(value => value).ToArray();
        return ordered[(int)Math.Min(ordered.Length - 1, Math.Ceiling(percentile * ordered.Length) - 1)];
    }

}
