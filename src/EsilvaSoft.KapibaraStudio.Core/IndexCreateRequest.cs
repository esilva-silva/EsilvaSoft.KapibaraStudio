using System.Text.Json;

namespace EsilvaSoft.KapibaraStudio.Core;

/// <summary>Defines a BSON index and its common server-side options.</summary>
public sealed record IndexCreateRequest(
    string Database,
    string Collection,
    string KeysJson,
    string? Name = null,
    bool IsUnique = false,
    bool IsSparse = false,
    int? ExpireAfterSeconds = null,
    string? PartialFilterJson = null,
    string? CollationJson = null,
    bool IsHidden = false,
    string? WildcardProjectionJson = null)
{
    public IndexCreateRequest Validate()
    {
        if (string.IsNullOrWhiteSpace(Database))
        {
            throw new ArgumentException("O banco de dados é obrigatório.", nameof(Database));
        }

        if (string.IsNullOrWhiteSpace(Collection))
        {
            throw new ArgumentException("A coleção é obrigatória.", nameof(Collection));
        }

        if (string.IsNullOrWhiteSpace(KeysJson))
        {
            throw new ArgumentException("As chaves do índice são obrigatórias.", nameof(KeysJson));
        }

        if (ExpireAfterSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ExpireAfterSeconds), "O TTL não pode ser negativo.");
        }

        if (PartialFilterJson is not null && string.IsNullOrWhiteSpace(PartialFilterJson))
        {
            throw new ArgumentException("O filtro parcial não pode estar vazio quando informado.", nameof(PartialFilterJson));
        }

        if (CollationJson is not null)
        {
            if (string.IsNullOrWhiteSpace(CollationJson))
            {
                throw new ArgumentException("A collation não pode estar vazia quando informada.", nameof(CollationJson));
            }

            try
            {
                using var collation = JsonDocument.Parse(CollationJson);
                if (collation.RootElement.ValueKind != JsonValueKind.Object)
                {
                    throw new ArgumentException("A collation precisa ser um documento JSON.", nameof(CollationJson));
                }
            }
            catch (JsonException exception)
            {
                throw new ArgumentException("A collation não contém JSON válido.", nameof(CollationJson), exception);
            }
        }

        ValidateWildcardProjection();

        return this;
    }

    private void ValidateWildcardProjection()
    {
        if (WildcardProjectionJson is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(WildcardProjectionJson))
        {
            throw new ArgumentException("A projeção wildcard não pode estar vazia quando informada.", nameof(WildcardProjectionJson));
        }

        try
        {
            using var keys = JsonDocument.Parse(KeysJson);
            if (keys.RootElement.ValueKind != JsonValueKind.Object || !keys.RootElement.TryGetProperty("$**", out _))
            {
                throw new ArgumentException("A projeção wildcard só pode ser usada quando as chaves incluem \"$**\".", nameof(WildcardProjectionJson));
            }

            using var projection = JsonDocument.Parse(WildcardProjectionJson);
            if (projection.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException("A projeção wildcard precisa ser um documento JSON.", nameof(WildcardProjectionJson));
            }

            bool? includesFields = null;
            foreach (var field in projection.RootElement.EnumerateObject())
            {
                if (!field.Value.TryGetInt32(out var value) || value is not 0 and not 1)
                {
                    throw new ArgumentException("Cada campo da projeção wildcard precisa ter valor 0 ou 1.", nameof(WildcardProjectionJson));
                }

                if (string.Equals(field.Name, "_id", StringComparison.Ordinal))
                {
                    continue;
                }

                var includes = value == 1;
                if (includesFields is not null && includesFields != includes)
                {
                    throw new ArgumentException("A projeção wildcard não pode misturar inclusões e exclusões, exceto para _id.", nameof(WildcardProjectionJson));
                }

                includesFields = includes;
            }
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("A projeção wildcard não contém JSON válido.", nameof(WildcardProjectionJson), exception);
        }
    }
}
