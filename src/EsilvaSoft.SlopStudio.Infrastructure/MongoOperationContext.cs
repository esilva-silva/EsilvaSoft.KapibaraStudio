using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Core;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace EsilvaSoft.SlopStudio.Infrastructure;

/// <summary>
/// Contexto de uma única operação Mongo: ambiente resolvido (ENV/vault/segredos), cliente e utilitários de
/// parsing de JSON/BSON que dependem de valores dinâmicos do ambiente da conexão. Preparado uma vez por
/// chamada pública de <see cref="MongoWorkspaceService"/> e repassado aos executores especializados.
/// </summary>
internal sealed class MongoOperationContext
{
    private readonly ConnectionProfile _profile;
    private readonly OperationEnvironment _environment;
    private readonly MongoClientPool _clients;

    private MongoOperationContext(ConnectionProfile profile, OperationEnvironment environment, MongoClientPool clients)
    {
        _profile = profile;
        _environment = environment;
        _clients = clients;
    }

    public static async Task<MongoOperationContext> PrepareAsync(
        ConnectionProfile profile,
        IConnectionSecretStore secrets,
        IEnvironmentVaultRepository? environments,
        MongoClientPool clients,
        CancellationToken cancellationToken,
        ISecretStore? credentialStore = null)
    {
        var environment = new OperationEnvironment(environments, secrets, profile.Id, credentialStore);
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        await environment.PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        return new MongoOperationContext(profile, environment, clients);
    }

    public MongoClient CreateClient()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(_profile.ConnectionString);
        return _clients.Get(MongoClientSettings.FromConnectionString(_environment.ResolvedConnection));
    }

    public IMongoCollection<BsonDocument> GetCollection(string database, string collection)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);
        return CreateClient().GetDatabase(database).GetCollection<BsonDocument>(collection);
    }

    public BsonDocument ParseNonEmptyFilter(string filterJson)
    {
        var filter = ParseDocument(filterJson, "filtro");

        if (filter.ElementCount == 0)
        {
            throw new ArgumentException("Uma operação de alteração exige um filtro não vazio.", nameof(filterJson));
        }

        return filter;
    }

    public BsonDocument[] ParsePipeline(string pipelineJson)
    {
        try
        {
            var array = BsonSerializer.Deserialize<BsonArray>(ResolveDynamicJson(pipelineJson));

            if (array.Any(value => !value.IsBsonDocument))
            {
                throw new ArgumentException("Cada estágio do pipeline precisa ser um documento BSON.", nameof(pipelineJson));
            }

            return array.Select(value => value.AsBsonDocument).ToArray();
        }
        catch (FormatException exception)
        {
            throw new ArgumentException($"O pipeline não contém Extended JSON válido: {exception.Message}", nameof(pipelineJson), exception);
        }
    }

    public BsonDocument ParseDocument(string json, string component)
    {
        try
        {
            return BsonDocument.Parse(ResolveDynamicJson(string.IsNullOrWhiteSpace(json) ? "{}" : json));
        }
        catch (FormatException exception)
        {
            throw new ArgumentException($"O {component} não contém JSON BSON válido: {exception.Message}", component, exception);
        }
    }

    public BsonArray ParseArray(string json, string component)
    {
        try
        {
            return BsonSerializer.Deserialize<BsonArray>(ResolveDynamicJson(json));
        }
        catch (FormatException exception)
        {
            throw new ArgumentException($"O {component} não contém um array Extended JSON válido: {exception.Message}", nameof(json), exception);
        }
    }

    public BsonDocument? ParseOptionalDocument(string? json, string component) =>
        string.IsNullOrWhiteSpace(json) ? null : ParseDocument(json, component);

    public Collation? ParseOptionalCollation(string? json) =>
        string.IsNullOrWhiteSpace(json) ? null : Collation.FromBsonDocument(ParseDocument(json, "collation"));

    // UUID constructors become canonical binaries first; ENV values are then inserted as literal strings.
    private string ResolveDynamicJson(string json) => DynamicValues.ResolveJson(IdentifierRepresentationService.RewriteConstructors(json), _environment.Get);
}
