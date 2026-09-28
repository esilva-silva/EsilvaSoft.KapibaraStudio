namespace EsilvaSoft.KapibaraStudio.Core;

/// <summary>How completely a result represents stored documents; only complete collection reads allow an editable copy.</summary>
public enum ResultCompleteness { Complete, PartialProjection, Derived, Unknown }
