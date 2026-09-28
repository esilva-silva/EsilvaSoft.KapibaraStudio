using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Infrastructure;
using LiteDB;

namespace EsilvaSoft.SlopStudio.UnitTests;

/// <summary>
/// M1 (revisão independente de 25/09/2026): count-based rotation of the 10.000-event ledger must not let an external
/// MCP flood evict legitimate evidence. External MCP reads rotate inside a 5.000-event quota; write evidence is never
/// rotated by count (only by the 30-day retention), and a ledger full of protected records fails the append visibly.
/// The ledger is seeded offline by cloning records the owner itself wrote, then exercised through the real owner.
/// </summary>
[TestFixture]
public sealed class LiteDbAgentAuditRetentionTests
{
    private const string CollectionName = "agentAuditEvents";
    private const int Ledger = 10_000;
    private const int ExternalQuota = 5_000;

    [Test]
    public async Task ExternalFloodRotatesOnlyExternalReadsAndNeverEvictsWritesOrInternalReads()
    {
        using var workspace = new Workspace();
        // Oldest first: writes, then internal reads, then a full external flood. Rotating purely by age would
        // evict the writes first.
        await SeedAsync(workspace, (Kind.Write, 100), (Kind.InternalRead, 900), (Kind.ExternalRead, 9_000));

        TimeSpan appendTime, facetWait;
        using (var owner = new LiteDbConnectionProfileRepository(workspace.Path))
        {
            // Evidence for the residual risk: every append decodes the whole ledger under the owner's gate, so other
            // facets wait for it. The broker's rate limit is what bounds how often this happens under a flood.
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var append = ((IAgentAuditRepository)owner).AppendAsync(Terminal(Kind.ExternalRead));
            var facet = System.Diagnostics.Stopwatch.StartNew();
            await owner.GetAllAsync();
            facetWait = facet.Elapsed;
            await append;
            appendTime = clock.Elapsed;
            clock.Restart();
            for (var index = 0; index < 5; index++)
                await ((IAgentAuditRepository)owner).AppendAsync(Terminal(Kind.ExternalRead));
            TestContext.Out.WriteLine($"append em regime (6.000 eventos): {clock.Elapsed.TotalMilliseconds / 5:F0} ms");
        }
        TestContext.Out.WriteLine($"append com ledger cheio: {appendTime.TotalMilliseconds:F0} ms; " +
            $"espera de outra faceta: {facetWait.TotalMilliseconds:F0} ms");

        var counts = Count(workspace);
        Assert.Multiple(() =>
        {
            Assert.That(counts[Kind.Write], Is.EqualTo(100), "Escritas protegidas.");
            Assert.That(counts[Kind.InternalRead], Is.EqualTo(900), "Leituras internas fora da rotação externa.");
            Assert.That(counts[Kind.ExternalRead], Is.EqualTo(ExternalQuota), "Leituras MCP presas à cota.");
        });
    }

    [Test]
    public async Task ExternalQuotaAppliesBeforeTheLedgerIsFull()
    {
        using var workspace = new Workspace();
        await SeedAsync(workspace, (Kind.InternalRead, 10), (Kind.ExternalRead, ExternalQuota));
        string newestExternal;
        using (var raw = workspace.OpenOffline())
            newestExternal = raw.GetCollection(CollectionName).FindAll()
                .OrderByDescending(item => item["occurredAtUtcTicks"].AsInt64).First()["_id"].AsGuid.ToString();

        using (var owner = new LiteDbConnectionProfileRepository(workspace.Path))
            await ((IAgentAuditRepository)owner).AppendAsync(Terminal(Kind.ExternalRead));

        var counts = Count(workspace);
        using var check = workspace.OpenOffline();
        Assert.Multiple(() =>
        {
            Assert.That(counts[Kind.ExternalRead], Is.EqualTo(ExternalQuota));
            Assert.That(counts[Kind.InternalRead], Is.EqualTo(10));
            Assert.That(check.GetCollection(CollectionName).FindById(Guid.Parse(newestExternal)), Is.Not.Null,
                "A rotação remove o mais antigo, não o mais recente.");
        });
    }

    [Test]
    public async Task FullLedgerRotatesInternalReadsBeforeAnyWrite()
    {
        using var workspace = new Workspace();
        await SeedAsync(workspace, (Kind.Write, 9_000), (Kind.InternalRead, 1_000));

        using (var owner = new LiteDbConnectionProfileRepository(workspace.Path))
            await ((IAgentAuditRepository)owner).AppendAsync(Terminal(Kind.InternalRead));

        var counts = Count(workspace);
        Assert.Multiple(() =>
        {
            Assert.That(counts[Kind.Write], Is.EqualTo(9_000));
            Assert.That(counts[Kind.InternalRead], Is.EqualTo(1_000));
        });
    }

    [Test]
    public async Task LedgerFullOfProtectedWritesFailsVisiblyWithoutEvictingAny()
    {
        using var workspace = new Workspace();
        await SeedAsync(workspace, (Kind.Write, Ledger));

        using (var owner = new LiteDbConnectionProfileRepository(workspace.Path))
            Assert.ThrowsAsync<IOException>(() => ((IAgentAuditRepository)owner).AppendAsync(Terminal(Kind.InternalRead)));

        var counts = Count(workspace);
        Assert.Multiple(() =>
        {
            Assert.That(counts[Kind.Write], Is.EqualTo(Ledger));
            Assert.That(counts[Kind.InternalRead], Is.Zero);
        });
    }

    private enum Kind { InternalRead, ExternalRead, Write }

    /// <summary>One single-event group: terminal-only read outcomes and terminal-only write denials are both valid.</summary>
    private static AgentAuditEvent Terminal(Kind kind, DateTimeOffset? at = null)
    {
        var now = at ?? DateTimeOffset.UtcNow;
        var external = kind == Kind.ExternalRead;
        var write = kind == Kind.Write;
        return new AgentAuditEvent(Guid.NewGuid(), AgentAuditEvent.CurrentSchemaVersion,
            now, Guid.NewGuid(), Guid.NewGuid(), null, null,
            external ? AgentAuditChannel.McpExternal : AgentAuditChannel.Internal, external ? "mcp" : null,
            write ? "mongo_insert_one" : "list_connections", 1, write ? AgentToolRisk.Write : AgentToolRisk.ReadOnly,
            null, write ? AgentAuditDecision.Denied : AgentAuditDecision.Allowed,
            write ? AgentAuditOutcome.Denied : AgentAuditOutcome.Succeeded, 1, null, 20, 0, 0)
        {
            NamespaceKind = AgentAuditNamespaceKind.None,
            DecisionReason = write ? AgentAuditDecisionReason.PermissionMissing : AgentAuditDecisionReason.PolicyAllowed,
            ApprovalState = AgentAuditApprovalState.NotRequired,
            StartedAtUtc = now.AddMilliseconds(-20),
            CompletedAtUtc = now
        }.Validate();
    }

    /// <summary>Writes one template per kind through the owner, then clones it offline with fresh ids and older times.</summary>
    private static async Task SeedAsync(Workspace workspace, params (Kind Kind, int Count)[] plan)
    {
        var templates = new Dictionary<Kind, Guid>();
        using (var owner = new LiteDbConnectionProfileRepository(workspace.Path))
        {
            foreach (var kind in plan.Select(item => item.Kind).Distinct())
            {
                var template = Terminal(kind, DateTimeOffset.UtcNow.AddDays(-20));
                await ((IAgentAuditRepository)owner).AppendAsync(template);
                templates[kind] = template.Id;
            }
        }

        using var raw = workspace.OpenOffline();
        var collection = raw.GetCollection(CollectionName);
        var documents = templates.ToDictionary(item => item.Key, item => collection.FindById(item.Value));
        collection.DeleteAll();
        var baseTicks = DateTimeOffset.UtcNow.AddDays(-20).UtcTicks;
        var sequence = 0L;
        var clones = new List<BsonDocument>();
        foreach (var (kind, count) in plan)
        {
            for (var index = 0; index < count; index++)
            {
                var clone = new BsonDocument(documents[kind].ToDictionary(item => item.Key, item => item.Value));
                var ticks = baseTicks + ++sequence * TimeSpan.TicksPerSecond;
                clone["_id"] = Guid.NewGuid();
                clone["invocationId"] = Guid.NewGuid();
                clone["occurredAtUtcTicks"] = ticks;
                clone["completedAtUtcTicks"] = ticks;
                clone["startedAtUtcTicks"] = ticks - 20 * TimeSpan.TicksPerMillisecond;
                clones.Add(clone);
            }
        }
        collection.InsertBulk(clones);
    }

    private static Dictionary<Kind, int> Count(Workspace workspace)
    {
        using var raw = workspace.OpenOffline();
        var counts = Enum.GetValues<Kind>().ToDictionary(kind => kind, _ => 0);
        foreach (var document in raw.GetCollection(CollectionName).FindAll())
        {
            var kind = document["risk"].AsInt32 != (int)AgentToolRisk.ReadOnly ? Kind.Write
                : document["channel"].AsInt32 == (int)AgentAuditChannel.McpExternal ? Kind.ExternalRead
                : Kind.InternalRead;
            counts[kind]++;
        }
        return counts;
    }

    private sealed class Workspace : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SlopStudio.AgentAuditRetention", Guid.NewGuid().ToString("N"));
        public Workspace() => Directory.CreateDirectory(_directory);
        public string Path => System.IO.Path.Combine(_directory, "workspace.db");
        public LiteDatabase OpenOffline() => new($"Filename={Path};Connection=direct");
        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }
}
