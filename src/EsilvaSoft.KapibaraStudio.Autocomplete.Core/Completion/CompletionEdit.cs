using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Text;

namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core.Completion;

public sealed record CompletionEdit(TextSpan InsertRange, TextSpan ReplaceRange, string NewText, bool IsSnippet = false);
