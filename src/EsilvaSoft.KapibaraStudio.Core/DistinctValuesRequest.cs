using System.Text.Json;

namespace EsilvaSoft.KapibaraStudio.Core;

public sealed record DistinctValuesRequest(
    string Database,
    string Collection,
    string Field,
    string FilterJson = "{}",
    int MaximumValues = 1_000,
    int? MaxTimeMs = null)
{
    public DistinctValuesRequest Validate()
    {
        if (string.IsNullOrWhiteSpace(Database))
        {
            throw new ArgumentException("O banco de dados é obrigatório.", nameof(Database));
        }

        if (string.IsNullOrWhiteSpace(Collection))
        {
            throw new ArgumentException("A coleção é obrigatória.", nameof(Collection));
        }

        if (string.IsNullOrWhiteSpace(Field) || Field.Length > 256 || Field.Split('.').Any(segment => string.IsNullOrWhiteSpace(segment) || segment.StartsWith('$')))
        {
            throw new ArgumentException("O campo deve ser um caminho BSON válido e não pode começar com $.", nameof(Field));
        }

        if (MaximumValues is < 1 or > 10_000)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumValues), "O limite deve estar entre 1 e 10000 valores.");
        }

        if (MaxTimeMs is < 1 or > 600_000)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxTimeMs), "O maxTimeMS deve estar entre 1 e 600000 milissegundos.");
        }

        try
        {
            using var document = JsonDocument.Parse(FilterJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException("O filtro deve ser um objeto JSON.", nameof(FilterJson));
            }
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("O filtro deve conter JSON válido.", nameof(FilterJson), exception);
        }

        return this;
    }
}
