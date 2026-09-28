using System.Text.Json.Nodes;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Infrastructure;
using LiteDB;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

/// <summary>Faceta <c>agentConversations</c> on the real LiteDB owner, in a temporary file (ADR-056).</summary>
[TestFixture]
public sealed class LiteDbAgentConversationTests
{
    private const string CollectionName = "agentConversations";
    private const string Provider = "claude-code";
    private static readonly string Hash = new('a', 64);

    [Test]
    public async Task SavedConversationRoundTripsAcrossOwnerInstancesWithNextRevision()
    {
        using var fixture = new Workspace();
        var conversation = Conversation(Provider, "Índices de clientes") with
        {
            ModelId = "claude-opus-5",
            Mode = AgentOperationMode.Planning,
            ProviderSessionId = "session-123",
            Entries =
            [
                new AgentConversationEntry(AgentConversationEntryKind.UserMessage, "Quais índices existem?", Now) with
                {
                    Attachments = [new(AgentAttachmentKind.WorkspaceFile, "clientes.json", "dados/clientes.json", 42, Hash)],
                },
                new AgentConversationEntry(AgentConversationEntryKind.ToolCall, string.Empty, Now) with
                {
                    ToolName = "get_indexes", ToolOutcome = AgentToolResultStatus.Succeeded,
                },
                new AgentConversationEntry(AgentConversationEntryKind.EditProposal, string.Empty, Now) with { ProposalId = Guid.NewGuid() },
            ],
        };

        using (var owner = new LiteDbConnectionProfileRepository(fixture.Path))
        {
            var saved = await Repository(owner).SaveAsync(conversation, 0, default);
            Assert.That(saved.Status, Is.EqualTo(AgentPersistenceStatus.Succeeded));
            Assert.That(saved.Value!.Revision, Is.EqualTo(1));
        }

        using (var owner = new LiteDbConnectionProfileRepository(fixture.Path))
        {
            var loaded = await Repository(owner).GetAsync(conversation.Id, default);
            Assert.That(loaded.Status, Is.EqualTo(AgentPersistenceStatus.Succeeded));
            var value = loaded.Value!;
            Assert.Multiple(() =>
            {
                Assert.That((value.ProviderId, value.Title, value.ModelId, value.Mode, value.ProviderSessionId, value.Revision),
                    Is.EqualTo((Provider, "Índices de clientes", "claude-opus-5", AgentOperationMode.Planning, "session-123", 1L)));
                Assert.That((value.CreatedAt, value.UpdatedAt), Is.EqualTo((conversation.CreatedAt, conversation.UpdatedAt)));
                Assert.That(value.Entries.Select(entry => entry.Kind), Is.EqualTo(conversation.Entries.Select(entry => entry.Kind)));
                Assert.That(value.Entries[0].Attachments.Single(), Is.EqualTo(conversation.Entries[0].Attachments.Single()));
                Assert.That((value.Entries[1].ToolName, value.Entries[1].ToolOutcome), Is.EqualTo(("get_indexes", (AgentToolResultStatus?)AgentToolResultStatus.Succeeded)));
                Assert.That(value.Entries[2].ProposalId, Is.EqualTo(conversation.Entries[2].ProposalId));
            });

            var second = await Repository(owner).SaveAsync(value with { Title = "Renomeada" }, 1, default);
            Assert.That(second.Value!.Revision, Is.EqualTo(2));
        }

        using var raw = fixture.OpenOffline();
        var json = raw.GetCollection(CollectionName).FindById(conversation.Id)["json"].AsString;
        // Enums are stored by name (append-only by name, never renumbered).
        Assert.That(json, Does.Contain("\"Planning\"").And.Contain("\"WorkspaceFile\"").And.Contain("\"ToolCall\""));
    }

    [Test]
    public async Task StaleOrMissingRevisionIsAConflictAndKeepsTheStoredCopy()
    {
        using var fixture = new Workspace();
        using var owner = new LiteDbConnectionProfileRepository(fixture.Path);
        var repository = Repository(owner);
        var conversation = Conversation(Provider, "Original");
        await repository.SaveAsync(conversation, 0, default);
        await repository.SaveAsync(conversation with { Title = "Segunda" }, 1, default);

        var stale = await repository.SaveAsync(conversation with { Title = "Perdida" }, 1, default);
        var recreate = await repository.SaveAsync(conversation with { Title = "Recriada" }, 0, default);
        var missing = await repository.SaveAsync(Conversation(Provider, "Nunca gravada"), 3, default);

        Assert.Multiple(async () =>
        {
            Assert.That((stale.Status, stale.ErrorCode), Is.EqualTo((AgentPersistenceStatus.Conflict, "RevisionMismatch")));
            Assert.That(recreate.Status, Is.EqualTo(AgentPersistenceStatus.Conflict));
            Assert.That(missing.Status, Is.EqualTo(AgentPersistenceStatus.Conflict));
            var stored = (await repository.GetAsync(conversation.Id, default)).Value!;
            Assert.That((stored.Title, stored.Revision), Is.EqualTo(("Segunda", 2L)));
        });
    }

    [Test]
    public async Task ConcurrentSavesWithTheSameExpectedRevisionLetExactlyOneWin()
    {
        using var fixture = new Workspace();
        using var owner = new LiteDbConnectionProfileRepository(fixture.Path);
        var repository = Repository(owner);
        var conversation = Conversation(Provider, "Base");
        await repository.SaveAsync(conversation, 0, default);

        var results = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(index => repository.SaveAsync(conversation with { Title = $"Versão {index}" }, 1, default)));

        Assert.That(results.Count(result => result.Succeeded), Is.EqualTo(1));
        Assert.That(results.Count(result => result.Status == AgentPersistenceStatus.Conflict), Is.EqualTo(7));
        Assert.That((await repository.GetAsync(conversation.Id, default)).Value!.Revision, Is.EqualTo(2));
    }

    [Test]
    public async Task StoredKeepHistoryOptOutRejectsLateSaveWithoutRemovingEarlierHistory()
    {
        using var fixture = new Workspace();
        using var owner = new LiteDbConnectionProfileRepository(fixture.Path);
        var conversations = Repository(owner);
        var permissions = (IAgentProviderPermissionsRepository)owner;
        var original = Conversation(Provider, "Antes do opt-out");
        Assert.That((await conversations.SaveAsync(original, 0, default)).Succeeded, Is.True);

        var optOut = await permissions.SaveAsync(AgentProviderPermissions.Default(Provider) with { KeepHistory = false }, 0, default);
        Assert.That(optOut.Succeeded, Is.True);
        var lateUpdate = await conversations.SaveAsync(original with { Title = "Gravação tardia" }, 1, default);
        var lateNew = await conversations.SaveAsync(Conversation(Provider, "Nova gravação tardia"), 0, default);
        var retained = await conversations.GetAsync(original.Id, default);

        Assert.Multiple(() =>
        {
            Assert.That((lateUpdate.Status, lateUpdate.ErrorCode), Is.EqualTo((AgentPersistenceStatus.Invalid, "HistoryDisabled")));
            Assert.That((lateNew.Status, lateNew.ErrorCode), Is.EqualTo((AgentPersistenceStatus.Invalid, "HistoryDisabled")));
            Assert.That((retained.Value?.Title, retained.Value?.Revision), Is.EqualTo(("Antes do opt-out", 1L)));
        });

        Assert.That((await permissions.SaveAsync(optOut.Value! with { KeepHistory = true }, 1, default)).Succeeded, Is.True);
        Assert.That((await conversations.SaveAsync(original with { Title = "Após reativar" }, 1, default)).Succeeded, Is.True);
    }

    [Test]
    public async Task SecretsAreRedactedBeforeWritingAndTheStoredCopyIsReturned()
    {
        using var fixture = new Workspace();
        const string uri = "mongodb://admin:S3nh4Forte@db.example.com:27017/app";
        var conversation = Conversation(Provider, "Conectar em " + uri) with
        {
            Entries =
            [
                new AgentConversationEntry(AgentConversationEntryKind.UserMessage, "Use " + uri + " e password=\"hunter22\"", Now),
                new AgentConversationEntry(AgentConversationEntryKind.AssistantMessage, "Não vou usar " + uri, Now),
            ],
        };

        using (var owner = new LiteDbConnectionProfileRepository(fixture.Path))
        {
            var saved = await Repository(owner).SaveAsync(conversation, 0, default);
            Assert.That(saved.Value!.Entries[0].Text, Does.Not.Contain("S3nh4Forte").And.Not.Contain("hunter22"));
            Assert.That(saved.Value.Title, Does.Not.Contain("S3nh4Forte"));
        }

        using var raw = fixture.OpenOffline();
        var document = raw.GetCollection(CollectionName).FindById(conversation.Id);
        var stored = document.ToString();
        Assert.That(stored, Does.Not.Contain("S3nh4Forte").And.Not.Contain("hunter22").And.Not.Contain("admin:"));
        Assert.That(stored, Does.Contain("[connection string removida]"));
    }

    [Test]
    public async Task ToolCallsRejectResultTextAttachmentsAndProposalReferencesWithoutChangingStoredConversation()
    {
        using var fixture = new Workspace();
        using var owner = new LiteDbConnectionProfileRepository(fixture.Path);
        var repository = Repository(owner);
        var conversation = Conversation(Provider, "Resumo da ferramenta") with
        {
            Entries = [new AgentConversationEntry(AgentConversationEntryKind.ToolCall, "", Now)
            {
                ToolName = "get_indexes", ToolOutcome = AgentToolResultStatus.Succeeded,
            }],
        };
        var saved = await repository.SaveAsync(conversation, 0, default);
        Assert.That(saved.Status, Is.EqualTo(AgentPersistenceStatus.Succeeded));

        const string resultCanary = "TOOL_RESULT_BSON_CANARY_72e91";
        const string attachmentCanary = "TOOL_ATTACHMENT_CANARY_84d30";
        var tool = saved.Value!.Entries.Single();
        var invalidEntries = new[]
        {
            tool with { Text = "{\"result\":\"" + resultCanary + "\"}" },
            tool with { Attachments = [new(AgentAttachmentKind.WorkspaceFile, attachmentCanary, "data.json", 42, Hash)] },
            tool with { ProposalId = Guid.NewGuid() },
        };
        foreach (var invalid in invalidEntries)
        {
            var refused = await repository.SaveAsync(saved.Value with { Entries = [invalid] }, 1, default);
            Assert.That((refused.Status, refused.ErrorCode),
                Is.EqualTo((AgentPersistenceStatus.Invalid, "ConversationEntryInvalid")));
        }

        var loaded = await repository.GetAsync(conversation.Id, default);
        Assert.Multiple(() =>
        {
            Assert.That((loaded.Status, loaded.Value?.Revision), Is.EqualTo((AgentPersistenceStatus.Succeeded, 1L)));
            Assert.That((loaded.Value!.Entries.Single().Text, loaded.Value.Entries.Single().Attachments.Count,
                loaded.Value.Entries.Single().ToolName, loaded.Value.Entries.Single().ToolOutcome),
                Is.EqualTo(("", 0, "get_indexes", (AgentToolResultStatus?)AgentToolResultStatus.Succeeded)));
        });

        owner.Dispose();
        using var raw = fixture.OpenOffline();
        var stored = raw.GetCollection(CollectionName).FindById(conversation.Id).ToString();
        Assert.That(stored, Does.Not.Contain(resultCanary).And.Not.Contain(attachmentCanary));
    }

    [TestCase("Text")]
    [TestCase("Attachments")]
    public async Task LegacyToolCallWithUnexpectedContentIsUnreadableAndCannotBeOverwritten(string member)
    {
        using var fixture = new Workspace();
        var conversation = Conversation(Provider, "Legado") with
        {
            Entries = [new AgentConversationEntry(AgentConversationEntryKind.ToolCall, "", Now)
            {
                ToolName = "get_indexes", ToolOutcome = AgentToolResultStatus.Succeeded,
            }],
        };
        using (var owner = new LiteDbConnectionProfileRepository(fixture.Path))
            Assert.That((await Repository(owner).SaveAsync(conversation, 0, default)).Succeeded, Is.True);

        const string canary = "LEGACY_TOOL_RESULT_CANARY_0a6bf";
        string original;
        using (var raw = fixture.OpenOffline())
        {
            var collection = raw.GetCollection(CollectionName);
            var stored = collection.FindById(conversation.Id);
            var node = JsonNode.Parse(stored["json"].AsString)!.AsObject();
            var entry = node["Entries"]!.AsArray()[0]!.AsObject();
            if (member == "Text") entry["Text"] = canary;
            else entry["Attachments"]!.AsArray().Add(new JsonObject
            {
                ["Kind"] = "WorkspaceFile", ["DisplayName"] = canary,
                ["PathOrName"] = "data.json", ["SizeBytes"] = 42, ["Sha256"] = Hash,
            });
            stored["json"] = node.ToJsonString();
            collection.Update(stored);
            original = collection.FindById(conversation.Id).ToString();
            Assert.That(original, Does.Contain(canary));
        }

        using (var owner = new LiteDbConnectionProfileRepository(fixture.Path))
        {
            var repository = Repository(owner);
            var listed = await repository.ListAsync(Provider, default);
            var loaded = await repository.GetAsync(conversation.Id, default);
            var overwrite = await repository.SaveAsync(conversation with { Title = "Substituição" }, 1, default);
            Assert.Multiple(() =>
            {
                Assert.That(listed.Value!.Single().State, Is.EqualTo(AgentConversationSummaryState.Unreadable));
                Assert.That(loaded.Status, Is.EqualTo(AgentPersistenceStatus.Unreadable));
                Assert.That(overwrite.Status, Is.EqualTo(AgentPersistenceStatus.Unreadable));
            });
        }

        using var reopened = fixture.OpenOffline();
        Assert.That(reopened.GetCollection(CollectionName).FindById(conversation.Id).ToString(), Is.EqualTo(original));
    }

    [Test]
    public async Task ExternalAbsolutePathsAndAttachmentContentCannotBeStored()
    {
        using var fixture = new Workspace();
        using var owner = new LiteDbConnectionProfileRepository(fixture.Path);
        var repository = Repository(owner);
        var absolute = OperatingSystem.IsWindows() ? @"C:\Users\ana\secrets.txt" : "/home/ana/secrets.txt";

        var rooted = await repository.SaveAsync(WithAttachment(new(AgentAttachmentKind.ExternalFile, "secrets.txt", absolute, 1, Hash)), 0, default);
        var externalWithFolder = await repository.SaveAsync(WithAttachment(new(AgentAttachmentKind.ExternalFile, "a.txt", "pasta/a.txt", 1, Hash)), 0, default);
        var escaping = await repository.SaveAsync(WithAttachment(new(AgentAttachmentKind.WorkspaceFile, "a.txt", "../fora/a.txt", 1, Hash)), 0, default);
        var badHash = await repository.SaveAsync(WithAttachment(new(AgentAttachmentKind.WorkspaceFile, "a.txt", "a.txt", 1, "XYZ")), 0, default);
        var nameOnly = await repository.SaveAsync(WithAttachment(new(AgentAttachmentKind.ExternalFile, "a.txt", "a.txt", 1, Hash)), 0, default);

        Assert.Multiple(() =>
        {
            Assert.That(rooted.Status, Is.EqualTo(AgentPersistenceStatus.Invalid));
            Assert.That(externalWithFolder.Status, Is.EqualTo(AgentPersistenceStatus.Invalid));
            Assert.That(escaping.Status, Is.EqualTo(AgentPersistenceStatus.Invalid));
            Assert.That(badHash.Status, Is.EqualTo(AgentPersistenceStatus.Invalid));
            Assert.That(nameOnly.Status, Is.EqualTo(AgentPersistenceStatus.Succeeded));
        });
        var listed = await repository.ListAsync(Provider, default);
        Assert.That(listed.Value, Has.Count.EqualTo(1));

        static AgentConversation WithAttachment(AgentAttachmentDescriptor attachment) =>
            Conversation(Provider, "Anexo") with
            {
                Entries = [new AgentConversationEntry(AgentConversationEntryKind.UserMessage, "Veja", Now) with { Attachments = [attachment] }],
            };
    }

    [Test]
    public async Task EntryAndDocumentSizeLimitsAreRefusedBeforeWriting()
    {
        using var fixture = new Workspace();
        using var owner = new LiteDbConnectionProfileRepository(fixture.Path);
        var repository = Repository(owner);
        var entryLimit = AgentConversationDocumentCodec.MaximumEntryTextChars;

        var atLimit = await repository.SaveAsync(Conversation(Provider, "Limite") with
        {
            Entries = [new(AgentConversationEntryKind.UserMessage, new string('x', entryLimit), Now)],
        }, 0, default);
        var tooLargeEntry = await repository.SaveAsync(Conversation(Provider, "Grande") with
        {
            Entries = [new(AgentConversationEntryKind.UserMessage, new string('x', entryLimit + 1), Now)],
        }, 0, default);
        var entries = Enumerable.Range(0, AgentConversationDocumentCodec.MaximumDocumentBytes / entryLimit + 1)
            .Select(_ => new AgentConversationEntry(AgentConversationEntryKind.AssistantMessage, new string('y', entryLimit), Now))
            .ToArray();
        var tooLargeDocument = await repository.SaveAsync(Conversation(Provider, "Documento") with { Entries = entries }, 0, default);

        Assert.Multiple(async () =>
        {
            Assert.That(atLimit.Status, Is.EqualTo(AgentPersistenceStatus.Succeeded));
            Assert.That((tooLargeEntry.Status, tooLargeEntry.ErrorCode), Is.EqualTo((AgentPersistenceStatus.Invalid, "ConversationEntryTooLarge")));
            Assert.That((tooLargeDocument.Status, tooLargeDocument.ErrorCode), Is.EqualTo((AgentPersistenceStatus.Invalid, "ConversationTooLarge")));
            Assert.That((await repository.ListAsync(null, default)).Value, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task CreatingBeyondTheProviderLimitIsRefusedWithoutDeletingAnything()
    {
        using var fixture = new Workspace();
        using var owner = new LiteDbConnectionProfileRepository(fixture.Path);
        var repository = Repository(owner);
        for (var index = 0; index < AgentConversationDocumentCodec.MaximumConversationsPerProvider; index++)
            Assert.That((await repository.SaveAsync(Conversation(Provider, $"C{index}"), 0, default)).Succeeded, Is.True);

        var refused = await repository.SaveAsync(Conversation(Provider, "Excedente"), 0, default);
        var otherProvider = await repository.SaveAsync(Conversation("openai", "Outro provider"), 0, default);

        Assert.Multiple(async () =>
        {
            Assert.That((refused.Status, refused.ErrorCode), Is.EqualTo((AgentPersistenceStatus.Invalid, "ConversationLimitReached")));
            Assert.That(otherProvider.Status, Is.EqualTo(AgentPersistenceStatus.Succeeded));
            Assert.That((await repository.ListAsync(Provider, default)).Value,
                Has.Count.EqualTo(AgentConversationDocumentCodec.MaximumConversationsPerProvider));
        });
    }

    [Test]
    public async Task UnreadableDocumentIsListedRefusedAndPreservedUntilAnExplicitDelete()
    {
        using var fixture = new Workspace();
        var id = Guid.NewGuid();
        BsonDocument original;
        using (var raw = fixture.OpenOffline())
        {
            original = StoredDocument(id, Provider, "{ isto não é json");
            raw.GetCollection(CollectionName).Insert(original);
        }

        using (var owner = new LiteDbConnectionProfileRepository(fixture.Path))
        {
            var repository = Repository(owner);
            var listed = await repository.ListAsync(Provider, default);
            var get = await repository.GetAsync(id, default);
            var overwrite = await repository.SaveAsync(Conversation(Provider, "Nova") with { Id = id }, 0, default);
            var overwriteWithRevision = await repository.SaveAsync(Conversation(Provider, "Nova") with { Id = id }, 1, default);
            var checkedDelete = await repository.DeleteAsync(id, 1, default);

            Assert.Multiple(() =>
            {
                var summary = listed.Value!.Single();
                Assert.That((summary.Id, summary.State, summary.Title), Is.EqualTo((id, AgentConversationSummaryState.Unreadable, (string?)null)));
                Assert.That(get.Status, Is.EqualTo(AgentPersistenceStatus.Unreadable));
                Assert.That(overwrite.Status, Is.EqualTo(AgentPersistenceStatus.Unreadable));
                Assert.That(overwriteWithRevision.Status, Is.EqualTo(AgentPersistenceStatus.Unreadable));
                Assert.That(checkedDelete.Status, Is.EqualTo(AgentPersistenceStatus.Unreadable));
            });
        }

        using (var raw = fixture.OpenOffline())
            Assert.That(raw.GetCollection(CollectionName).FindById(id).ToString(), Is.EqualTo(original.ToString()));

        using (var owner = new LiteDbConnectionProfileRepository(fixture.Path))
        {
            var explicitDelete = await Repository(owner).DeleteAsync(id, null, default);
            Assert.That(explicitDelete.Status, Is.EqualTo(AgentPersistenceStatus.Succeeded));
            Assert.That((await Repository(owner).GetAsync(id, default)).Status, Is.EqualTo(AgentPersistenceStatus.NotFound));
        }
    }

    [Test]
    public async Task UnknownMembersAndTamperedMetadataMakeTheDocumentUnreadable()
    {
        using var fixture = new Workspace();
        var conversation = Conversation(Provider, "Válida");
        using (var owner = new LiteDbConnectionProfileRepository(fixture.Path))
            await Repository(owner).SaveAsync(conversation, 0, default);

        var extraMember = Guid.NewGuid();
        var mismatchedRevision = Guid.NewGuid();
        using (var raw = fixture.OpenOffline())
        {
            var collection = raw.GetCollection(CollectionName);
            var stored = collection.FindById(conversation.Id);
            var node = JsonNode.Parse(stored["json"].AsString)!.AsObject();
            node["Id"] = extraMember.ToString();
            node["FieldFromTheFuture"] = true;
            collection.Insert(StoredDocument(extraMember, Provider, node.ToJsonString()));
            node.Remove("FieldFromTheFuture");
            node["Id"] = mismatchedRevision.ToString();
            var tampered = StoredDocument(mismatchedRevision, Provider, node.ToJsonString());
            tampered["revision"] = 7L;
            collection.Insert(tampered);
        }

        using var reopened = new LiteDbConnectionProfileRepository(fixture.Path);
        var repository = Repository(reopened);
        Assert.Multiple(async () =>
        {
            Assert.That((await repository.GetAsync(conversation.Id, default)).Status, Is.EqualTo(AgentPersistenceStatus.Succeeded));
            Assert.That((await repository.GetAsync(extraMember, default)).Status, Is.EqualTo(AgentPersistenceStatus.Unreadable));
            Assert.That((await repository.GetAsync(mismatchedRevision, default)).Status, Is.EqualTo(AgentPersistenceStatus.Unreadable));
        });
    }

    [Test]
    public async Task NewerFormatVersionIsReadOnlyAndListedAsUnsupported()
    {
        using var fixture = new Workspace();
        var id = Guid.NewGuid();
        using (var raw = fixture.OpenOffline())
        {
            var future = StoredDocument(id, Provider, "{\"shape\":\"v2\"}");
            future["formatVersion"] = AgentConversation.CurrentFormatVersion + 1;
            future["extra"] = "campo novo";
            raw.GetCollection(CollectionName).Insert(future);
        }

        using var owner = new LiteDbConnectionProfileRepository(fixture.Path);
        var repository = Repository(owner);
        var summary = (await repository.ListAsync(null, default)).Value!.Single();
        var get = await repository.GetAsync(id, default);
        var save = await repository.SaveAsync(Conversation(Provider, "Downgrade") with { Id = id }, 1, default);

        Assert.Multiple(() =>
        {
            Assert.That((summary.State, summary.Title, summary.Revision),
                Is.EqualTo((AgentConversationSummaryState.UnsupportedVersion, "Título", 1L)));
            Assert.That(get.Status, Is.EqualTo(AgentPersistenceStatus.UnsupportedVersion));
            Assert.That(save.Status, Is.EqualTo(AgentPersistenceStatus.UnsupportedVersion));
        });
    }

    [Test]
    public async Task ListIsNewestFirstAndFilteredByProvider()
    {
        using var fixture = new Workspace();
        using var owner = new LiteDbConnectionProfileRepository(fixture.Path);
        var repository = Repository(owner);
        var older = Conversation(Provider, "Antiga") with { UpdatedAt = Now.AddDays(-2) };
        var newer = Conversation(Provider, "Recente") with { UpdatedAt = Now };
        var other = Conversation("openai", "Outro") with { UpdatedAt = Now.AddDays(-1) };
        foreach (var conversation in new[] { older, newer, other }) await repository.SaveAsync(conversation, 0, default);

        var mine = await repository.ListAsync(Provider, default);
        var all = await repository.ListAsync(null, default);

        Assert.Multiple(() =>
        {
            Assert.That(mine.Value!.Select(summary => summary.Title), Is.EqualTo(ExpectedMine));
            Assert.That(all.Value!.Select(summary => summary.Title), Is.EqualTo(ExpectedAll));
            Assert.That(mine.Value!.All(summary => summary.State == AgentConversationSummaryState.Readable), Is.True);
        });
    }

    [Test]
    public async Task DeleteAllRemovesOnlyTheRequestedProviderIncludingItsUnreadableDocuments()
    {
        using var fixture = new Workspace();
        var unreadable = Guid.NewGuid();
        using (var raw = fixture.OpenOffline())
            raw.GetCollection(CollectionName).Insert(StoredDocument(unreadable, Provider, "não é json"));

        using var owner = new LiteDbConnectionProfileRepository(fixture.Path);
        var repository = Repository(owner);
        await repository.SaveAsync(Conversation(Provider, "A"), 0, default);
        await repository.SaveAsync(Conversation(Provider, "B"), 0, default);
        var kept = Conversation("openai", "Fica");
        await repository.SaveAsync(kept, 0, default);

        var deleted = await repository.DeleteAllAsync(Provider, default);
        Assert.That(deleted.Value, Is.EqualTo(3));
        Assert.That((await repository.ListAsync(null, default)).Value!.Select(summary => summary.Id), Is.EqualTo(new[] { kept.Id }));

        var all = await repository.DeleteAllAsync(null, default);
        Assert.That(all.Value, Is.EqualTo(1));
        Assert.That((await repository.ListAsync(null, default)).Value, Is.Empty);
    }

    [Test]
    public async Task RevisionCheckedDeleteDetectsConflictsAndMissingDocuments()
    {
        using var fixture = new Workspace();
        using var owner = new LiteDbConnectionProfileRepository(fixture.Path);
        var repository = Repository(owner);
        var conversation = Conversation(Provider, "Apagar");
        await repository.SaveAsync(conversation, 0, default);

        Assert.That((await repository.DeleteAsync(conversation.Id, 5, default)).Status, Is.EqualTo(AgentPersistenceStatus.Conflict));
        Assert.That((await repository.DeleteAsync(conversation.Id, 1, default)).Succeeded, Is.True);
        Assert.That((await repository.DeleteAsync(conversation.Id, null, default)).Status, Is.EqualTo(AgentPersistenceStatus.NotFound));
    }

    [Test]
    public async Task InvalidValuesAndDisposedOwnerAreTypedFailuresNotExceptions()
    {
        using var fixture = new Workspace();
        var owner = new LiteDbConnectionProfileRepository(fixture.Path);
        var repository = Repository(owner);

        var badProvider = await repository.SaveAsync(Conversation("Claude Code", "x"), 0, default);
        var emptyId = await repository.SaveAsync(Conversation(Provider, "x") with { Id = Guid.Empty }, 0, default);
        var badMode = await repository.SaveAsync(Conversation(Provider, "x") with { Mode = (AgentOperationMode)42 }, 0, default);
        var nullEntries = await repository.SaveAsync(Conversation(Provider, "x") with { Entries = null! }, 0, default);
        var negativeRevision = await repository.SaveAsync(Conversation(Provider, "x"), -1, default);
        owner.Dispose();
        var afterDispose = await repository.ListAsync(null, default);

        Assert.Multiple(() =>
        {
            Assert.That(badProvider.Status, Is.EqualTo(AgentPersistenceStatus.Invalid));
            Assert.That(emptyId.Status, Is.EqualTo(AgentPersistenceStatus.Invalid));
            Assert.That(badMode.Status, Is.EqualTo(AgentPersistenceStatus.Invalid));
            Assert.That(nullEntries.Status, Is.EqualTo(AgentPersistenceStatus.Invalid));
            Assert.That(negativeRevision.Status, Is.EqualTo(AgentPersistenceStatus.Invalid));
            Assert.That((afterDispose.Status, afterDispose.ErrorCode), Is.EqualTo((AgentPersistenceStatus.Failed, "StoreFailed")));
        });
    }

    private static readonly DateTimeOffset Now = new(2026, 9, 26, 12, 30, 15, 123, TimeSpan.FromHours(-3));

    private static readonly string[] ExpectedMine = ["Recente", "Antiga"];
    private static readonly string[] ExpectedAll = ["Recente", "Outro", "Antiga"];

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1859", Justification = "Facet members are explicit interface implementations.")]
    private static IAgentConversationRepository Repository(LiteDbConnectionProfileRepository owner) => owner;

    private static AgentConversation Conversation(string providerId, string title) =>
        new(Guid.NewGuid(), providerId, title, null, AgentOperationMode.Agent, null, Now.AddHours(-1), Now, 0,
            [new(AgentConversationEntryKind.UserMessage, "Olá", Now)]);

    private static BsonDocument StoredDocument(Guid id, string providerId, string json) => new()
    {
        ["_id"] = id,
        ["formatVersion"] = AgentConversation.CurrentFormatVersion,
        ["providerId"] = providerId,
        ["revision"] = 1L,
        ["updatedAtUtc"] = DateTime.UtcNow,
        ["title"] = "Título",
        ["json"] = json,
    };

    private sealed class Workspace : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "KapibaraStudio.Tests", Guid.NewGuid().ToString("N"));
        public string Path => System.IO.Path.Combine(_directory, "workspace.db");
        public LiteDatabase OpenOffline()
        {
            Directory.CreateDirectory(_directory);
            return new($"Filename={Path};Connection=direct");
        }
        public void Dispose()
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }
    }
}
