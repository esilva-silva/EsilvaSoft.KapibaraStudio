namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core;

/// <summary>Peek never schedules a remote load; LoadIfNeeded may start a background refresh.</summary>
public enum MetadataAccess { LoadIfNeeded, Peek }
