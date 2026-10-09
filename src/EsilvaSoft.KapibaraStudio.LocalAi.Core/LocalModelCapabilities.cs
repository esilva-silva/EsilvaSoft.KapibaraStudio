namespace EsilvaSoft.KapibaraStudio.LocalAi.Core;

[Flags]
public enum LocalModelCapabilities
{
    None = 0, Autocomplete = 1, Chat = 2, Fim = 4, Embeddings = 8,
    Agent = 16, Tools = 32, Reasoning = 64
}
