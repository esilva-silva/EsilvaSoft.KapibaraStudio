namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core.Context;

/// <summary>
/// A single aggregation stage whose name, property names and scalar literals have already been decoded by the caller.
/// This contract intentionally does not accept JavaScript or JSON source text.
/// </summary>
public sealed class PipelineStage
{
    public PipelineStage(string name, params PipelineStageProperty[] properties)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(properties);
        if (properties.Any(property => property is null)) throw new ArgumentException("Stage properties cannot contain null.", nameof(properties));
        Name = name;
        Properties = Array.AsReadOnly(properties.ToArray());
    }

    public string Name { get; }
    public IReadOnlyList<PipelineStageProperty> Properties { get; }

    public static PipelineStage Unset(params string[] names) => new("$unset", new PipelineStageProperty("$unset", PipelineStageValue.LiteralNames(names)));
    public static PipelineStage Count(string fieldName) => new("$count", new PipelineStageProperty("$count", PipelineStageValue.Literal(fieldName)));
}
