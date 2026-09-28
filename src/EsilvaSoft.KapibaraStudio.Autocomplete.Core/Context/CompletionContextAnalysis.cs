using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Completion;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Text;

namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core.Context;

/// <summary>Result of cursor analysis plus the catalog request context consumed by the traditional provider.</summary>
public sealed record CompletionContextAnalysis(CompletionContext Context, CompletionCursorRole Role, TextSpan InsertSpan,
    char? Quote, bool ExistingProperty, NamespaceTarget Target);
