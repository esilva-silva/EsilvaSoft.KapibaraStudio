namespace EsilvaSoft.KapibaraStudio.Core;

public sealed record MongoQuery(
    string Database,
    string Collection,
    string FilterJson = "{}",
    string? ProjectionJson = null,
    string? SortJson = null,
    int Limit = 100,
    int Skip = 0,
    string? HintJson = null,
    int? MaxTimeMs = null,
    string? Comment = null,
    int? BatchSize = null,
    string? CollationJson = null)
{
    public MongoQuery Validate()
    {
        if (string.IsNullOrWhiteSpace(Database))
        {
            throw new ArgumentException("O banco de dados é obrigatório.", nameof(Database));
        }

        if (string.IsNullOrWhiteSpace(Collection))
        {
            throw new ArgumentException("A coleção é obrigatória.", nameof(Collection));
        }

        if (Limit is < 1 or > 1_000)
        {
            throw new ArgumentOutOfRangeException(nameof(Limit), "O limite deve estar entre 1 e 1000.");
        }

        if (Skip is < 0 or > 1_000_000)
        {
            throw new ArgumentOutOfRangeException(nameof(Skip), "O skip deve estar entre 0 e 1000000.");
        }

        if (MaxTimeMs is < 1 or > 600_000)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxTimeMs), "O maxTimeMS deve estar entre 1 e 600000 milissegundos.");
        }
        if (Comment is { Length: > 512 })
        {
            throw new ArgumentException("O comentário deve ter no máximo 512 caracteres.", nameof(Comment));
        }
        if (BatchSize is < 1 or > 10_000)
        {
            throw new ArgumentOutOfRangeException(nameof(BatchSize), "O batchSize deve estar entre 1 e 10000.");
        }
        if (!string.IsNullOrWhiteSpace(CollationJson))
        {
            try
            {
                using var collation = System.Text.Json.JsonDocument.Parse(CollationJson);
                if (collation.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
                {
                    throw new ArgumentException("A collation precisa ser um documento JSON.", nameof(CollationJson));
                }
            }
            catch (System.Text.Json.JsonException exception)
            {
                throw new ArgumentException("A collation não contém JSON válido.", nameof(CollationJson), exception);
            }
        }

        return this;
    }
}
