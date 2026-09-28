using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture, NonParallelizable]
public sealed class AutocompleteMetricsTests
{
    [Test]
    public async Task InstrumentsCarryOnlyAllowedTagsWithoutUserText()
    {
        var recorded = new ConcurrentBag<(string Instrument, KeyValuePair<string, object?>[] Tags)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) => { if (instrument.Meter.Name == AutocompleteMetrics.MeterName) meterListener.EnableMeasurementEvents(instrument); };
        listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) => recorded.Add((instrument.Name, tags.ToArray())));
        listener.SetMeasurementEventCallback<double>((instrument, _, tags, _) => recorded.Add((instrument.Name, tags.ToArray())));
        listener.Start();

        var source = new FakeMetadataSource { Collections = _ => [new("colecao-secreta", CollectionKind.Collection, """{"$jsonSchema":{"properties":{"campo-secreto":{"bsonType":"string"}}}}""")] };
        using var cache = new MetadataCache(source);
        var profile = ConnectionProfile.Create("conexao-secreta", "mongodb://secret-host");
        cache.Connect(profile);
        await cache.RefreshAsync(new(ConnectionIdentity.From(profile), MetadataScope.Definition, "banco-secreto", "colecao-secreta"));
        new KnowledgeCatalog([new LanguageCatalogSource(), new MetadataCatalogSource(cache)])
            .Query(new(SymbolKinds.Field | SymbolKinds.QueryOperator, EditorDialects.Console, "campo") { Connection = profile, Database = "banco-secreto", Collection = "colecao-secreta" });
        using var session = new CompletionSession();
        await session.RequestAsync(new AutocompleteService(), new AutocompleteRequest("db.getCollection('colecao-secreta').find({ campo-secre", "valor-secreto"), immediate: true);

        Assert.That(recorded.Select(item => item.Instrument), Does.Contain("catalog.query.duration").And.Contain("metadata.refresh.duration")
            .And.Contain("metadata.cache.lookup").And.Contain("completion.requested"));
        foreach (var (instrument, tags) in recorded)
            foreach (var tag in tags)
            {
                Assert.That(AutocompleteMetrics.AllowedTags, Does.Contain(tag.Key), instrument);
                Assert.That(tag.Value?.ToString() ?? "", Does.Not.Contain("secret"), instrument);
            }
    }
}
