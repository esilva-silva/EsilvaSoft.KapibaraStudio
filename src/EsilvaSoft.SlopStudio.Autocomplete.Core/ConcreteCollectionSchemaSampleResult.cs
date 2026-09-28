namespace EsilvaSoft.SlopStudio.Autocomplete.Core;

/// <summary>Result of a same-client collection-kind check followed by bounded schema sampling.</summary>
public sealed record ConcreteCollectionSchemaSampleResult(
    ConcreteCollectionSchemaSampleStatus Status,
    IReadOnlyList<SampledDocument> Items);

public enum ConcreteCollectionSchemaSampleStatus
{
    Sampled = 0,
    TargetNotVerifiable = 1,
    LimitExceeded = 2
}
