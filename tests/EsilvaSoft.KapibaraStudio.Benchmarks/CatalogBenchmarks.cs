using BenchmarkDotNet.Attributes;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Benchmarks;

/// <summary>Catalog queries answered from memory for the collection and field scale matrix.</summary>
[MemoryDiagnoser]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "BenchmarkDotNet owns the lifecycle and releases the cache in GlobalCleanup.")]
public class CatalogQueryBenchmarks
{
    private MetadataCache _cache = null!;
    private KnowledgeCatalog _catalog = null!;
    private CatalogQuery _collections = null!;
    private CatalogQuery _fieldPrefix = null!;
    private CatalogQuery _fieldHumps = null!;
    private CatalogQuery _nestedFields = null!;
    private CatalogQuery _operators = null!;

    [Params(10, 100, 1_000)]
    public int Collections { get; set; }

    [Params(100, 1_000, 10_000)]
    public int Fields { get; set; }

    [GlobalSetup]
    public async Task SetupAsync()
    {
        var profile = ConnectionProfile.Create("bench", "mongodb://bench");
        var source = new SyntheticMetadataSource(Collections, Fields);
        _cache = new MetadataCache(source, options: new MetadataCacheOptions { SchemaMaximumNodes = 20_000 });
        _cache.Connect(profile);
        var identity = ConnectionIdentity.From(profile);
        _cache.PutCollections(profile, "db", source.CollectionNames);
        _cache.PutIndexes(profile, "db", source.CollectionNames[^1], await source.ListIndexesAsync(profile, "db", source.CollectionNames[^1], CancellationToken.None));
        await _cache.RefreshAsync(new(identity, MetadataScope.Definition, "db", source.CollectionNames[^1]));
        _catalog = new KnowledgeCatalog([new LanguageCatalogSource(), new MetadataCatalogSource(_cache)]);
        var fields = new CatalogQuery(SymbolKinds.Field, EditorDialects.Console) { Connection = profile, Database = "db", Collection = source.CollectionNames[^1] };
        _collections = new(SymbolKinds.Collection, EditorDialects.Console, "colecao00") { Connection = profile, Database = "db" };
        _fieldPrefix = fields with { Prefix = "campo0001" };
        _fieldHumps = fields with { Prefix = "cn" };
        _nestedFields = fields with { ParentPath = "Cliente" };
        _operators = new(SymbolKinds.QueryOperator, EditorDialects.Console, "e");
        // The first field query merges evidence once; keystrokes reuse the merged schema.
        _catalog.Query(_fieldPrefix);
    }

    [GlobalCleanup]
    public void Cleanup() => _cache.Dispose();

    [Benchmark]
    public int CollectionPrefix() => _catalog.Query(_collections).Candidates.Count;

    [Benchmark]
    public int FieldPrefix() => _catalog.Query(_fieldPrefix).Candidates.Count;

    [Benchmark]
    public int FieldCamelHumps() => _catalog.Query(_fieldHumps).Candidates.Count;

    [Benchmark]
    public int NestedFields() => _catalog.Query(_nestedFields).Candidates.Count;

    [Benchmark]
    public int OperatorPrefix() => _catalog.Query(_operators).Candidates.Count;
}

/// <summary>Cost of building a scope table and of reading the cache on the keystroke path.</summary>
[MemoryDiagnoser]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "BenchmarkDotNet owns the lifecycle and releases the cache in GlobalCleanup.")]
public class MetadataStructureBenchmarks
{
    private string[] _names = [];
    private MetadataCache _cache = null!;
    private ConnectionProfile _profile = null!;
    private ConnectionIdentity _identity = null!;

    [Params(100, 1_000, 10_000)]
    public int Names { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _names = Enumerable.Range(0, Names).Select(SyntheticWorkload.FieldName).ToArray();
        _profile = ConnectionProfile.Create("bench", "mongodb://user:secret@bench-a:27017,bench-b:27017/db?replicaSet=rs&authSource=admin");
        _identity = ConnectionIdentity.From(_profile);
        _cache = new MetadataCache(new SyntheticMetadataSource(Names, 10));
        _cache.PutCollections(_profile, "db", _names);
    }

    [GlobalCleanup]
    public void Cleanup() => _cache.Dispose();

    [Benchmark]
    public int BuildNameTable() => new NameTable<string>(_names, name => name).Count;

    [Benchmark]
    public int PeekCollections() => _cache.GetCollections(_identity, "db", MetadataAccess.Peek).Value!.Count;

    [Benchmark]
    public int ConnectionIdentityHash() => ConnectionIdentity.From(_profile).Fingerprint.Length;
}
