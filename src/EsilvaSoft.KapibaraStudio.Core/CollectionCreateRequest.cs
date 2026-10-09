using System.Text.Json;

namespace EsilvaSoft.KapibaraStudio.Core;

/// <summary>Defines a user-created collection or view in an existing database.</summary>
public sealed record CollectionCreateRequest(
    string Database,
    string Collection,
    bool IsCapped = false,
    long? MaxSizeBytes = null,
    long? MaxDocuments = null,
    string? ViewOn = null,
    string? ViewPipelineJson = null,
    string? CollationJson = null,
    bool IsClustered = false,
    string? ClusteredIndexKeyJson = null)
{
    public CollectionCreateRequest Validate()
    {
        if (string.IsNullOrWhiteSpace(Database))
        {
            throw new ArgumentException("O banco de dados é obrigatório.", nameof(Database));
        }

        if (string.IsNullOrWhiteSpace(Collection))
        {
            throw new ArgumentException("O nome da coleção é obrigatório.", nameof(Collection));
        }

        if (Collection.StartsWith("system.", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Coleções do sistema não podem ser criadas pela interface.", nameof(Collection));
        }

        var isView = !string.IsNullOrWhiteSpace(ViewOn);
        if (isView)
        {
            if (string.Equals(Collection, ViewOn, StringComparison.Ordinal))
            {
                throw new ArgumentException("Uma view não pode usar a si mesma como origem.", nameof(ViewOn));
            }

            if (ViewOn!.StartsWith("system.", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("A view não pode usar uma coleção de sistema como origem.", nameof(ViewOn));
            }

            if (IsCapped || MaxSizeBytes is not null || MaxDocuments is not null || IsClustered)
            {
                throw new ArgumentException("Uma view não aceita opções de coleção capped.", nameof(IsCapped));
            }

            if (string.IsNullOrWhiteSpace(ViewPipelineJson))
            {
                throw new ArgumentException("Uma view exige um pipeline JSON, mesmo que seja [].", nameof(ViewPipelineJson));
            }

            try
            {
                using var pipeline = JsonDocument.Parse(ViewPipelineJson);
                if (pipeline.RootElement.ValueKind != JsonValueKind.Array
                    || pipeline.RootElement.EnumerateArray().Any(stage => stage.ValueKind != JsonValueKind.Object))
                {
                    throw new ArgumentException("O pipeline da view precisa ser um array de documentos JSON.", nameof(ViewPipelineJson));
                }
            }
            catch (JsonException exception)
            {
                throw new ArgumentException("O pipeline da view não contém JSON válido.", nameof(ViewPipelineJson), exception);
            }
        }
        else if (!string.IsNullOrWhiteSpace(ViewPipelineJson))
        {
            throw new ArgumentException("O pipeline só pode ser informado ao criar uma view.", nameof(ViewPipelineJson));
        }

        if (!IsCapped && (MaxSizeBytes is not null || MaxDocuments is not null))
        {
            throw new ArgumentException("Limites de tamanho e documentos exigem uma coleção capped.", nameof(IsCapped));
        }

        ValidateClusteredIndex();

        if (IsCapped && MaxSizeBytes is not > 0)
        {
            throw new ArgumentException("Uma coleção capped exige tamanho máximo em bytes.", nameof(MaxSizeBytes));
        }

        if (MaxSizeBytes is < 1 or > 1099511627776)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxSizeBytes), "O tamanho máximo deve estar entre 1 byte e 1 TB.");
        }

        if (MaxDocuments is < 1 or > 1000000000)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxDocuments), "O máximo de documentos deve estar entre 1 e 1000000000.");
        }

        if (!string.IsNullOrWhiteSpace(CollationJson))
        {
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

        return this;
    }

    private void ValidateClusteredIndex()
    {
        if (!IsClustered)
        {
            if (!string.IsNullOrWhiteSpace(ClusteredIndexKeyJson))
            {
                throw new ArgumentException("A chave clustered só pode ser informada quando Clustered estiver marcado.", nameof(ClusteredIndexKeyJson));
            }

            return;
        }

        if (IsCapped || !string.IsNullOrWhiteSpace(ViewOn))
        {
            throw new ArgumentException("Uma coleção clustered não pode ser view nem capped.", nameof(IsClustered));
        }

        if (string.IsNullOrWhiteSpace(ClusteredIndexKeyJson))
        {
            throw new ArgumentException("Uma coleção clustered exige a chave do índice BSON.", nameof(ClusteredIndexKeyJson));
        }

        try
        {
            using var key = JsonDocument.Parse(ClusteredIndexKeyJson);
            if (key.RootElement.ValueKind != JsonValueKind.Object || key.RootElement.EnumerateObject().Count() != 1)
            {
                throw new ArgumentException("A chave clustered precisa ser um documento BSON com exatamente um campo.", nameof(ClusteredIndexKeyJson));
            }
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("A chave clustered não contém JSON válido.", nameof(ClusteredIndexKeyJson), exception);
        }
    }
}
