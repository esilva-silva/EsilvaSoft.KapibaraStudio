namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core;

/// <summary>A metadata change caused by the IDE. Strong removes cached values; soft only marks them stale.</summary>
public sealed record MetadataInvalidation(Guid ProfileId, MetadataChange Change, string Database = "", string Collection = "",
    InvalidationStrength Strength = InvalidationStrength.Strong);
