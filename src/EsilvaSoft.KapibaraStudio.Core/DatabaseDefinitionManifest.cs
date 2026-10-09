using System.Text.Json;

namespace EsilvaSoft.KapibaraStudio.Core;

/// <summary>
/// Portable, optional database definitions carried separately from collection documents.
/// This contract is additive; older logical-export manifest readers may ignore it.
/// </summary>
public sealed record DatabaseDefinitionManifest(
    int SchemaVersion,
    IReadOnlyList<CollectionDefinitionSnapshot> Collections,
    IReadOnlyList<ViewDefinitionSnapshot> Views)
{
    public const int CurrentSchemaVersion = 1;

    public DatabaseDefinitionManifest Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion)
        {
            throw new ArgumentOutOfRangeException(nameof(SchemaVersion), "A versão das definições do banco não é suportada.");
        }

        ArgumentNullException.ThrowIfNull(Collections);
        ArgumentNullException.ThrowIfNull(Views);

        var collectionNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var collection in Collections)
        {
            collection.Validate();
            if (!collectionNames.Add(collection.Name))
            {
                throw new ArgumentException("O pacote contém definições duplicadas para uma coleção.", nameof(Collections));
            }
        }

        var viewNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var view in Views)
        {
            view.Validate();
            if (!viewNames.Add(view.Name) || collectionNames.Contains(view.Name))
            {
                throw new ArgumentException("O pacote contém nomes de view duplicados ou em colisão com coleções.", nameof(Views));
            }
        }

        var knownSources = new HashSet<string>(collectionNames, StringComparer.Ordinal);
        knownSources.UnionWith(viewNames);
        foreach (var view in Views)
        {
            if (!knownSources.Contains(view.ViewOn))
            {
                throw new ArgumentException("Uma view depende de uma coleção ou view ausente no pacote.", nameof(Views));
            }
        }

        EnsureAcyclicViews(Views);
        return this;
    }

    private static void EnsureAcyclicViews(IReadOnlyList<ViewDefinitionSnapshot> views)
    {
        var viewNames = views.Select(view => view.Name).ToHashSet(StringComparer.Ordinal);
        var byName = views.ToDictionary(view => view.Name, StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var active = new HashSet<string>(StringComparer.Ordinal);

        foreach (var name in viewNames)
        {
            Visit(name);
        }

        void Visit(string name)
        {
            if (visited.Contains(name))
            {
                return;
            }

            if (!active.Add(name))
            {
                throw new ArgumentException("O pacote contém dependências cíclicas entre views.", nameof(views));
            }

            var source = byName[name].ViewOn;
            if (viewNames.Contains(source))
            {
                Visit(source);
            }

            active.Remove(name);
            visited.Add(name);
        }
    }
}

/// <summary>Portable options, validator and indexes for one collection.</summary>
public sealed record CollectionDefinitionSnapshot(
    string Name,
    string OptionsJson,
    CollectionValidationInfo? Validation,
    IReadOnlyList<IndexDefinitionSnapshot> Indexes)
{
    public CollectionDefinitionSnapshot Validate()
    {
        ValidateName(Name, nameof(Name));
        ValidateJsonObject(OptionsJson, nameof(OptionsJson));
        ArgumentNullException.ThrowIfNull(Indexes);
        Validation?.Validate();

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var index in Indexes)
        {
            index.Validate();
            if (!names.Add(index.Name))
            {
                throw new ArgumentException("A definição contém nomes de índice duplicados.", nameof(Indexes));
            }
        }

        return this;
    }

    internal static void ValidateName(string? name, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(name) || name.StartsWith("system.", StringComparison.OrdinalIgnoreCase) ||
            name.Contains('.') || name.Contains('$') ||
            name.Contains('\0'))
        {
            throw new ArgumentException("O nome da definição está vazio ou pertence a um namespace protegido.", parameterName);
        }
    }

    internal static void ValidateJsonObject(string? json, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ArgumentException("A definição JSON é obrigatória.", parameterName);
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException("A definição precisa ser um documento JSON.", parameterName);
            }
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("A definição não contém JSON válido.", parameterName, exception);
        }
    }
}

/// <summary>Index key pattern and portable options in Extended JSON form.</summary>
public sealed record IndexDefinitionSnapshot(string Name, string KeysJson, string OptionsJson)
{
    public IndexDefinitionSnapshot Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Contains('\0'))
        {
            throw new ArgumentException("O nome do índice está vazio ou contém caractere inválido.", nameof(Name));
        }

        CollectionDefinitionSnapshot.ValidateJsonObject(KeysJson, nameof(KeysJson));
        CollectionDefinitionSnapshot.ValidateJsonObject(OptionsJson, nameof(OptionsJson));

        using var keys = JsonDocument.Parse(KeysJson);
        if (!keys.RootElement.EnumerateObject().Any())
        {
            throw new ArgumentException("As chaves do índice não podem estar vazias.", nameof(KeysJson));
        }

        return this;
    }
}

/// <summary>A MongoDB view definition, excluding its source collection's documents.</summary>
public sealed record ViewDefinitionSnapshot(
    string Name,
    string ViewOn,
    string PipelineJson,
    string? CollationJson)
{
    public ViewDefinitionSnapshot Validate()
    {
        CollectionDefinitionSnapshot.ValidateName(Name, nameof(Name));
        CollectionDefinitionSnapshot.ValidateName(ViewOn, nameof(ViewOn));

        try
        {
            using var pipeline = JsonDocument.Parse(PipelineJson);
            if (pipeline.RootElement.ValueKind != JsonValueKind.Array ||
                pipeline.RootElement.EnumerateArray().Any(stage => stage.ValueKind != JsonValueKind.Object))
            {
                throw new ArgumentException("O pipeline da view precisa ser um array de documentos JSON.", nameof(PipelineJson));
            }
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("O pipeline da view não contém JSON válido.", nameof(PipelineJson), exception);
        }

        if (CollationJson is not null)
        {
            CollectionDefinitionSnapshot.ValidateJsonObject(CollationJson, nameof(CollationJson));
        }

        return this;
    }
}

internal static class CollectionValidationInfoValidation
{
    public static void Validate(this CollectionValidationInfo validation)
    {
        CollectionDefinitionSnapshot.ValidateJsonObject(validation.ValidatorJson, nameof(validation.ValidatorJson));
        if (!Enum.IsDefined(validation.ValidationLevel) || !Enum.IsDefined(validation.ValidationAction))
        {
            throw new ArgumentException("O nível ou a ação de validação não são suportados.", nameof(validation));
        }
    }
}
