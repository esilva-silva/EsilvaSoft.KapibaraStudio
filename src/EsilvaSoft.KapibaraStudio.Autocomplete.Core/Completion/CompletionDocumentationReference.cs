namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core.Completion;

/// <summary>Small, value-free reference retained by an item; its display text is built only for the highlighted item.</summary>
public sealed record CompletionDocumentationReference(
    string? Category = null,
    string? ValueShape = null,
    IReadOnlyList<string>? Parameters = null,
    string? Returns = null,
    string? Since = null,
    EvidenceSources Evidence = EvidenceSources.None);
