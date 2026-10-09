using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EsilvaSoft.KapibaraStudio.Core;

/// <summary>Read-only server and collection state captured before a confirmed maintenance operation.</summary>
public sealed record CollectionMaintenancePreflight(
    string Database,
    string Collection,
    string TopologyJson,
    string DefinitionJson,
    string StatisticsJson,
    CollectionValidationInfo Validation,
    string Source,
    DateTimeOffset ObservedAt)
{
    public const string ExpectedSource = "hello;listCollections;collStats;getCollectionValidation";

    /// <summary>Fingerprint of target and observed state; observation time is intentionally excluded.</summary>
    public string Fingerprint => ComputeFingerprint(
        Database, Collection, TopologyJson, DefinitionJson, StatisticsJson, Validation, Source);

    public CollectionMaintenancePreflight Validate(string database, string collection)
    {
        if (string.IsNullOrWhiteSpace(Database) || string.IsNullOrWhiteSpace(Collection))
            throw new ArgumentException("A prévia precisa identificar banco e coleção.");
        if (!string.Equals(Database, database, StringComparison.Ordinal)
            || !string.Equals(Collection, collection, StringComparison.Ordinal))
            throw new ArgumentException("A prévia não corresponde ao alvo confirmado.");
        if (Collection.StartsWith("system.", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Coleções de sistema não podem receber manutenção pela interface.");
        if (!string.Equals(Source, ExpectedSource, StringComparison.Ordinal))
            throw new ArgumentException("A origem da prévia de manutenção não é reconhecida.");
        if (ObservedAt == default || ObservedAt.Offset != TimeSpan.Zero)
            throw new ArgumentException("A prévia precisa informar quando o estado foi observado em UTC.");
        if (Validation is null || !Enum.IsDefined(Validation.ValidationLevel) || !Enum.IsDefined(Validation.ValidationAction))
            throw new ArgumentException("A prévia contém configuração de validação inválida.");

        ValidateJsonObject(TopologyJson, "topologia");
        JsonDocument definition;
        try
        {
            definition = JsonDocument.Parse(DefinitionJson);
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("A prévia contém JSON inválido na definição da coleção.", exception);
        }
        using (definition)
        {
            if (!definition.RootElement.TryGetProperty("type", out var type)
                || type.ValueKind != JsonValueKind.String
                || !string.Equals(type.GetString(), "collection", StringComparison.Ordinal))
                throw new ArgumentException("A prévia não identifica uma coleção regular.");
        }
        ValidateJsonObject(StatisticsJson, "estatísticas");
        ValidateJsonObject(Validation.ValidatorJson, "validador atual");
        return this;
    }

    public bool MatchesObservedState(CollectionMaintenancePreflight? current) =>
        current is not null
        && string.Equals(Database, current.Database, StringComparison.Ordinal)
        && string.Equals(Collection, current.Collection, StringComparison.Ordinal)
        && string.Equals(Fingerprint, current.Fingerprint, StringComparison.Ordinal);

    public void EnsureMatchesObservedState(CollectionMaintenancePreflight? current)
    {
        if (!MatchesObservedState(current))
            throw new InvalidOperationException("O estado mudou desde a prévia obrigatória. Revise a coleção antes de repetir a operação.");
    }

    private static void ValidateJsonObject(string json, string label)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
                return;
        }
        catch (JsonException exception)
        {
            throw new ArgumentException($"A prévia contém JSON inválido em {label}.", nameof(json), exception);
        }

        throw new ArgumentException($"A prévia contém um documento inválido em {label}.", nameof(json));
    }

    private static string ComputeFingerprint(
        string database,
        string collection,
        string topology,
        string definition,
        string statistics,
        CollectionValidationInfo validation,
        string source)
    {
        var material = string.Join('\u001f', database, collection, NormalizeTopology(topology), definition, statistics,
            validation.ValidatorJson, validation.ValidationLevel.ToString(), validation.ValidationAction.ToString(), source);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }

    private static string NormalizeTopology(string topology)
    {
        // MongoDB's hello.localTime advances on every observation and does not describe topology.
        using var document = JsonDocument.Parse(topology);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (string.Equals(property.Name, "localTime", StringComparison.Ordinal))
                    continue;
                writer.WritePropertyName(property.Name);
                property.Value.WriteTo(writer);
            }
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
