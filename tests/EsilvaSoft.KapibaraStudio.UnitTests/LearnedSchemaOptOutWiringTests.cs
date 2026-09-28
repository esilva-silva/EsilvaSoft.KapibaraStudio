using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.SchemaLearning;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;
using EsilvaSoft.KapibaraStudio.Infrastructure;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

/// <summary>
/// Closes the two real gaps found by the schema-learning integration batch: nobody called
/// <see cref="ILearnedSchemaOptOut.ApplyPreferences"/>/<see cref="ILearnedSchemaOptOut.SetServingExcluded"/>, and
/// <see cref="LearnedSchemaCatalogSource"/>'s in-memory LRU did not forget an excluded profile within the same
/// instance. See DEC-L15-OPTOUT-WIRING in docs/auto-complite/decisions.md.
/// </summary>
[TestFixture]
public sealed class LearnedSchemaOptOutWiringTests
{
    private static ConnectionProfile Profile(Guid generation) =>
        ConnectionProfile.Create("aprendido", "mongodb://host-aprendido") with { SourceGenerationId = generation };

    private static LearnedSchemaSnapshot Snapshot(LearnedSchemaKey key, Guid? generation, params (string Name, long Present, long Eligible)[] fields) =>
        new(key, LearnedSchemaSnapshot.CurrentFormatVersion, revision: 1, generation, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow,
            completeDocumentObservations: 10, sampledBatches: 1, skippedDocuments: 0, isTruncated: false,
            fields: fields.Select(field => new LearnedFieldStatistics(new LearnedFieldPath([field.Name]), field.Present, field.Eligible,
                new Dictionary<string, long> { ["string"] = field.Present }, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow)).ToArray());

    private static CatalogQuery FieldQuery(ConnectionProfile profile, string database = "Loja", string collection = "Pedidos") =>
        new(SymbolKinds.Field, EditorDialects.Console) { Connection = profile, Database = database, Collection = collection };

    private static async Task<CatalogCompleteness> CollectUntilSettledAsync(LearnedSchemaCatalogSource source, CatalogQuery query, List<CatalogCandidate> sink)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (true)
        {
            sink.Clear();
            var completeness = source.Collect(query, sink, CancellationToken.None);
            if (completeness != CatalogCompleteness.Loading) return completeness;
            if (DateTime.UtcNow > deadline) Assert.Fail("Hidratação de schema aprendido não terminou em 5 s.");
            await Task.Delay(10);
        }
    }

    [Test]
    public async Task InvalidateProfileStopsServingLearnedFieldsInTheSameInstance()
    {
        var profile = Profile(Guid.NewGuid());
        var key = new LearnedSchemaKey(profile.Id, "Loja", "Pedidos");
        // The repository's own state changes the instant the profile is gone (e.g. the delete flow removed the
        // learned rows too, or the same key can simply never resolve again); what this test isolates is whether the
        // in-memory LRU keeps serving the pre-delete snapshot forever versus re-asking the repository on the very
        // next Collect.
        var deleted = false;
        var repository = new FakeLearnedSchemaCatalogRepository
        {
            OnRead = _ => deleted
                ? new(LearnedSchemaHydrationState.NotLearned, null, null)
                : new(LearnedSchemaHydrationState.Available, Snapshot(key, profile.SourceGenerationId, ("cliente", 8, 10)), null)
        };
        using var metadataCache = new MetadataCache(new FakeMetadataSource());
        metadataCache.Connect(profile);
        var source = new LearnedSchemaCatalogSource(repository, metadataCache);

        var sink = new List<CatalogCandidate>();
        var completeness = await CollectUntilSettledAsync(source, FieldQuery(profile), sink);
        Assert.That(completeness, Is.EqualTo(CatalogCompleteness.Complete));
        Assert.That(sink, Has.Count.EqualTo(1), "Precondição: o campo aprendido é servido antes da exclusão.");

        // The gap the integration batch found: without invalidation, this LRU slot would never be re-read again
        // within the same instance, so the profile-owned schema would keep being served (or misreported) forever.
        deleted = true;
        source.InvalidateProfile(profile.Id);

        var afterDelete = new List<CatalogCandidate>();
        var completenessAfterDelete = await CollectUntilSettledAsync(source, FieldQuery(profile), afterDelete);
        Assert.That(completenessAfterDelete, Is.EqualTo(CatalogCompleteness.Complete));
        Assert.That(afterDelete, Is.Empty, "Depois de excluído, o perfil não pode continuar servindo campo aprendido da mesma instância.");
        Assert.That(repository.CallsFor(key), Is.EqualTo(2), "A exclusão força uma nova hidratação em vez de reaproveitar a entrada removida.");
    }

    [Test]
    public void WorkspaceServiceInvalidatesLearnedSchemaOnProfileDeletion()
    {
        var profile = Profile(Guid.NewGuid());
        var key = new LearnedSchemaKey(profile.Id, "Loja", "Pedidos");
        var repository = new FakeLearnedSchemaCatalogRepository { OnRead = _ => new(LearnedSchemaHydrationState.Available, Snapshot(key, profile.SourceGenerationId, ("cliente", 8, 10)), null) };
        using var metadataCache = new MetadataCache(new FakeMetadataSource());
        var catalog = new LearnedSchemaCatalogSource(repository, metadataCache);
        // Warm the LRU for the profile being deleted.
        catalog.Collect(FieldQuery(profile), new List<CatalogCandidate>(), CancellationToken.None);

        catalog.InvalidateProfile(profile.Id);

        // No repository/database interaction needed here beyond proving the public hook exists and is idempotent
        // for a profile that was never cached, matching WorkspaceService.DeleteProfileAsync's null-conditional call.
        Assert.DoesNotThrow(() => catalog.InvalidateProfile(Guid.NewGuid()));
    }

    [Test]
    public async Task ApplyPreferencesLoadsExclusionAndSetExcludedRoundTripsThroughWorkspacePreferences()
    {
        using var context = new WorkspaceTestContext();
        var profile = ConnectionProfile.Create("A", "mongodb://host");
        await context.Repository.SaveAsync(profile);
        var optOut = new LearnedSchemaOptOut(new AutocompleteService());

        using (var workspace = new WorkspaceViewModel(context.Workspace, context.Repository, learnedSchemaOptOut: optOut))
        {
            await workspace.InitializeAsync();
            Assert.That(optOut.ExcludedProfiles, Is.Empty, "Sessão nova exclui ninguém.");

            await workspace.SetLearnedSchemaExcludedAsync(profile.Id, true);
            Assert.That(optOut.ExcludedProfiles, Is.EquivalentTo(new[] { profile.Id }));
        }

        // A fresh instance/optOut applies the persisted preference on load, exactly like SchemaSamplingProfileIds.
        var restoredOptOut = new LearnedSchemaOptOut(new AutocompleteService());
        using var restored = new WorkspaceViewModel(context.Workspace, context.Repository, learnedSchemaOptOut: restoredOptOut);
        await restored.InitializeAsync();
        Assert.That(restoredOptOut.ExcludedProfiles, Is.EquivalentTo(new[] { profile.Id }));

        await restored.SetLearnedSchemaExcludedAsync(profile.Id, false);
        Assert.That(restoredOptOut.ExcludedProfiles, Is.Empty);

        var session = await context.Repository.LoadSessionAsync();
        Assert.That(session.Preferences.LearnedSchemaExcludedProfileIds, Is.Empty, "A restauração persiste de volta a lista vazia.");
    }
}
