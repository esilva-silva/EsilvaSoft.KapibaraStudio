using System.Globalization;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Benchmarks;

/// <summary>
/// Retained memory of the catalog in the 1 000 collections × 1 000 fields scenario, measured with full collections.
/// Every collection has a validator; editing visits more collections than the LRU keeps.
/// </summary>
public static class MemoryScenario
{
    public static async Task RunAsync(TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);
        const int collections = 1_000;
        const int fields = 1_000;
        const int visited = 80;
        var options = new MetadataCacheOptions();
        var profile = ConnectionProfile.Create("memoria", "mongodb://memoria");
        var identity = ConnectionIdentity.From(profile);
        var source = new SyntheticMetadataSource(collections, fields, validatorsForAll: true);
        var baseline = Measure();
        using var cache = new MetadataCache(source, options: options);
        cache.Connect(profile);
        await cache.RefreshAsync(new(identity, MetadataScope.Collections, "db")).ConfigureAwait(false);
        var names = Measure();
        for (var index = 0; index < visited; index++)
            await cache.RefreshAsync(new(identity, MetadataScope.Definition, "db", source.CollectionNames[index])).ConfigureAwait(false);
        var definitions = Measure();
        for (var index = 0; index < visited; index++) await cache.SampleSchemaAsync(profile, "db", source.CollectionNames[index]).ConfigureAwait(false);
        var sampled = Measure();
        var catalog = new KnowledgeCatalog([new LanguageCatalogSource(), new MetadataCatalogSource(cache)]);
        var result = catalog.Query(new(SymbolKinds.Field, EditorDialects.Console) { Connection = profile, Database = "db", Collection = source.CollectionNames[visited - 1] });
        var merged = Measure();
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"Cenário: {collections} coleções × {fields} campos, validator em todas; {visited} coleções visitadas com definição e amostra explícita; LRU de {options.CollectionScopedEntriesPerConnection} entradas por conexão; consulta com {result.Candidates.Count} candidatos."));
        Write(output, "Nomes de coleções", names - baseline);
        Write(output, "Definições com validator retidas (LRU)", definitions - names);
        Write(output, "Schemas amostrados retidos (LRU compartilhado)", sampled - definitions);
        Write(output, "Schema mesclado e tabelas da consulta", merged - sampled);
        Write(output, "Total retido", merged - baseline);
        GC.KeepAlive(cache);
        GC.KeepAlive(catalog);
    }

    private static long Measure() => GC.GetTotalMemory(forceFullCollection: true);

    private static void Write(TextWriter output, string label, long bytes) =>
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{label}: {bytes / 1024d / 1024d:F1} MB"));
}
