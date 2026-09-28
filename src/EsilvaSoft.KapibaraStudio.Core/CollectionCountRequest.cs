using System.Text.Json;

namespace EsilvaSoft.KapibaraStudio.Core;

public sealed record CollectionCountRequest(
    string Database,
    string Collection,
    string FilterJson = "{}",
    bool UseEstimatedCount = false,
    int? MaxTimeMs = null)
{
    public CollectionCountRequest Validate()
    {
        if (string.IsNullOrWhiteSpace(Database))
        {
            throw new ArgumentException("O banco de dados é obrigatório.", nameof(Database));
        }

        if (string.IsNullOrWhiteSpace(Collection))
        {
            throw new ArgumentException("A coleção é obrigatória.", nameof(Collection));
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

            if (UseEstimatedCount && document.RootElement.EnumerateObject().Any())
            {
                throw new ArgumentException("A contagem estimada não aceita filtro.", nameof(FilterJson));
            }
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("O filtro deve conter JSON válido.", nameof(FilterJson), exception);
        }

        return this;
    }
}
