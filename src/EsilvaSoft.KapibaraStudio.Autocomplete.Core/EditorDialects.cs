namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core;

/// <summary>Editor surfaces. <see cref="Mql"/> symbols (operators, stages) are valid inside every dialect.</summary>
[Flags]
public enum EditorDialects
{
    None = 0,
    Console = 1,
    MongoshScript = 2,
    AggregationJson = 4,
    Mql = 8,
    Scripts = Console | MongoshScript,
    All = Console | MongoshScript | AggregationJson | Mql
}
