using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core;
using MongoDB.Bson;
using MongoDB.Driver;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Core;

internal static partial class KapiSimFixture
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };
    [GeneratedRegex("^kapi_sim_[a-z][a-z0-9_]{0,47}$", RegexOptions.CultureInvariant)]
    private static partial Regex DatabasePattern();
    internal static void RequireDatabase(string database)
    {
        if (!DatabasePattern().IsMatch(database)) throw new InvalidDataException("Somente bancos kapi_sim_* são permitidos.");
    }
    internal static string RequireUri(string uri)
    {
        // No credentials, SRV, DNS, replica discovery, environment expansion or arbitrary driver options.
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed) || parsed.Scheme != "mongodb" ||
            !string.IsNullOrEmpty(parsed.UserInfo) || !IPAddress.TryParse(parsed.Host.Trim('[', ']'), out var address) ||
            !IPAddress.IsLoopback(address) || parsed.AbsolutePath is not ("" or "/") ||
            parsed.Query is not ("" or "?directConnection=true") || parsed.Fragment.Length != 0 || parsed.Port is < 1 or > 65535)
            throw new InvalidDataException("Fixture exige mongodb:// com IP loopback, porta explícita e sem credenciais/opções externas.");
        return $"mongodb://{(address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? "[" + address + "]" : address.ToString())}:{parsed.Port}/?directConnection=true";
    }
    internal static T Read<T>(string path)
    {
        var bytes = LabWorkspace.ReadFileLimited(path, 4 * 1024 * 1024);
        JsonContractValidation.RequireUniqueProperties(bytes);
        return JsonSerializer.Deserialize<T>(bytes, JsonOptions) ?? throw new InvalidDataException("Fixture vazia.");
    }
    internal static Domain[] ReadDomains(string root, IEnumerable<string> paths) => paths.Select(path =>
    {
        var domain = Read<Domain>(Cli.KapiLabCommandLine.ReadArtifactPath(path, root));
        RequireDatabase(domain.Database);
        if (domain.Schema != "kapilab-kapi-sim-domain-v1" || domain.Collections is not { Length: > 0 and <= 10 } ||
            domain.Collections.Select(collection => collection.Name).Distinct(StringComparer.Ordinal).Count() != domain.Collections.Length)
            throw new InvalidDataException("Domínio sintético inválido.");
        foreach (var collection in domain.Collections)
        {
            if (!Regex.IsMatch(collection.Name, "^[a-z][a-z0-9_]{0,47}$", RegexOptions.CultureInvariant) ||
                collection.Documents is not { Length: > 0 and <= 1000 } || collection.Indexes is not { Length: <= 20 })
                throw new InvalidDataException("Coleção sintética inválida.");
            foreach (var doc in collection.Documents)
                if (doc.ValueKind != JsonValueKind.Object || !doc.TryGetProperty("_id", out _))
                    throw new InvalidDataException("Documentos sintéticos exigem _id determinístico.");
            foreach (var index in collection.Indexes)
                if (index.Keys.ValueKind != JsonValueKind.Object || index.Keys.EnumerateObject().Any(key =>
                    key.Value.ValueKind != JsonValueKind.Number || !key.Value.TryGetInt32(out var direction) || direction is not (1 or -1)))
                    throw new InvalidDataException("Fixture aceita somente índices ordenados comuns.");
        }
        return domain;
    }).ToArray();

    internal static async Task<string> SeedAsync(string uri, Domain[] domains, CancellationToken cancellationToken)
    {
        uri = RequireUri(uri);
        // Validate the whole batch before the first drop. Namespace names are never supplied to a shell.
        if (domains.Length is < 1 or > 6 || domains.Select(domain => domain.Database).Distinct().Count() != domains.Length)
            throw new InvalidDataException("Informe de 1 a 6 domínios distintos.");
        foreach (var domain in domains) RequireDatabase(domain.Database);
        var settings = MongoClientSettings.FromConnectionString(uri);
        settings.ServerSelectionTimeout = TimeSpan.FromSeconds(5);
        using var client = new MongoClient(settings);
        foreach (var domain in domains)
        {
            await client.DropDatabaseAsync(domain.Database, cancellationToken);
            var database = client.GetDatabase(domain.Database);
            foreach (var collection in domain.Collections)
            {
                var target = database.GetCollection<BsonDocument>(collection.Name);
                await target.InsertManyAsync(collection.Documents.Select(doc => BsonDocument.Parse(doc.GetRawText())), cancellationToken: cancellationToken);
                foreach (var index in collection.Indexes)
                    await target.Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(BsonDocument.Parse(index.Keys.GetRawText()),
                        new CreateIndexOptions { Name = index.Name, Unique = index.Unique }), cancellationToken: cancellationToken);
            }
        }
        return Hash(JsonSerializer.Serialize(domains, JsonOptions));
    }
    internal static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    internal sealed record Domain(string Schema, string Database, Collection[] Collections);
    internal sealed record Collection(string Name, JsonElement[] Documents, Index[] Indexes);
    internal sealed record Index(string Name, JsonElement Keys, bool Unique = false);
}
