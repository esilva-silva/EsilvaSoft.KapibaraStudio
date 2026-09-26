using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Infrastructure;
using LiteDB;
using Microsoft.Extensions.DependencyInjection;

namespace EsilvaSoft.SlopStudio.UnitTests;

[TestFixture]
public sealed class LiteDbAgentAuditTests
{
    private const string CollectionName = "agentAuditEvents";

    [Test]
    public async Task AppendPersistsClosedVersionedEventAcrossReopenWithoutConnectionSecrets()
    {
        using var workspace = new Workspace();
        var entry = Event() with { ConnectionId = Guid.NewGuid(), SessionId = Guid.NewGuid(),
            ExternalIdentifier = "provider-a", Channel = AgentAuditChannel.ProviderExternal,
            Permission = AgentPermission.ReadMetadata, ItemCount = 7, OutputBytes = 1234 };
        using (var owner = new LiteDbConnectionProfileRepository(workspace.Path, new InMemoryProfileSecretStore()))
        {
            await owner.SaveAsync(ConnectionProfile.Create("name-canary", "mongodb://user:secret-canary@host-canary:27017"));
            await ((IAgentAuditRepository)owner).AppendAsync(entry);
        }
        using (var owner = new LiteDbConnectionProfileRepository(workspace.Path))
        {
            Assert.That((await ((IAgentAuditRepository)owner).GetRecentAsync()).Single(), Is.EqualTo(entry));
        }
        using var raw = workspace.OpenOffline();
        var document = raw.GetCollection(CollectionName).FindById(entry.Id);
        Assert.Multiple(() =>
        {
            Assert.That(document["schemaVersion"].AsInt32, Is.EqualTo(3));
            Assert.That(document["durationMilliseconds"].IsInt64, Is.True);
            Assert.That(document.Count, Is.EqualTo(30));
            Assert.That(document.ToString(), Does.Not.Contain("secret-canary").And.Not.Contain("host-canary")
                .And.Not.Contain("name-canary"));
        });
    }

    [Test]
    public void SensitiveIdentifiersAndOutOfBoundsMetricsAreRejectedBeforeStorage()
    {
        using var workspace = new Workspace();
        using var owner = new LiteDbConnectionProfileRepository(workspace.Path);
        var audit = (IAgentAuditRepository)owner;
        var entry = Event();
        Assert.Multiple(() =>
        {
            Assert.ThrowsAsync<ArgumentException>(() => audit.AppendAsync(entry with { ExternalIdentifier = "mongodb://host/secret" }));
            Assert.ThrowsAsync<ArgumentException>(() => audit.AppendAsync(entry with { Channel = AgentAuditChannel.McpExternal,
                ExternalIdentifier = "client token=secret" }));
            Assert.ThrowsAsync<ArgumentException>(() => audit.AppendAsync(entry with { ToolName = "find:password" }));
            Assert.ThrowsAsync<ArgumentException>(() => audit.AppendAsync(entry with { OutputBytes = AgentAuditEvent.MaximumOutputBytes + 1 }));
            Assert.ThrowsAsync<ArgumentException>(() => audit.AppendAsync(entry with { TurnId = Guid.NewGuid() }));
        });
        Assert.That(audit.GetRecentAsync().GetAwaiter().GetResult(), Is.Empty);
    }

    [Test]
    public async Task UnsupportedSchemaFailsClosedAndPreservesUnreadableDocument()
    {
        using var workspace = new Workspace();
        var first = Event();
        using (var owner = new LiteDbConnectionProfileRepository(workspace.Path))
            await ((IAgentAuditRepository)owner).AppendAsync(first);
        using (var raw = workspace.OpenOffline())
        {
            var collection = raw.GetCollection(CollectionName);
            var document = collection.FindById(first.Id);
            document["schemaVersion"] = 99;
            collection.Update(document);
        }
        using (var owner = new LiteDbConnectionProfileRepository(workspace.Path))
        {
            var audit = (IAgentAuditRepository)owner;
            Assert.ThrowsAsync<InvalidDataException>(() => audit.GetRecentAsync());
            Assert.ThrowsAsync<InvalidDataException>(() => audit.AppendAsync(Event()));
        }
        using var check = workspace.OpenOffline();
        Assert.That(check.GetCollection(CollectionName).FindById(first.Id)["schemaVersion"].AsInt32, Is.EqualTo(99));
        Assert.That(check.GetCollection(CollectionName).Count(), Is.EqualTo(1));
    }

    [Test]
    public async Task LegacyV1RemainsReadableWithoutSilentlyRewritingItsUnknownEvidence()
    {
        using var workspace = new Workspace();
        var first = Event();
        using (var owner = new LiteDbConnectionProfileRepository(workspace.Path))
            await ((IAgentAuditRepository)owner).AppendAsync(first);
        using (var raw = workspace.OpenOffline())
        {
            var collection = raw.GetCollection(CollectionName);
            var document = collection.FindById(first.Id);
            foreach (var field in new[] { "namespaceKind", "databaseName", "collectionName", "namespacePseudonym",
                         "decisionReason", "approvalState", "approvalId", "approvedAtUtcTicks",
                         "startedAtUtcTicks", "completedAtUtcTicks" })
                document.Remove(field);
            document["schemaVersion"] = AgentAuditEvent.LegacySchemaVersion;
            document["durationMilliseconds"] = 20;
            collection.Update(document);
        }
        using (var owner = new LiteDbConnectionProfileRepository(workspace.Path))
        {
            var audit = (IAgentAuditRepository)owner;
            var loaded = (await audit.GetRecentAsync()).Single();
            Assert.Multiple(() =>
            {
                Assert.That(loaded.SchemaVersion, Is.EqualTo(1));
                Assert.That(loaded.NamespaceKind, Is.EqualTo(AgentAuditNamespaceKind.LegacyUnknown));
                Assert.That(loaded.DecisionReason, Is.EqualTo(AgentAuditDecisionReason.LegacyUnknown));
                Assert.That(loaded.ApprovalState, Is.EqualTo(AgentAuditApprovalState.LegacyUnknown));
                Assert.That(loaded.StartedAtUtc, Is.EqualTo(loaded.OccurredAtUtc));
                Assert.That(loaded.CompletedAtUtc, Is.EqualTo(loaded.OccurredAtUtc));
            });
            Assert.ThrowsAsync<ArgumentException>(() => audit.AppendAsync(loaded));
            await audit.AppendAsync(Event());
        }
        using var check = workspace.OpenOffline();
        var preserved = check.GetCollection(CollectionName).FindById(first.Id);
        Assert.That(preserved.Count, Is.EqualTo(20));
        Assert.That(preserved["schemaVersion"].AsInt32, Is.EqualTo(1));
    }

    [Test]
    public async Task PreviousV2RemainsReadableWhileNewAppendsUseV3Int64Duration()
    {
        using var workspace = new Workspace();
        var previous = Event();
        using (var owner = new LiteDbConnectionProfileRepository(workspace.Path))
            await ((IAgentAuditRepository)owner).AppendAsync(previous);
        using (var raw = workspace.OpenOffline())
        {
            var collection = raw.GetCollection(CollectionName);
            var document = collection.FindById(previous.Id);
            document["schemaVersion"] = AgentAuditEvent.PreviousSchemaVersion;
            document["durationMilliseconds"] = 20;
            collection.Update(document);
        }
        using (var owner = new LiteDbConnectionProfileRepository(workspace.Path))
        {
            var audit = (IAgentAuditRepository)owner;
            var loaded = (await audit.GetRecentAsync()).Single();
            Assert.That(loaded.SchemaVersion, Is.EqualTo(2));
            Assert.ThrowsAsync<ArgumentException>(() => audit.AppendAsync(loaded));
            await audit.AppendAsync(Event());
        }
        using var check = workspace.OpenOffline();
        var preserved = check.GetCollection(CollectionName).FindById(previous.Id);
        Assert.Multiple(() =>
        {
            Assert.That(preserved["schemaVersion"].AsInt32, Is.EqualTo(2));
            Assert.That(preserved["durationMilliseconds"].IsInt32, Is.True);
            Assert.That(check.GetCollection(CollectionName).FindAll().Count(), Is.EqualTo(2));
        });
    }

    [Test]
    public async Task StaleReadOnlyIntentGetsOneAuditIncompleteTerminalWithoutClaimingDelivery()
    {
        using var workspace = new Workspace();
        using var owner = new LiteDbConnectionProfileRepository(workspace.Path);
        var audit = (IAgentAuditRepository)owner;
        var started = DateTimeOffset.UtcNow.AddHours(-2);
        var intent = Intent() with { StartedAtUtc = started, OccurredAtUtc = started };
        await audit.AppendAsync(intent);
        Assert.That((await audit.GetPendingAsync()).Select(item => item.Id), Is.EqualTo(new[] { intent.Id }));

        await audit.AppendAsync(Event());
        var incomplete = (await audit.GetRecentAsync()).Single(item => item.Outcome == AgentAuditOutcome.AuditIncomplete);
        Assert.Multiple(() =>
        {
            Assert.That(incomplete.InvocationId, Is.EqualTo(intent.InvocationId));
            Assert.That(incomplete.DecisionReason, Is.EqualTo(AgentAuditDecisionReason.AuditRecovery));
            Assert.That(incomplete.ItemCount, Is.Zero);
            Assert.That(incomplete.OutputBytes, Is.Zero);
        });
        Assert.That(await audit.GetPendingAsync(), Is.Empty);
        await audit.AppendAsync(Event());
        Assert.That((await audit.GetRecentAsync()).Count(item => item.Outcome == AgentAuditOutcome.AuditIncomplete), Is.EqualTo(1));
    }

    [Test]
    public async Task AuditIncompleteWithoutMatchingIntentIsRejectedOnAppendAndStoredReads()
    {
        using var workspace = new Workspace();
        var started = DateTimeOffset.UtcNow.AddHours(-2);
        var intent = Intent() with { StartedAtUtc = started, OccurredAtUtc = started };
        AgentAuditEvent incomplete;
        using (var owner = new LiteDbConnectionProfileRepository(workspace.Path))
        {
            var audit = (IAgentAuditRepository)owner;
            await audit.AppendAsync(intent);
            await audit.AppendAsync(Event());
            incomplete = (await audit.GetRecentAsync()).Single(item => item.Outcome == AgentAuditOutcome.AuditIncomplete);
            Assert.ThrowsAsync<InvalidOperationException>(() => audit.AppendAsync(incomplete with
            {
                Id = Guid.NewGuid(), InvocationId = Guid.NewGuid()
            }));
        }

        using (var raw = workspace.OpenOffline())
            raw.GetCollection(CollectionName).Delete(intent.Id);

        using (var owner = new LiteDbConnectionProfileRepository(workspace.Path))
        {
            var audit = (IAgentAuditRepository)owner;
            Assert.ThrowsAsync<InvalidDataException>(() => audit.GetRecentAsync());
            Assert.ThrowsAsync<InvalidDataException>(() => audit.GetPendingAsync());
            Assert.ThrowsAsync<InvalidDataException>(() => audit.AppendAsync(Event()));
        }
        using var check = workspace.OpenOffline();
        Assert.That(check.GetCollection(CollectionName).FindById(incomplete.Id), Is.Not.Null);
    }

    [Test]
    public async Task StaleReadOnlyIntentAwaitingApprovalRemainsPendingWithoutBlockingNextAppend()
    {
        using var workspace = new Workspace();
        using var owner = new LiteDbConnectionProfileRepository(workspace.Path);
        var audit = (IAgentAuditRepository)owner;
        var started = DateTimeOffset.UtcNow.AddHours(-2);
        var intent = Intent() with
        {
            StartedAtUtc = started, OccurredAtUtc = started,
            ApprovalState = AgentAuditApprovalState.Pending,
            ApprovalId = Guid.NewGuid()
        };
        await audit.AppendAsync(intent);
        await audit.AppendAsync(Event());

        Assert.That((await audit.GetPendingAsync()).Select(item => item.Id), Is.EqualTo(new[] { intent.Id }));
        Assert.That((await audit.GetRecentAsync()).Any(item => item.Outcome == AgentAuditOutcome.AuditIncomplete),
            Is.False);
    }

    [Test]
    public async Task ReadOnlyRecoveryIsBoundedToSixtyFourAndIdempotentAcrossAppends()
    {
        using var workspace = new Workspace();
        var template = Intent();
        using (var owner = new LiteDbConnectionProfileRepository(workspace.Path))
            await ((IAgentAuditRepository)owner).AppendAsync(template);
        using (var raw = workspace.OpenOffline())
        {
            var collection = raw.GetCollection(CollectionName);
            var document = collection.FindById(template.Id);
            var oldTicks = DateTime.UtcNow.AddHours(-2).Ticks;
            document["occurredAtUtcTicks"] = oldTicks;
            document["startedAtUtcTicks"] = oldTicks;
            collection.Update(document);
            for (var index = 0; index < 64; index++)
            {
                var clone = new BsonDocument();
                foreach (var key in document.Keys) clone[key] = document[key];
                clone["_id"] = Guid.NewGuid();
                clone["invocationId"] = Guid.NewGuid();
                collection.Insert(clone);
            }
        }
        using var recoveredOwner = new LiteDbConnectionProfileRepository(workspace.Path);
        var audit = (IAgentAuditRepository)recoveredOwner;
        await audit.AppendAsync(Event());
        Assert.That((await audit.GetPendingAsync()).Count, Is.EqualTo(1));
        Assert.That((await audit.GetRecentAsync(500)).Count(item => item.Outcome == AgentAuditOutcome.AuditIncomplete),
            Is.EqualTo(64));
        await audit.AppendAsync(Event());
        Assert.That(await audit.GetPendingAsync(), Is.Empty);
        Assert.That((await audit.GetRecentAsync(500)).Count(item => item.Outcome == AgentAuditOutcome.AuditIncomplete),
            Is.EqualTo(65));
        await audit.AppendAsync(Event());
        Assert.That((await audit.GetRecentAsync(500)).Count(item => item.Outcome == AgentAuditOutcome.AuditIncomplete),
            Is.EqualTo(65));
    }

    [Test]
    public async Task OldWriteIntentRemainsPendingUntilExplicitVerifiedTerminalAfterInt32DurationRange()
    {
        using var workspace = new Workspace();
        var intent = Intent(AgentToolRisk.Write);
        using (var owner = new LiteDbConnectionProfileRepository(workspace.Path))
            await ((IAgentAuditRepository)owner).AppendAsync(intent);
        using (var raw = workspace.OpenOffline())
        {
            var collection = raw.GetCollection(CollectionName);
            var document = collection.FindById(intent.Id);
            var older = DateTimeOffset.UtcNow.AddDays(-31);
            document["occurredAtUtcTicks"] = older.UtcTicks;
            document["startedAtUtcTicks"] = older.UtcTicks;
            document["schemaVersion"] = AgentAuditEvent.PreviousSchemaVersion;
            document["durationMilliseconds"] = 0;
            collection.Update(document);
        }
        using (var owner = new LiteDbConnectionProfileRepository(workspace.Path))
        {
            var audit = (IAgentAuditRepository)owner;
            await audit.AppendAsync(Event());
            var pending = (await audit.GetPendingAsync()).Single();
            Assert.That(pending.Id, Is.EqualTo(intent.Id));
            Assert.That((await audit.GetRecentAsync()).Any(item => item.Outcome == AgentAuditOutcome.AuditIncomplete), Is.False);
            var completed = DateTimeOffset.UtcNow;
            var terminal = pending with
            {
                Id = Guid.NewGuid(), SchemaVersion = AgentAuditEvent.CurrentSchemaVersion,
                Outcome = AgentAuditOutcome.Succeeded,
                Decision = AgentAuditDecision.ApprovedOnce,
                DecisionReason = AgentAuditDecisionReason.ApprovalGranted,
                ApprovalState = AgentAuditApprovalState.ApprovedOnce,
                ApprovedAtUtc = pending.StartedAtUtc.AddHours(1),
                OccurredAtUtc = completed, CompletedAtUtc = completed,
                DurationMilliseconds = (completed.UtcTicks - pending.StartedAtUtc.UtcTicks +
                    TimeSpan.TicksPerMillisecond - 1) / TimeSpan.TicksPerMillisecond
            };
            Assert.That(terminal.DurationMilliseconds, Is.GreaterThan(int.MaxValue));
            await audit.AppendAsync(terminal);
            Assert.That(await audit.GetPendingAsync(), Is.Empty);
        }
    }

    [Test]
    public async Task V3PersistsExplicitNamespaceDecisionReasonAndLinkedApprovalTimeline()
    {
        using var workspace = new Workspace();
        var intent = Intent(AgentToolRisk.Write) with
        {
            NamespaceKind = AgentAuditNamespaceKind.Explicit,
            DatabaseName = "catalogo",
            CollectionName = "clientes",
            ConnectionId = Guid.NewGuid(),
            Permission = AgentPermission.UpdateDocuments
        };
        var terminalTime = intent.StartedAtUtc.AddMilliseconds(10);
        var terminal = intent with
        {
            Id = Guid.NewGuid(),
            Outcome = AgentAuditOutcome.Succeeded,
            Decision = AgentAuditDecision.ApprovedOnce,
            DecisionReason = AgentAuditDecisionReason.ApprovalGranted,
            ApprovalState = AgentAuditApprovalState.ApprovedOnce,
            ApprovedAtUtc = intent.StartedAtUtc.AddMilliseconds(5),
            OccurredAtUtc = terminalTime,
            CompletedAtUtc = terminalTime,
            DurationMilliseconds = 10
        };
        using (var owner = new LiteDbConnectionProfileRepository(workspace.Path))
        {
            var audit = (IAgentAuditRepository)owner;
            await audit.AppendAsync(intent);
            Assert.ThrowsAsync<InvalidOperationException>(() => audit.AppendAsync(terminal with
            {
                ApprovalId = Guid.NewGuid()
            }));
            await audit.AppendAsync(terminal);
        }
        using (var owner = new LiteDbConnectionProfileRepository(workspace.Path))
        {
            var events = await ((IAgentAuditRepository)owner).GetRecentAsync();
            Assert.That(events, Is.EquivalentTo(new[] { intent, terminal }));
        }
        using var raw = workspace.OpenOffline();
        var document = raw.GetCollection(CollectionName).FindById(terminal.Id);
        Assert.Multiple(() =>
        {
            Assert.That(document["databaseName"].AsString, Is.EqualTo("catalogo"));
            Assert.That(document["collectionName"].AsString, Is.EqualTo("clientes"));
            Assert.That(document["approvalId"].AsGuid, Is.EqualTo(intent.ApprovalId));
            Assert.That(document["approvedAtUtcTicks"].AsInt64, Is.EqualTo(terminal.ApprovedAtUtc!.Value.UtcTicks));
        });
    }

    [Test]
    public async Task ApprovalWaitLongerThanExecutionCapStillAllowsCoherentTerminal()
    {
        using var workspace = new Workspace();
        using var owner = new LiteDbConnectionProfileRepository(workspace.Path);
        var audit = (IAgentAuditRepository)owner;
        var started = DateTimeOffset.UtcNow.AddMinutes(-2);
        var intent = Intent(AgentToolRisk.Write) with { StartedAtUtc = started, OccurredAtUtc = started };
        var completed = DateTimeOffset.UtcNow;
        var terminal = intent with
        {
            Id = Guid.NewGuid(), Outcome = AgentAuditOutcome.Succeeded,
            Decision = AgentAuditDecision.ApprovedOnce,
            DecisionReason = AgentAuditDecisionReason.ApprovalGranted,
            ApprovalState = AgentAuditApprovalState.ApprovedOnce,
            ApprovedAtUtc = started.AddMinutes(1),
            OccurredAtUtc = completed, CompletedAtUtc = completed,
            DurationMilliseconds = (int)Math.Ceiling((completed - started).TotalMilliseconds)
        };
        await audit.AppendAsync(intent);
        Assert.ThrowsAsync<ArgumentException>(() => audit.AppendAsync(terminal with
        {
            DurationMilliseconds = 30_000
        }));
        await audit.AppendAsync(terminal);
        Assert.That((await audit.GetRecentAsync()).Select(item => item.Id),
            Is.EquivalentTo(new[] { intent.Id, terminal.Id }));
    }

    [Test]
    public async Task PendingApprovalCanEndAsCancelledOrFailedWithoutClaimingExecution()
    {
        using var workspace = new Workspace();
        using var owner = new LiteDbConnectionProfileRepository(workspace.Path);
        var audit = (IAgentAuditRepository)owner;
        foreach (var outcome in new[] { AgentAuditOutcome.Cancelled, AgentAuditOutcome.Failed })
        {
            var intent = Intent(AgentToolRisk.Write);
            var completed = intent.StartedAtUtc.AddMilliseconds(50);
            var terminal = intent with
            {
                Id = Guid.NewGuid(), Outcome = outcome, OccurredAtUtc = completed,
                CompletedAtUtc = completed, DurationMilliseconds = 50,
                DecisionReason = outcome == AgentAuditOutcome.Cancelled
                    ? AgentAuditDecisionReason.Cancelled : AgentAuditDecisionReason.ExecutionFailed
            };
            await audit.AppendAsync(intent);
            await audit.AppendAsync(terminal);
        }
        Assert.That(await audit.GetRecentAsync(), Has.Count.EqualTo(4));
    }

    [Test]
    public void WriteSuccessCannotUseAbsentPolicyOrDeniedDecision()
    {
        using var workspace = new Workspace();
        using var owner = new LiteDbConnectionProfileRepository(workspace.Path);
        var audit = (IAgentAuditRepository)owner;
        var intent = Intent(AgentToolRisk.Write);
        var completed = intent.StartedAtUtc.AddMilliseconds(10);
        var terminal = intent with
        {
            Id = Guid.NewGuid(), Outcome = AgentAuditOutcome.Succeeded,
            OccurredAtUtc = completed, CompletedAtUtc = completed,
            DurationMilliseconds = 10, DecisionReason = AgentAuditDecisionReason.ApprovalGranted,
            Decision = AgentAuditDecision.ApprovedOnce,
            ApprovalState = AgentAuditApprovalState.ApprovedOnce,
            ApprovedAtUtc = intent.StartedAtUtc.AddMilliseconds(5)
        };
        Assert.ThrowsAsync<ArgumentException>(() => audit.AppendAsync(terminal with { PolicyRevision = 0 }));
        Assert.ThrowsAsync<ArgumentException>(() => audit.AppendAsync(terminal with { Decision = AgentAuditDecision.Denied }));
        Assert.ThrowsAsync<ArgumentException>(() => audit.AppendAsync(terminal with { Outcome = AgentAuditOutcome.Uncertain,
            Decision = AgentAuditDecision.Denied }));
        Assert.ThrowsAsync<ArgumentException>(() => audit.AppendAsync(terminal with
        {
            DecisionReason = AgentAuditDecisionReason.PolicyMissing
        }));
    }

    [Test]
    public void DecisionReasonAndApprovalMustMatchStageAndTerminalDecision()
    {
        using var workspace = new Workspace();
        using var owner = new LiteDbConnectionProfileRepository(workspace.Path);
        var audit = (IAgentAuditRepository)owner;
        var intent = Intent();
        var terminal = Event();
        Assert.Multiple(() =>
        {
            Assert.ThrowsAsync<ArgumentException>(() => audit.AppendAsync(intent with
            {
                DecisionReason = AgentAuditDecisionReason.PolicyAllowed
            }));
            Assert.ThrowsAsync<ArgumentException>(() => audit.AppendAsync(terminal with
            {
                DecisionReason = AgentAuditDecisionReason.NotEvaluated
            }));
            Assert.ThrowsAsync<ArgumentException>(() => audit.AppendAsync(terminal with
            {
                Outcome = AgentAuditOutcome.Failed, DecisionReason = AgentAuditDecisionReason.ExecutionFailed,
                PolicyRevision = 0
            }));
            Assert.ThrowsAsync<ArgumentException>(() => audit.AppendAsync(terminal with
            {
                Outcome = AgentAuditOutcome.Failed, DecisionReason = AgentAuditDecisionReason.ExecutionFailed,
                Risk = AgentToolRisk.Write
            }));
            Assert.ThrowsAsync<ArgumentException>(() => audit.AppendAsync(terminal with
            {
                Outcome = AgentAuditOutcome.Failed, DecisionReason = AgentAuditDecisionReason.ExecutionFailed,
                Decision = AgentAuditDecision.ApprovedOnce
            }));
            Assert.ThrowsAsync<ArgumentException>(() => audit.AppendAsync(terminal with
            {
                Outcome = AgentAuditOutcome.Failed, DecisionReason = AgentAuditDecisionReason.ExecutionFailed,
                Decision = AgentAuditDecision.Denied, ApprovalState = AgentAuditApprovalState.ApprovedOnce,
                ApprovalId = Guid.NewGuid(), ApprovedAtUtc = terminal.StartedAtUtc
            }));
            Assert.ThrowsAsync<ArgumentException>(() => audit.AppendAsync(terminal with
            {
                Outcome = AgentAuditOutcome.Denied, Decision = AgentAuditDecision.Denied,
                DecisionReason = AgentAuditDecisionReason.PermissionMissing,
                ApprovalState = AgentAuditApprovalState.Rejected, ApprovalId = Guid.NewGuid()
            }));
            Assert.ThrowsAsync<ArgumentException>(() => audit.AppendAsync(terminal with
            {
                Outcome = AgentAuditOutcome.Denied, Decision = AgentAuditDecision.Denied,
                DecisionReason = AgentAuditDecisionReason.ApprovalRejected
            }));
            Assert.ThrowsAsync<ArgumentException>(() => audit.AppendAsync(terminal with
            {
                Outcome = AgentAuditOutcome.Denied, Decision = AgentAuditDecision.Denied,
                DecisionReason = AgentAuditDecisionReason.ApprovalExpired
            }));
        });
    }

    [Test]
    public async Task ExpiredApprovalHasDistinctReasonAndDeniesTheCall()
    {
        using var workspace = new Workspace();
        using var owner = new LiteDbConnectionProfileRepository(workspace.Path);
        var entry = Event() with
        {
            Outcome = AgentAuditOutcome.Denied,
            Decision = AgentAuditDecision.Denied,
            DecisionReason = AgentAuditDecisionReason.ApprovalExpired,
            ApprovalState = AgentAuditApprovalState.Expired,
            ApprovalId = Guid.NewGuid()
        };
        var audit = (IAgentAuditRepository)owner;
        await audit.AppendAsync(entry);
        Assert.That((await audit.GetRecentAsync()).Single(), Is.EqualTo(entry));
    }

    [Test]
    public async Task V3PseudonymRoundTripsWithoutRawNamespace()
    {
        using var workspace = new Workspace();
        using var owner = new LiteDbConnectionProfileRepository(workspace.Path);
        var entry = Event() with { NamespaceKind = AgentAuditNamespaceKind.Pseudonym,
            NamespacePseudonym = "ns_7f4a" };
        var audit = (IAgentAuditRepository)owner;
        await audit.AppendAsync(entry);
        Assert.That((await audit.GetRecentAsync()).Single(), Is.EqualTo(entry));
    }

    [Test]
    public async Task MissingV3FieldFailsClosedAndPreservesDocument()
    {
        using var workspace = new Workspace();
        var entry = Event();
        using (var owner = new LiteDbConnectionProfileRepository(workspace.Path))
            await ((IAgentAuditRepository)owner).AppendAsync(entry);
        using (var raw = workspace.OpenOffline())
        {
            var collection = raw.GetCollection(CollectionName);
            var document = collection.FindById(entry.Id);
            document.Remove("decisionReason");
            collection.Update(document);
        }
        using (var owner = new LiteDbConnectionProfileRepository(workspace.Path))
        {
            var audit = (IAgentAuditRepository)owner;
            Assert.ThrowsAsync<InvalidDataException>(() => audit.GetRecentAsync());
            Assert.ThrowsAsync<InvalidDataException>(() => audit.AppendAsync(Event()));
        }
        using var check = workspace.OpenOffline();
        Assert.That(check.GetCollection(CollectionName).FindById(entry.Id).ContainsKey("decisionReason"), Is.False);
    }

    [Test]
    public void V3RejectsUnboundedNamespaceAndInconsistentApprovalOrTimeline()
    {
        using var workspace = new Workspace();
        using var owner = new LiteDbConnectionProfileRepository(workspace.Path);
        var audit = (IAgentAuditRepository)owner;
        var valid = Event();
        Assert.Multiple(() =>
        {
            Assert.ThrowsAsync<ArgumentException>(() => audit.AppendAsync(valid with
            {
                NamespaceKind = AgentAuditNamespaceKind.Explicit, DatabaseName = new string('x', 121)
            }));
            Assert.ThrowsAsync<ArgumentException>(() => audit.AppendAsync(valid with
            {
                NamespaceKind = AgentAuditNamespaceKind.Explicit, DatabaseName = "mongodb://host/secret"
            }));
            Assert.ThrowsAsync<ArgumentException>(() => audit.AppendAsync(valid with
            {
                ApprovalState = AgentAuditApprovalState.ApprovedOnce, ApprovalId = Guid.NewGuid(),
                ApprovedAtUtc = valid.StartedAtUtc.AddMilliseconds(-1), Decision = AgentAuditDecision.ApprovedOnce
            }));
            Assert.ThrowsAsync<ArgumentException>(() => audit.AppendAsync(valid with
            {
                CompletedAtUtc = valid.CompletedAtUtc!.Value.AddMilliseconds(1)
            }));
            Assert.ThrowsAsync<ArgumentException>(() => audit.AppendAsync(valid with
            {
                DecisionReason = AgentAuditDecisionReason.LegacyUnknown
            }));
            Assert.ThrowsAsync<ArgumentException>(() => audit.AppendAsync(valid with { PolicyRevision = 0 }));
        });
    }

    [Test]
    public async Task ConcurrentAppendsSerializeAndDuplicateEventCannotOverwrite()
    {
        using var workspace = new Workspace();
        using var owner = new LiteDbConnectionProfileRepository(workspace.Path);
        var audit = (IAgentAuditRepository)owner;
        var entries = Enumerable.Range(0, 64).Select(_ => Event()).ToArray();
        await Task.WhenAll(entries.Select(item => audit.AppendAsync(item)));
        Assert.That((await audit.GetRecentAsync(100)).Select(item => item.Id), Is.EquivalentTo(entries.Select(item => item.Id)));
        Assert.ThrowsAsync<InvalidOperationException>(() => audit.AppendAsync(entries[0] with
        {
            Outcome = AgentAuditOutcome.Failed, DecisionReason = AgentAuditDecisionReason.ExecutionFailed
        }));
        Assert.That((await audit.GetRecentAsync(100)).Single(item => item.Id == entries[0].Id), Is.EqualTo(entries[0]));
    }

    [Test]
    public async Task RetentionDeletesExpiredClosedEventsButKeepsUnresolvedIntent()
    {
        using var workspace = new Workspace();
        var intent = Intent(AgentToolRisk.Write);
        var closed = Event();
        using (var owner = new LiteDbConnectionProfileRepository(workspace.Path))
        {
            var audit = (IAgentAuditRepository)owner;
            await audit.AppendAsync(intent);
            await audit.AppendAsync(closed);
        }
        using (var raw = workspace.OpenOffline())
        {
            var collection = raw.GetCollection(CollectionName);
            foreach (var id in new[] { intent.Id, closed.Id })
            {
                var document = collection.FindById(id);
                var shift = TimeSpan.FromDays(31).Ticks;
                document["occurredAtUtcTicks"] = document["occurredAtUtcTicks"].AsInt64 - shift;
                document["startedAtUtcTicks"] = document["startedAtUtcTicks"].AsInt64 - shift;
                if (!document["completedAtUtcTicks"].IsNull)
                    document["completedAtUtcTicks"] = document["completedAtUtcTicks"].AsInt64 - shift;
                collection.Update(document);
            }
        }
        using (var owner = new LiteDbConnectionProfileRepository(workspace.Path))
        {
            var audit = (IAgentAuditRepository)owner;
            await audit.AppendAsync(Event());
            var retained = await audit.GetRecentAsync();
            Assert.That(retained.Select(item => item.Id), Does.Contain(intent.Id));
            Assert.That(retained.Select(item => item.Id), Does.Not.Contain(closed.Id));
        }
    }

    [Test]
    public async Task SecondIntentForOneInvocationIsRejectedAndFirstStaysPending()
    {
        using var workspace = new Workspace();
        using var owner = new LiteDbConnectionProfileRepository(workspace.Path);
        var audit = (IAgentAuditRepository)owner;
        var intent = Intent();
        await audit.AppendAsync(intent);

        Assert.ThrowsAsync<InvalidOperationException>(() => audit.AppendAsync(intent with { Id = Guid.NewGuid() }));
        Assert.That(await audit.GetRecentAsync(), Is.EqualTo(new[] { intent }));
    }

    [Test]
    public async Task TerminalMustMatchEveryTrustedIdentityAndActionDimension()
    {
        using var workspace = new Workspace();
        using var owner = new LiteDbConnectionProfileRepository(workspace.Path);
        var audit = (IAgentAuditRepository)owner;
        var intent = Intent() with { Channel = AgentAuditChannel.ProviderExternal,
            ExternalIdentifier = "provider-a", SessionId = Guid.NewGuid(), TurnId = Guid.NewGuid(),
            ConnectionId = Guid.NewGuid(), Permission = AgentPermission.ReadMetadata };
        await audit.AppendAsync(intent);
        var terminal = intent with { Id = Guid.NewGuid(), Outcome = AgentAuditOutcome.Succeeded,
            OccurredAtUtc = intent.OccurredAtUtc.AddTicks(1), CompletedAtUtc = intent.OccurredAtUtc.AddTicks(1),
            DurationMilliseconds = 1, Decision = AgentAuditDecision.Allowed,
            DecisionReason = AgentAuditDecisionReason.PolicyAllowed };
        AgentAuditEvent[] mismatches = [
            terminal with { PrincipalId = Guid.NewGuid() },
            terminal with { SessionId = Guid.NewGuid() },
            terminal with { TurnId = Guid.NewGuid() },
            terminal with { Channel = AgentAuditChannel.McpExternal },
            terminal with { ExternalIdentifier = "provider-b" },
            terminal with { ToolName = "list_databases" },
            terminal with { ToolVersion = 2 },
            terminal with { Risk = AgentToolRisk.Write, ApprovalId = Guid.NewGuid(),
                ApprovalState = AgentAuditApprovalState.ApprovedOnce,
                ApprovedAtUtc = terminal.StartedAtUtc, Decision = AgentAuditDecision.ApprovedOnce,
                DecisionReason = AgentAuditDecisionReason.ApprovalGranted },
            terminal with { Permission = AgentPermission.ReadSchema },
            terminal with { PolicyRevision = 2 },
            terminal with { ConnectionId = Guid.NewGuid() },
            terminal with { StartedAtUtc = intent.StartedAtUtc.AddTicks(-2),
                OccurredAtUtc = intent.OccurredAtUtc.AddTicks(-1),
                CompletedAtUtc = intent.OccurredAtUtc.AddTicks(-1) }
        ];
        foreach (var mismatch in mismatches)
            Assert.ThrowsAsync<InvalidOperationException>(() => audit.AppendAsync(mismatch));
        Assert.That(await audit.GetRecentAsync(), Is.EqualTo(new[] { intent }));

        await audit.AppendAsync(terminal);
        Assert.That((await audit.GetRecentAsync()).Select(item => item.Id), Is.EquivalentTo(new[] { intent.Id, terminal.Id }));
        Assert.ThrowsAsync<InvalidOperationException>(() => audit.AppendAsync(terminal with { Id = Guid.NewGuid() }));
    }

    [Test]
    public async Task OrphanTerminalCannotResolveLaterIntentWithSameInvocationId()
    {
        using var workspace = new Workspace();
        using var owner = new LiteDbConnectionProfileRepository(workspace.Path);
        var audit = (IAgentAuditRepository)owner;
        var terminal = Event(); // Read-only tool calls may have one terminal event without a write intent.
        await audit.AppendAsync(terminal);
        Assert.ThrowsAsync<InvalidOperationException>(() => audit.AppendAsync(Intent() with
        {
            InvocationId = terminal.InvocationId, OccurredAtUtc = terminal.OccurredAtUtc.AddTicks(1),
            StartedAtUtc = terminal.OccurredAtUtc.AddTicks(1)
        }));
        Assert.That(await audit.GetRecentAsync(), Is.EqualTo(new[] { terminal }));
        var orphanWrite = Event();
        Assert.ThrowsAsync<InvalidOperationException>(() => audit.AppendAsync(orphanWrite with
        {
            Risk = AgentToolRisk.Write, Outcome = AgentAuditOutcome.Succeeded,
            ApprovalId = Guid.NewGuid(), ApprovalState = AgentAuditApprovalState.ApprovedOnce,
            ApprovedAtUtc = orphanWrite.StartedAtUtc, Decision = AgentAuditDecision.ApprovedOnce,
            DecisionReason = AgentAuditDecisionReason.ApprovalGranted
        }));
    }

    [Test]
    public async Task PersistedDuplicateIntentClosesReadsAndAppendsWithoutRewritingEitherRecord()
    {
        using var workspace = new Workspace();
        var intent = Intent();
        var duplicateId = Guid.NewGuid();
        using (var owner = new LiteDbConnectionProfileRepository(workspace.Path))
            await ((IAgentAuditRepository)owner).AppendAsync(intent);
        using (var raw = workspace.OpenOffline())
        {
            var collection = raw.GetCollection(CollectionName);
            var duplicate = collection.FindById(intent.Id);
            duplicate["_id"] = duplicateId;
            collection.Insert(duplicate);
        }
        using (var owner = new LiteDbConnectionProfileRepository(workspace.Path))
        {
            var audit = (IAgentAuditRepository)owner;
            Assert.ThrowsAsync<InvalidDataException>(() => audit.GetRecentAsync());
            Assert.ThrowsAsync<InvalidDataException>(() => audit.AppendAsync(Event()));
        }
        using var check = workspace.OpenOffline();
        Assert.That(check.GetCollection(CollectionName).Count(), Is.EqualTo(2));
    }

    [Test]
    public void DisposedStorageFailsVisiblyAndDiResolvesTheSingleOwner()
    {
        using var workspace = new Workspace();
        var services = new ServiceCollection();
        services.AddSlopStudioInfrastructure(workspace.Path);
        using var provider = services.BuildServiceProvider();
        var owner = provider.GetRequiredService<LiteDbConnectionProfileRepository>();
        var audit = provider.GetRequiredService<IAgentAuditRepository>();
        Assert.That(audit, Is.SameAs(owner));
        Assert.That(provider.GetRequiredService<IAgentAuditRepository>(), Is.SameAs(audit));
        // The shared registry is always composed; the default stage (ADR-056) releases metadata tools only.
        Assert.That(provider.GetRequiredService<IAgentToolRegistry>().GetDescriptors()
            .Select(descriptor => AgentToolExposure.StageOf(descriptor.Name)), Is.All.EqualTo(AgentToolExposureStage.Metadata));
        owner.Dispose();
        Assert.ThrowsAsync<ObjectDisposedException>(() => audit.AppendAsync(Event()));
    }

    private static AgentAuditEvent Event()
    {
        var now = DateTimeOffset.UtcNow;
        return new AgentAuditEvent(Guid.NewGuid(), AgentAuditEvent.CurrentSchemaVersion,
            now, Guid.NewGuid(), Guid.NewGuid(), null, null, AgentAuditChannel.Internal, null,
            "list_connections", 1, AgentToolRisk.ReadOnly, null, AgentAuditDecision.Allowed,
            AgentAuditOutcome.Succeeded, 1, null, 20, 0, 0)
        {
            NamespaceKind = AgentAuditNamespaceKind.None,
            DecisionReason = AgentAuditDecisionReason.PolicyAllowed,
            ApprovalState = AgentAuditApprovalState.NotRequired,
            StartedAtUtc = now.AddMilliseconds(-20),
            CompletedAtUtc = now
        };
    }

    private static AgentAuditEvent Intent(AgentToolRisk risk = AgentToolRisk.ReadOnly)
    {
        var original = Event();
        var approval = risk == AgentToolRisk.ReadOnly ? (Guid?)null : Guid.NewGuid();
        return original with
        {
            Risk = risk,
            Outcome = AgentAuditOutcome.Intent,
            Decision = AgentAuditDecision.Requested,
            DecisionReason = AgentAuditDecisionReason.NotEvaluated,
            ApprovalState = risk == AgentToolRisk.ReadOnly
                ? AgentAuditApprovalState.NotRequired : AgentAuditApprovalState.Pending,
            ApprovalId = approval,
            StartedAtUtc = original.OccurredAtUtc,
            CompletedAtUtc = null,
            DurationMilliseconds = 0
        };
    }

    private sealed class Workspace : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SlopStudio.AgentAuditTests", Guid.NewGuid().ToString("N"));
        public Workspace() => Directory.CreateDirectory(_directory);
        public string Path => System.IO.Path.Combine(_directory, "workspace.db");
        public LiteDatabase OpenOffline() => new($"Filename={Path};Connection=direct");
        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }
}
