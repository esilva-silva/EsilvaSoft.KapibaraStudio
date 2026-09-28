namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core.Completion;

[Flags]
public enum CompletionItemTags : byte { None = 0, Deprecated = 1, Write = 2, Stale = 4, Ai = 8 }
