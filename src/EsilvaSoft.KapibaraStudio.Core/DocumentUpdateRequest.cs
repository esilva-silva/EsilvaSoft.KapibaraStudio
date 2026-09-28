using System.Text.Json;

namespace EsilvaSoft.KapibaraStudio.Core;

/// <summary>Defines a filtered partial update using MongoDB update operators.</summary>
public sealed record DocumentUpdateRequest(
    string Database,
    string Collection,
    string FilterJson,
    string UpdateJson,
    bool Upsert = false,
    string? ArrayFiltersJson = null)
{
    public DocumentUpdateRequest Validate()
    {
        if (string.IsNullOrWhiteSpace(Database))
        {
            throw new ArgumentException("O banco de dados é obrigatório.", nameof(Database));
        }

        if (string.IsNullOrWhiteSpace(Collection))
        {
            throw new ArgumentException("A coleção é obrigatória.", nameof(Collection));
        }

        if (string.IsNullOrWhiteSpace(FilterJson) || string.Equals(FilterJson.Trim(), "{}", StringComparison.Ordinal))
        {
            throw new ArgumentException("A atualização exige um filtro não vazio.", nameof(FilterJson));
        }

        if (string.IsNullOrWhiteSpace(UpdateJson))
        {
            throw new ArgumentException("O documento de atualização é obrigatório.", nameof(UpdateJson));
        }

        try
        {
            using var update = JsonDocument.Parse(UpdateJson);
            if (update.RootElement.ValueKind == JsonValueKind.Array
                && update.RootElement.EnumerateArray().Any(stage => stage.ValueKind != JsonValueKind.Object))
            {
                throw new ArgumentException("O pipeline de atualização precisa ser um array de documentos JSON.", nameof(UpdateJson));
            }

            if (update.RootElement.ValueKind is not JsonValueKind.Object and not JsonValueKind.Array)
            {
                throw new ArgumentException("A atualização precisa ser um documento de operadores ou um pipeline JSON.", nameof(UpdateJson));
            }
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("A atualização não contém JSON válido.", nameof(UpdateJson), exception);
        }

        if (!string.IsNullOrWhiteSpace(ArrayFiltersJson))
        {
            try
            {
                using var arrayFilters = JsonDocument.Parse(ArrayFiltersJson);
                if (arrayFilters.RootElement.ValueKind != JsonValueKind.Array
                    || arrayFilters.RootElement.EnumerateArray().Any(filter => filter.ValueKind != JsonValueKind.Object))
                {
                    throw new ArgumentException("Os filtros de array precisam ser um array de documentos JSON.", nameof(ArrayFiltersJson));
                }
            }
            catch (JsonException exception)
            {
                throw new ArgumentException("Os filtros de array não contêm JSON válido.", nameof(ArrayFiltersJson), exception);
            }
        }

        return this;
    }
}
