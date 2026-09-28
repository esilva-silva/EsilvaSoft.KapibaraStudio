namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core;

/// <summary>Connection, database and collection that scope a metadata symbol.</summary>
public sealed record CatalogScope(ConnectionIdentity Connection, string Database = "", string Collection = "");
