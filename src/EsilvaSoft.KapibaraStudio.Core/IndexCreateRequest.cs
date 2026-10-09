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

        ValidateKeyCompatibility();

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
            if (keys.RootElement.ValueKind != JsonValueKind.Object || !keys.RootElement.EnumerateObject().Any(field =>
                    string.Equals(field.Name, "$**", StringComparison.Ordinal)
                    || field.Name.EndsWith(".$**", StringComparison.Ordinal)))
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

    private void ValidateKeyCompatibility()
    {
        JsonDocument keys;
        try
        {
            keys = JsonDocument.Parse(KeysJson);
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("As chaves do índice não contêm JSON válido.", nameof(KeysJson), exception);
        }

        using (keys)
        {
            if (keys.RootElement.ValueKind != JsonValueKind.Object || !keys.RootElement.EnumerateObject().Any())
            {
                throw new ArgumentException("As chaves do índice precisam ser um documento não vazio.", nameof(KeysJson));
            }

            var fields = keys.RootElement.EnumerateObject().ToArray();
            if (fields.Select(field => field.Name).Distinct(StringComparer.Ordinal).Count() != fields.Length)
            {
                throw new ArgumentException("As chaves do índice não podem repetir campos.", nameof(KeysJson));
            }

            var types = fields.Select(field => (field.Name, Type: ReadKeyType(field.Value))).ToArray();
            if (types.Any(field => field.Type == "invalid"))
            {
                throw new ArgumentException("Cada direção de índice precisa ser 1, -1, hashed, text, 2d ou 2dsphere.", nameof(KeysJson));
            }

            var textPositions = types.Select((field, index) => (field.Type, Index: index))
                .Where(field => field.Type == "text")
                .Select(field => field.Index)
                .ToArray();
            var textKeys = textPositions.Length;
            var wildcardKeys = types.Count(field => field.Name == "$**" || field.Name.EndsWith(".$**", StringComparison.Ordinal));

            // A single text index may contain several text fields (MongoDB supports up to 32).
            // The one-text-index-per-collection constraint depends on existing server metadata and remains enforced by MongoDB.
            if (textKeys > 32)
            {
                throw new ArgumentException("Um índice de texto aceita no máximo 32 campos de texto.", nameof(KeysJson));
            }

            if (textKeys > 1 && textPositions[^1] - textPositions[0] + 1 != textKeys)
            {
                throw new ArgumentException("As chaves de texto precisam ficar adjacentes na definição do índice composto.", nameof(KeysJson));
            }

            if (wildcardKeys > 1)
            {
                throw new ArgumentException("Um índice wildcard aceita somente uma chave wildcard.", nameof(KeysJson));
            }

            if (IsUnique && types.Any(field => field.Type == "hashed"))
            {
                throw new ArgumentException("Índices hashed não aceitam a opção unique.", nameof(IsUnique));
            }

            if (types.Count(field => field.Type == "hashed") > 1)
            {
                throw new ArgumentException("Um índice composto aceita somente uma chave hashed.", nameof(KeysJson));
            }

            if (wildcardKeys > 0 && (IsUnique || ExpireAfterSeconds is not null || textKeys > 0 ||
                                      types.Any(field => field.Type is "hashed" or "2d" or "2dsphere")))
            {
                throw new ArgumentException("Índices wildcard não podem combinar unique, TTL, texto ou outro tipo especial.", nameof(KeysJson));
            }

            if (textKeys > 0 && (IsUnique || ExpireAfterSeconds is not null || WildcardProjectionJson is not null ||
                                 types.Any(field => field.Type is "hashed" or "2d" or "2dsphere")))
            {
                throw new ArgumentException("Índices de texto não podem combinar unique, TTL, projeção wildcard ou outro tipo especial.", nameof(KeysJson));
            }

            var has2d = types.Any(field => field.Type == "2d");
            var has2dsphere = types.Any(field => field.Type == "2dsphere");
            if ((has2d || has2dsphere) && (ExpireAfterSeconds is not null || WildcardProjectionJson is not null || has2d && has2dsphere))
            {
                throw new ArgumentException("Índices geoespaciais não podem combinar TTL, projeção wildcard ou tipos 2d e 2dsphere no mesmo índice.", nameof(KeysJson));
            }

            if (has2d && (types[0].Type != "2d" || types.Length > 2))
            {
                throw new ArgumentException("Índice 2d composto exige a chave geoespacial primeiro e aceita no máximo mais uma chave.", nameof(KeysJson));
            }

            if (ExpireAfterSeconds is not null && (fields.Length != 1 || types[0].Type != "ascending"))
            {
                throw new ArgumentException("Índice TTL exige uma única chave ascendente; a combinação atual não é suportada.", nameof(ExpireAfterSeconds));
            }

            if (IsSparse && PartialFilterJson is not null)
            {
                throw new ArgumentException("As opções sparse e partial não podem ser usadas juntas.", nameof(PartialFilterJson));
            }
        }
    }

    private static string ReadKeyType(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            return value.GetString() switch
            {
                "hashed" => "hashed",
                "text" => "text",
                "2d" => "2d",
                "2dsphere" => "2dsphere",
                _ => "invalid"
            };
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var direction))
        {
            return direction == 1 ? "ascending" : direction == -1 ? "descending" : "invalid";
        }

        // Extended JSON integer values are accepted by BSON, but key directions must still be exactly 1 or -1.
        if (value.ValueKind == JsonValueKind.Object)
        {
            var properties = value.EnumerateObject().ToArray();
            if (properties.Length == 1 && properties[0].NameEquals("$numberInt") &&
                properties[0].Value.ValueKind == JsonValueKind.String &&
                int.TryParse(properties[0].Value.GetString(), System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out var extendedDirection))
            {
                return extendedDirection == 1 ? "ascending" : extendedDirection == -1 ? "descending" : "invalid";
            }
        }

        return "invalid";
    }
}
