namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core;

/// <summary>Where the knowledge about a field comes from.</summary>
[Flags]
public enum EvidenceSources
{
    None = 0, Validator = 1, Index = 2, Results = 4, Sample = 8, History = 16,
    /// <summary>Campo derivado dos estágios de agregação anteriores ao caret, sem leitura de dados.</summary>
    Pipeline = 32,
    /// <summary>
    /// Campo aprendido de forma persistente pelo schema learning (Fase L). Por DEC-L-MERGE, este bit nunca entra
    /// em <c>SchemaBuilder.AddSchema</c>/<c>MergedSchema</c>: a fonte aprendida constrói seu próprio
    /// <see cref="CollectionSchema"/>, sempre com <c>SampleSize = 0</c>, para que nenhuma porcentagem aprendida
    /// some numeradores de duas populações sobre o denominador da amostra.
    /// </summary>
    Learned = 64
}
