namespace EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

/// <summary>Expansion and document rows of one rendered result, kept while the same execution is re-rendered.</summary>
internal sealed class ResultTreeState
{
    public HashSet<string> Expanded { get; } = new(StringComparer.Ordinal);
    public Dictionary<ResultDocumentViewModel, ResultNodeViewModel> DocumentNodes { get; } = [];
    public bool Rendered { get; set; }
}
