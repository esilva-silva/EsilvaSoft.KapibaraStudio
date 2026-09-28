using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;
using MongoDB.Bson;
using MongoDB.Driver;

namespace EsilvaSoft.KapibaraStudio.Infrastructure;

/// <summary>Diagnóstico e administração de nível de servidor: status, operações em curso, usuários e papéis.</summary>
internal static class MongoServerAdministrator
{
    public static async Task<string> GetServerStatusAsync(MongoOperationContext context, CancellationToken cancellationToken)
    {
        var status = await context.CreateClient()
            .GetDatabase("admin")
            .RunCommandAsync<BsonDocument>(new BsonDocument("serverStatus", 1), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return status.ToJson(MongoJson.CanonicalSettings);
    }

    public static async Task<string> GetTopologyAsync(MongoOperationContext context, CancellationToken cancellationToken)
    {
        var topology = await context.CreateClient()
            .GetDatabase("admin")
            .RunCommandAsync<BsonDocument>(new BsonDocument("hello", 1), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return topology.ToJson(MongoJson.CanonicalSettings);
    }

    public static async Task<string> GetCurrentOperationsAsync(MongoOperationContext context, CancellationToken cancellationToken)
    {
        var operations = await context.CreateClient()
            .GetDatabase("admin")
            .RunCommandAsync<BsonDocument>(
                new BsonDocument
                {
                    ["currentOp"] = 1,
                    ["allUsers"] = true
                },
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return operations.ToJson(MongoJson.CanonicalSettings);
    }

    public static async Task<string> GetProfilerStatusAsync(MongoOperationContext context, string database, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(database);
        var status = await context.CreateClient()
            .GetDatabase(database)
            .RunCommandAsync<BsonDocument>(new BsonDocument("profile", -1), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return status.ToJson(MongoJson.CanonicalSettings);
    }

    public static async Task KillOperationAsync(MongoOperationContext context, ConnectionProfile profile, OperationKillRequest request, CancellationToken cancellationToken)
    {
        profile.EnsureWriteAllowed();
        var operationId = request.GetOperationId();
        await context.CreateClient()
            .GetDatabase("admin")
            .RunCommandAsync<BsonDocument>(new BsonDocument("killOp", operationId), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    public static async Task<string> ValidateCollectionIntegrityAsync(MongoOperationContext context, ConnectionProfile profile, CollectionIntegrityCheckRequest request, CancellationToken cancellationToken)
    {
        profile.EnsureWriteAllowed();
        request.Validate();
        var result = await context.CreateClient()
            .GetDatabase(request.Database)
            .RunCommandAsync<BsonDocument>(
                new BsonDocument
                {
                    ["validate"] = request.Collection,
                    ["full"] = true
                },
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return result.ToJson(MongoJson.CanonicalSettings);
    }

    public static async Task<string> CompactCollectionAsync(MongoOperationContext context, ConnectionProfile profile, CollectionCompactRequest request, CancellationToken cancellationToken)
    {
        profile.EnsureWriteAllowed();
        request.Validate();
        var command = new BsonDocument("compact", request.Collection);
        if (request.Force)
        {
            command["force"] = true;
        }

        var result = await context.CreateClient()
            .GetDatabase(request.Database)
            .RunCommandAsync<BsonDocument>(command, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return result.ToJson(MongoJson.CanonicalSettings);
    }

    public static async Task<string> GetUsersAsync(MongoOperationContext context, CancellationToken cancellationToken)
    {
        var users = await context.CreateClient()
            .GetDatabase("admin")
            .RunCommandAsync<BsonDocument>(new BsonDocument("usersInfo", 1), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return users.ToJson(MongoJson.CanonicalSettings);
    }

    public static async Task CreateUserAsync(MongoOperationContext context, ConnectionProfile profile, DatabaseUserCreateRequest request, CancellationToken cancellationToken)
    {
        profile.EnsureWriteAllowed();
        request.Validate();
        var roles = context.ParsePipeline(request.RolesJson);
        var command = new BsonDocument
        {
            ["createUser"] = request.Username.Trim(),
            ["pwd"] = request.Password,
            ["roles"] = new BsonArray(roles.Select(role => (BsonValue)role))
        };
        await context.CreateClient()
            .GetDatabase(request.Database)
            .RunCommandAsync<BsonDocument>(command, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    public static async Task DropUserAsync(MongoOperationContext context, ConnectionProfile profile, DatabaseUserDropRequest request, CancellationToken cancellationToken)
    {
        profile.EnsureWriteAllowed();
        request.Validate();
        await context.CreateClient()
            .GetDatabase(request.Database)
            .RunCommandAsync<BsonDocument>(
                new BsonDocument("dropUser", request.Username.Trim()),
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    public static async Task UpdateUserRolesAsync(MongoOperationContext context, ConnectionProfile profile, DatabaseUserRoleRequest request, CancellationToken cancellationToken)
    {
        profile.EnsureWriteAllowed();
        request.Validate();
        var commandName = request.Revoke ? "revokeRolesFromUser" : "grantRolesToUser";
        var command = new BsonDocument
        {
            [commandName] = request.Username.Trim(),
            ["roles"] = new BsonArray(context.ParsePipeline(request.RolesJson).Select(role => (BsonValue)role))
        };
        await context.CreateClient()
            .GetDatabase(request.Database)
            .RunCommandAsync<BsonDocument>(command, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    public static async Task<string> GetRolesAsync(MongoOperationContext context, CancellationToken cancellationToken)
    {
        var roles = await context.CreateClient()
            .GetDatabase("admin")
            .RunCommandAsync<BsonDocument>(new BsonDocument("rolesInfo", 1), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return roles.ToJson(MongoJson.CanonicalSettings);
    }
}
