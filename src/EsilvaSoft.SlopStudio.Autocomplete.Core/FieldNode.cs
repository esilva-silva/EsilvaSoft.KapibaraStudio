using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Autocomplete.Core;

/// <summary>Name, types and structure of one field. Values of documents are never kept.</summary>
public sealed class FieldNode
{
    // Most fields are leaves: they share one empty table instead of allocating five arrays each.
    private static readonly NameTable<FieldNode> NoChildren = new([], child => child.Name);
    private readonly IReadOnlyList<FieldNode> _ordered;

    internal FieldNode(string name, string path, IReadOnlyDictionary<string, int> types, int occurrences, int sampleSize, FieldTraits flags,
        EvidenceSources evidence, IReadOnlyList<FieldNode> children, IReadOnlyList<string> enumLiterals, long sequence)
    {
        Name = name; Path = path; Types = types; Occurrences = occurrences; SampleSize = sampleSize; Flags = flags; Evidence = evidence;
        EnumLiterals = enumLiterals; Sequence = sequence; _ordered = children;
        Children = children.Count == 0 ? NoChildren : new NameTable<FieldNode>(children, child => child.Name);
    }

    public string Name { get; }
    public string Path { get; }
    /// <summary>Observed or declared BSON type names with how many times each was seen.</summary>
    public IReadOnlyDictionary<string, int> Types { get; }
    public FieldTraits Flags { get; }
    public EvidenceSources Evidence { get; }
    public NameTable<FieldNode> Children { get; }
    /// <summary>Only literals declared by a validator enum; never values read from documents.</summary>
    public IReadOnlyList<string> EnumLiterals { get; }
    /// <summary>Fraction of sampled documents containing the field; null without a sample.</summary>
    public double? Occurrence => SampleSize > 0 && Evidence.HasFlag(EvidenceSources.Sample) ? Math.Min(1, (double)Occurrences / SampleSize) : null;
    /// <summary>Number of sampled documents containing this field. Zero for evidence without a sample.</summary>
    public int ObservedCount => Evidence.HasFlag(EvidenceSources.Sample) ? Occurrences : 0;
    public string PrimaryType => Types.Count == 0 ? "unknown"
        : Types.OrderByDescending(type => type.Value).ThenBy(type => type.Key, StringComparer.Ordinal).First().Key;

    internal int Occurrences { get; }
    internal int SampleSize { get; }
    internal long Sequence { get; }
    internal IReadOnlyList<FieldNode> OrderedChildren => _ordered;

    public FieldNode? Find(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.Length == 0) return this;
        var node = this;
        foreach (var segment in path.Split('.'))
            if (!node.Children.TryGetExact(segment, out node)) return null;
        return node;
    }
}
