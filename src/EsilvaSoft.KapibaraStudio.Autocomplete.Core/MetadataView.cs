namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core;

/// <summary>A cached value and its state. A stale value stays usable while it is refreshed.</summary>
public readonly record struct MetadataView<T>(T? Value, MetadataFreshness Freshness, DateTimeOffset? LoadedAt, bool IsRefreshing) where T : class;
