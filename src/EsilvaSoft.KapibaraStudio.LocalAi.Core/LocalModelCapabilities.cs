namespace EsilvaSoft.KapibaraStudio.LocalAi.Core;

[Flags]
public enum LocalModelCapabilities { None = 0, Autocomplete = 1, Chat = 2, Fim = 4, Embeddings = 8 }
