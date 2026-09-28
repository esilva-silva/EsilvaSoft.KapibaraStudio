namespace EsilvaSoft.KapibaraStudio.Core;

/// <summary>Represents an explicitly saved query and its execution options.</summary>
public sealed record SavedQuery(
    Guid Id,
    string Name,
    Guid? ProfileId,
    string Database,
    string Collection,
    string FilterJson,
    string? ProjectionJson,
    string? SortJson,
    string? HintJson,
    int Limit,
    int Skip,
    int? MaxTimeMs,
    bool IsFavorite,
    DateTimeOffset UpdatedAt,
    string? Comment = null,
    int? BatchSize = null,
    string? CollationJson = null)
{
    public string DisplayText => $"{(IsFavorite ? "★ " : string.Empty)}{Name} · {Database}.{Collection}";

    public SavedQuery Validate()
    {
        if (Id == Guid.Empty)
        {
            throw new ArgumentException("O identificador da consulta salva é obrigatório.", nameof(Id));
        }

        if (string.IsNullOrWhiteSpace(Name) || Name.Trim().Length > 120)
        {
            throw new ArgumentException("O nome da consulta deve ter entre 1 e 120 caracteres.", nameof(Name));
        }

        _ = new MongoQuery(Database, Collection, FilterJson, ProjectionJson, SortJson, Limit, Skip, HintJson, MaxTimeMs, Comment, BatchSize, CollationJson).Validate();
        if (UpdatedAt == default)
        {
            throw new ArgumentException("A data de atualização é obrigatória.", nameof(UpdatedAt));
        }

        return this;
    }

    public MongoQuery ToQuery() => new(Database, Collection, FilterJson, ProjectionJson, SortJson, Limit, Skip, HintJson, MaxTimeMs, Comment, BatchSize, CollationJson);

    public static SavedQuery Create(string name, Guid? profileId, MongoQuery query, bool isFavorite = false, DateTimeOffset? updatedAt = null) =>
        new SavedQuery(Guid.NewGuid(), name.Trim(), profileId, query.Database, query.Collection, query.FilterJson, query.ProjectionJson, query.SortJson, query.HintJson, query.Limit, query.Skip, query.MaxTimeMs, isFavorite, updatedAt ?? DateTimeOffset.UtcNow, query.Comment, query.BatchSize, query.CollationJson).Validate();
}
