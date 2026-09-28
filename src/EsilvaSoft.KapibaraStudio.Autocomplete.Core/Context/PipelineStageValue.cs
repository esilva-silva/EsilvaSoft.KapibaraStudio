namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core.Context;

/// <summary>One semantic value supplied by a pipeline-stage tokenizer.</summary>
public sealed record PipelineStageValue(PipelineValueKind Kind, string? Text = null, IReadOnlyList<string>? Names = null,
    IReadOnlyList<PipelineStage>? Stages = null, IReadOnlyDictionary<string, IReadOnlyList<PipelineStage>>? Branches = null)
{
    public static PipelineStageValue Include { get; } = new(PipelineValueKind.Include);
    public static PipelineStageValue Exclude { get; } = new(PipelineValueKind.Exclude);
    public static PipelineStageValue Expression { get; } = new(PipelineValueKind.Expression);
    public static PipelineStageValue Unknown { get; } = new(PipelineValueKind.Unknown);

    public static PipelineStageValue Literal(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        return new(PipelineValueKind.Literal, value);
    }

    public static PipelineStageValue FieldReference(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return new(PipelineValueKind.FieldReference, path);
    }

    public static PipelineStageValue Accumulator(string name, bool numeric = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return new(numeric ? PipelineValueKind.NumericAccumulator : PipelineValueKind.Accumulator, name);
    }

    public static PipelineStageValue LiteralNames(params string[] names)
    {
        ArgumentNullException.ThrowIfNull(names);
        if (names.Length == 0 || names.Any(string.IsNullOrEmpty)) throw new ArgumentException("At least one non-empty literal name is required.", nameof(names));
        return new(PipelineValueKind.Names, Names: Array.AsReadOnly(names.ToArray()));
    }

    public static PipelineStageValue Pipeline(IReadOnlyList<PipelineStage> stages) =>
        new(PipelineValueKind.Pipeline, Stages: stages);

    public static PipelineStageValue Facet(IReadOnlyDictionary<string, IReadOnlyList<PipelineStage>> branches) =>
        new(PipelineValueKind.FacetBranches, Branches: branches);
}
