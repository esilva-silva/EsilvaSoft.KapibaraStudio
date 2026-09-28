namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core.Context;

/// <summary>
/// The already-tokenized form of a value relevant to pipeline field inference. Text is a decoded literal, never source
/// code; parsing source text belongs to the tolerant parser and is deliberately not performed here.
/// </summary>
public enum PipelineValueKind : byte
{
    Unknown,
    Include,
    Exclude,
    Expression,
    FieldReference,
    Accumulator,
    NumericAccumulator,
    Literal,
    Names,
    Pipeline,
    FacetBranches
}
