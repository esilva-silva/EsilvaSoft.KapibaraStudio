using EsilvaSoft.KapibaraStudio.Application.SchemaLearning;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Completion;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests.Language.Completion;

[TestFixture]
public sealed class LearnedSchemaCompletionIntegrationTests
{
    [Test]
    public async Task CompletionServicePublishesLearnedFieldAfterCatalogHydration()
    {
        var generation = Guid.NewGuid();
        var profile = ConnectionProfile.Create("aprendido", "mongodb://host-aprendido") with
        {
            SourceGenerationId = generation
        };
        var key = new LearnedSchemaKey(profile.Id, "Loja", "Pedidos");
        var repository = new FakeLearnedSchemaCatalogRepository
        {
            OnRead = queried => new LearnedSchemaHydrationResult(
                LearnedSchemaHydrationState.Available,
                new LearnedSchemaSnapshot(
                    queried,
                    LearnedSchemaSnapshot.CurrentFormatVersion,
                    revision: 1,
                    generation,
                    DateTimeOffset.UtcNow.AddMinutes(-5),
                    DateTimeOffset.UtcNow,
                    completeDocumentObservations: 10,
                    sampledBatches: 1,
                    skippedDocuments: 0,
                    isTruncated: false,
                    fields:
                    [
                        new LearnedFieldStatistics(
                            new LearnedFieldPath(["cliente"]),
                            presentDocumentObservations: 8,
                            eligibleDocumentObservations: 10,
                            typeObservations: new Dictionary<string, long> { ["string"] = 8 },
                            DateTimeOffset.UtcNow.AddMinutes(-5),
                            DateTimeOffset.UtcNow)
                    ]),
                null)
        };
        using var metadataCache = new MetadataCache(new FakeMetadataSource());
        metadataCache.Connect(profile);
        var learned = new LearnedSchemaCatalogSource(repository, metadataCache);
        var catalog = new KnowledgeCatalog([learned]);
        var service = new CompletionService(catalog, profiles: new SingleProfileResolver(profile));
        var context = new CompletionContext(new(1, 1), EditorDialects.Console, SymbolKinds.Field, "cli", new(0, 3))
        {
            Scope = new CatalogScope(ConnectionIdentity.From(profile), "Loja", "Pedidos"),
            CatalogAccess = MetadataAccess.Peek
        };

        CompletionList? result = null;
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            result = await service.CompleteAsync(context);
            if (result.Items.Count > 0) break;
            await Task.Delay(10);
        }

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Items, Has.Count.EqualTo(1));
        var item = result.Items[0];
        Assert.Multiple(() =>
        {
            Assert.That(item.Label, Is.EqualTo("cliente"));
            Assert.That(item.FieldPath, Is.EqualTo("cliente"));
            Assert.That(item.Documentation!.Evidence.HasFlag(EvidenceSources.Learned), Is.True);
            Assert.That(repository.ReadAvailabilityCalls, Is.GreaterThan(0));
        });
    }

    private sealed class SingleProfileResolver(ConnectionProfile profile) : ICompletionProfileResolver
    {
        public ConnectionProfile? Resolve(Guid profileId) => profileId == profile.Id ? profile : null;
    }
}
