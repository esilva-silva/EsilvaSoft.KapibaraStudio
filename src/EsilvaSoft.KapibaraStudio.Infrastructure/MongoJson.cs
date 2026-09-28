using MongoDB.Bson.IO;

namespace EsilvaSoft.KapibaraStudio.Infrastructure;

/// <summary>Configuração canônica de Extended JSON compartilhada por todos os executores Mongo.</summary>
internal static class MongoJson
{
    public static readonly JsonWriterSettings CanonicalSettings = new()
    {
        OutputMode = JsonOutputMode.CanonicalExtendedJson,
        Indent = true
    };
}
