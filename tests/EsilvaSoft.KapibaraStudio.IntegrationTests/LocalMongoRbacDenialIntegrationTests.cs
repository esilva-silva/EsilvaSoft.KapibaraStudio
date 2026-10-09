using System.Security.Cryptography;
using MongoDB.Bson;
using MongoDB.Driver;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

[TestFixture]
[Category("Integration")]
public sealed class LocalMongoRbacDenialIntegrationTests
{
    private const string OptInVariable = "KAPIBARA_F6_LIVE_MONGO";
    private const string ConnectionVariable = "KAPIBARA_F6_MONGO_URI";
    private const string DatabaseName = "sample_mflix";
    private const int UnauthorizedCode = 13;

    [Test]
    public async Task UserWithoutRolesIsDeniedDocumentAndCollectionMetadataReads()
    {
        var optIn = Environment.GetEnvironmentVariable(OptInVariable);
        var connectionString = Environment.GetEnvironmentVariable(ConnectionVariable);
        if (!string.Equals(optIn, "1", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(connectionString))
        {
            Assert.Ignore($"Homologação Mongo local opt-in: defina {OptInVariable}=1 e {ConnectionVariable}; nenhum banco foi acessado.");
        }

        MongoUrl mongoUrl;
        try
        {
            mongoUrl = MongoUrl.Create(connectionString);
        }
        catch (Exception)
        {
            Assert.Fail("A URI configurada para homologação Mongo é inválida; nenhum banco foi acessado.");
            return;
        }

        Assert.That(mongoUrl.DatabaseName, Is.EqualTo(DatabaseName), "A URI precisa apontar para sample_mflix.");
        Assert.That(mongoUrl.Server.Host, Is.AnyOf("localhost", "127.0.0.1", "::1"),
            "Este teste aceita somente MongoDB loopback local.");

        var administrator = new MongoClient(connectionString);
        var adminDatabase = administrator.GetDatabase("admin");
        BsonDocument commandLineOptions;
        try
        {
            commandLineOptions = await adminDatabase.RunCommandAsync<BsonDocument>(new BsonDocument("getCmdLineOpts", 1));
        }
        catch (MongoCommandException exception) when (exception.Code == UnauthorizedCode)
        {
            Assert.Ignore("A conexão configurada não pode inspecionar a configuração de autorização; nenhum usuário foi criado.");
            return;
        }
        catch (Exception)
        {
            Assert.Ignore("Não foi possível verificar a configuração de autorização do servidor; nenhum usuário foi criado.");
            return;
        }

        var authorization = commandLineOptions.GetValue("parsed", new BsonDocument()).AsBsonDocument
            .GetValue("security", new BsonDocument()).AsBsonDocument
            .GetValue("authorization", BsonNull.Value);
        if (authorization.BsonType != BsonType.String || authorization.AsString != "enabled")
        {
            Assert.Ignore("O servidor não expõe autorização habilitada; negação RBAC não pode ser provada e nenhum usuário foi criado.");
            return;
        }

        BsonDocument connectionStatus;
        try
        {
            connectionStatus = await adminDatabase.RunCommandAsync<BsonDocument>(new BsonDocument
            {
                ["connectionStatus"] = 1,
                ["showPrivileges"] = true
            });
        }
        catch (Exception)
        {
            Assert.Ignore("Não foi possível verificar os privilégios administrativos necessários; nenhum usuário foi criado.");
            return;
        }

        var authenticatedUserPrivileges = connectionStatus.GetValue("authInfo", new BsonDocument()).AsBsonDocument
            .GetValue("authenticatedUserPrivileges", new BsonArray());
        if (authenticatedUserPrivileges is not BsonArray privileges
            || !HasDatabaseAction(privileges, "createUser")
            || !HasDatabaseAction(privileges, "dropUser")
            || !HasDatabaseAction(privileges, "viewUser"))
        {
            Assert.Ignore("A conexão não comprova createUser, dropUser e viewUser em sample_mflix; nenhum usuário foi criado.");
            return;
        }

        var username = "f6_rbac_denial_" + Guid.NewGuid().ToString("N");
        var passwordBytes = RandomNumberGenerator.GetBytes(48);
        var password = Convert.ToBase64String(passwordBytes);
        CryptographicOperations.ZeroMemory(passwordBytes);

        var database = administrator.GetDatabase(DatabaseName);
        BsonDocument? initialUsers;
        try
        {
            initialUsers = await ReadNamedUserAsync(database, username);
        }
        catch (Exception)
        {
            Assert.Ignore("A conexão administrativa não pode verificar o usuário sintético antes da escrita; nenhum usuário foi criado.");
            return;
        }
        if (initialUsers is not null)
            Assert.Fail("Colisão inesperada de usuário sintético; nenhuma escrita foi iniciada.");

        var creationMayHaveSucceeded = false;
        Exception? operationFailure = null;
        Exception? cleanupFailure = null;
        var denialResults = new List<(string Operation, int? Code)>();
        try
        {
            try
            {
                creationMayHaveSucceeded = true;
                await database.RunCommandAsync<BsonDocument>(new BsonDocument
                {
                    ["createUser"] = username,
                    ["pwd"] = password,
                    ["roles"] = new BsonArray(),
                    ["mechanisms"] = new BsonArray { "SCRAM-SHA-256" }
                });

                var createdUser = await ReadNamedUserAsync(database, username);
                Assert.That(createdUser, Is.Not.Null, "O usuário sintético precisa ser confirmado antes de testar a conexão restrita.");
                Assert.That(createdUser!["roles"].AsBsonArray, Is.Empty, "A identidade usada no teste não pode ter privilégios herdados.");

                var restrictedSettings = MongoClientSettings.FromConnectionString(connectionString);
                restrictedSettings.Credential = MongoCredential.CreateCredential(DatabaseName, username, password);
                using var restrictedClient = new MongoClient(restrictedSettings);
                var restrictedDatabase = restrictedClient.GetDatabase(DatabaseName);

                denialResults.Add(("find", await CaptureUnauthorizedAsync(async () =>
                {
                    var response = await restrictedDatabase.RunCommandAsync<BsonDocument>(new BsonDocument
                    {
                        ["find"] = "movies",
                        ["filter"] = new BsonDocument("_id", "f6-rbac-never-match-" + Guid.NewGuid().ToString("N")),
                        ["limit"] = 1,
                        ["singleBatch"] = true
                    });
                    if (response.GetValue("cursor", new BsonDocument()).AsBsonDocument
                        .GetValue("firstBatch", new BsonArray()).AsBsonArray.Count != 0)
                    {
                        throw new InvalidOperationException("A conta sem roles retornou documentos; o smoke será reprovado.");
                    }
                })));

                denialResults.Add(("listCollections", await CaptureUnauthorizedAsync(async () =>
                {
                    _ = await restrictedDatabase.RunCommandAsync<BsonDocument>(new BsonDocument
                    {
                        ["listCollections"] = 1,
                        ["nameOnly"] = true,
                        ["authorizedCollections"] = false
                    });
                })));

                Assert.Multiple(() =>
                {
                    Assert.That(denialResults, Has.Count.EqualTo(2));
                    foreach (var (operation, code) in denialResults)
                    {
                        Assert.That(code, Is.EqualTo(UnauthorizedCode),
                            $"A operação read-only {operation} deve falhar com Unauthorized (13), confirmando negação RBAC.");
                    }
                });
            }
            catch (Exception exception)
            {
                // Do not expose command text, connection data, or the in-memory password in test output.
                operationFailure = new InvalidOperationException("A homologação RBAC falhou antes de provar as duas negações esperadas.",
                    exception is MongoCommandException commandException
                        ? new InvalidOperationException($"MongoDB retornou código {commandException.Code}.")
                        : new InvalidOperationException(exception.GetType().Name));
            }
        }
        finally
        {
            if (creationMayHaveSucceeded)
            {
                try
                {
                    var user = await ReadNamedUserAsync(database, username);
                    if (user is not null)
                    {
                        if (user["roles"].AsBsonArray.Count != 0)
                        {
                            cleanupFailure = new InvalidOperationException("O usuário sintético ganhou roles; remoção recusada por segurança.");
                        }
                        else
                        {
                            await database.RunCommandAsync<BsonDocument>(new BsonDocument("dropUser", username));
                            if (await ReadNamedUserAsync(database, username) is not null)
                                cleanupFailure = new InvalidOperationException("O usuário sintético ainda aparece após dropUser.");
                        }
                    }
                }
                catch (Exception exception)
                {
                    cleanupFailure = new InvalidOperationException("A limpeza do usuário sintético não foi confirmada.",
                        exception is MongoCommandException commandException
                            ? new InvalidOperationException($"MongoDB retornou código {commandException.Code}.")
                            : new InvalidOperationException(exception.GetType().Name));
                }
            }
        }

        if (operationFailure is not null && cleanupFailure is not null)
            throw new AggregateException("O smoke RBAC falhou e a limpeza requer atenção.", operationFailure, cleanupFailure);
        if (cleanupFailure is not null)
            throw cleanupFailure;
        if (operationFailure is not null)
            throw operationFailure;

        TestContext.Progress.WriteLine("F6-19: conta sintética sem roles recebeu Unauthorized (13) em find e listCollections; usuário removido e readback confirmou ausência.");
    }

    private static async Task<BsonDocument?> ReadNamedUserAsync(IMongoDatabase database, string username)
    {
        var response = await database.RunCommandAsync<BsonDocument>(new BsonDocument
        {
            ["usersInfo"] = new BsonDocument { ["user"] = username, ["db"] = DatabaseName },
            ["showCredentials"] = false,
            ["showPrivileges"] = false
        });
        var users = response.GetValue("users", BsonNull.Value);
        if (users is not BsonArray values)
            throw new InvalidOperationException("MongoDB retornou usersInfo em formato inesperado.");
        return values.OfType<BsonDocument>().SingleOrDefault(user =>
            user.GetValue("user", BsonNull.Value).AsString == username
            && user.GetValue("db", BsonNull.Value).AsString == DatabaseName);
    }

    private static async Task<int?> CaptureUnauthorizedAsync(Func<Task> operation)
    {
        try
        {
            await operation();
            return null;
        }
        catch (MongoCommandException exception)
        {
            return exception.Code;
        }
    }

    private static bool HasDatabaseAction(BsonArray privileges, string action) => privileges
        .OfType<BsonDocument>()
        .Any(privilege => ResourceCoversSampleMflix(privilege.GetValue("resource", new BsonDocument()).AsBsonDocument)
            && privilege.GetValue("actions", new BsonArray()).AsBsonArray
                .OfType<BsonString>()
                .Any(candidate => candidate.Value is "anyAction" || candidate.Value == action));

    private static bool ResourceCoversSampleMflix(BsonDocument resource)
    {
        if (resource.GetValue("anyResource", false).ToBoolean())
            return true;
        if (resource.GetValue("cluster", false).ToBoolean())
            return false;

        var database = resource.GetValue("db", BsonNull.Value);
        if (database.BsonType != BsonType.String)
            return false;
        var databaseName = database.AsString;
        if (databaseName.Length != 0 && databaseName != DatabaseName)
            return false;

        var collection = resource.GetValue("collection", BsonNull.Value);
        return collection.BsonType == BsonType.String && collection.AsString.Length == 0;
    }
}
