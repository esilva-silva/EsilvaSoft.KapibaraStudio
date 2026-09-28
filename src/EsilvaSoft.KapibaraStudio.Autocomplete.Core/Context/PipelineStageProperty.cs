namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core.Context;

/// <summary>One literal property name and its tokenized value in a stage document.</summary>
public sealed record PipelineStageProperty
{
    public PipelineStageProperty(string name, PipelineStageValue value)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(value);
        Name = name;
        Value = value;
    }

    public string Name { get; }
    public PipelineStageValue Value { get; }
}
