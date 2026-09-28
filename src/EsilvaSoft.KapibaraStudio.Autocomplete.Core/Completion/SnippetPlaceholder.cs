using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Text;

namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core.Completion;

/// <summary>A occurrence selected by the snippet editor after the template is expanded.</summary>
public sealed record SnippetPlaceholder(int Index, TextSpan Span, string? DefaultText, IReadOnlyList<string> Choices);
