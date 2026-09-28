using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core;

/// <summary>Immutable merge of every field evidence known for a collection.</summary>
public sealed class CollectionSchema
{
    internal CollectionSchema(FieldNode root, EvidenceSources evidence, int sampleSize, bool truncated, int nodeCount)
    {
        Root = root; Evidence = evidence; SampleSize = sampleSize; IsTruncated = truncated; NodeCount = nodeCount;
    }

    public static CollectionSchema Empty { get; } = new SchemaBuilder().Build();

    public FieldNode Root { get; }
    public EvidenceSources Evidence { get; }
    public int SampleSize { get; }
    public bool IsTruncated { get; }
    public int NodeCount { get; }

    public FieldNode? Find(string path) => Root.Find(path);

    /// <summary>Every field in the order it was first discovered.</summary>
    public IEnumerable<FieldNode> Descendants()
    {
        var nodes = new List<FieldNode>(NodeCount);
        var pending = new Stack<FieldNode>();
        pending.Push(Root);
        while (pending.TryPop(out var node))
            foreach (var child in node.OrderedChildren) { nodes.Add(child); pending.Push(child); }
        return nodes.OrderBy(node => node.Sequence);
    }

    public IEnumerable<string> Paths() => Descendants().Select(node => node.Path);

    /// <summary>
    /// Unions every field of the given schemas. Bounded by <paramref name="maximumDepth"/>/<paramref name="maximumNodes"/>
    /// after the union (not just within each source), so merging many local schemas or evidence sets never grows past
    /// the same ceiling a single source already respects; the excess is marked <see cref="IsTruncated"/>.
    /// </summary>
    public static CollectionSchema Merge(IEnumerable<CollectionSchema?> schemas, int maximumDepth = 12, int maximumNodes = 10_000)
    {
        ArgumentNullException.ThrowIfNull(schemas);
        var builder = new SchemaBuilder(maximumDepth, maximumNodes);
        foreach (var schema in schemas) if (schema is not null) builder.AddSchema(schema);
        return builder.Build();
    }
}
