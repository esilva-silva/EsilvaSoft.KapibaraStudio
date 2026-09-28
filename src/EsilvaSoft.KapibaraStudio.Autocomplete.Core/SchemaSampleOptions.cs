namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core;

public sealed record SchemaSampleOptions(int Size = 100, int Depth = 4, int MaxTimeMs = 2000)
{
    public SchemaSampleOptions Validate()
    {
        if (Size is < 1 or > 1000 || Depth is < 1 or > 8 || MaxTimeMs is < 1 or > 60000)
            throw new ArgumentException("Amostra de schema: 1–1000 documentos, profundidade 1–8 e tempo máximo de 1–60000 ms.");
        return this;
    }
}
