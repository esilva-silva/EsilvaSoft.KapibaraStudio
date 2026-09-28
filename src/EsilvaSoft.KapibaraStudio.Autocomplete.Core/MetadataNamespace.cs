namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core;

public sealed record MetadataNamespace(Guid ProfileId, string Database, string Collection = "", string Index = "");
