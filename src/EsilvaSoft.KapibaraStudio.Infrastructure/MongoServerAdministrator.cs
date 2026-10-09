using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
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
        var operations = await ReadCurrentOperationsAsync(context, cancellationToken).ConfigureAwait(false);
        return operations.ToJson(MongoJson.CanonicalSettings);
    }

    private static async Task<BsonDocument> ReadCurrentOperationsAsync(MongoOperationContext context, CancellationToken cancellationToken)
    {
        // $currentOp is collectionless. Include idle sessions holding transaction locks and
        // idle connections (index builds waiting for commit quorum), but bound one snapshot.
        var pipeline = PipelineDefinition<NoPipelineInput, BsonDocument>.Create(
        [
            new BsonDocument("$currentOp", new BsonDocument
            {
                ["allUsers"] = true,
                ["idleSessions"] = true,
                ["idleConnections"] = true,
                ["idleCursors"] = false
            }),
            new BsonDocument("$limit", 201)
        ]);
        var admin = context.CreateClient().GetDatabase("admin");
        try
        {
            using var cursor = await admin.AggregateAsync(pipeline,
                new AggregateOptions { BatchSize = 64, MaxTime = TimeSpan.FromSeconds(5) },
                cancellationToken).ConfigureAwait(false);
            var batch = new BsonArray();
            while (batch.Count < 201 && await cursor.MoveNextAsync(cancellationToken).ConfigureAwait(false))
            {
                foreach (var operation in cursor.Current)
                {
                    if (batch.Count == 201)
                        break;
                    batch.Add(operation);
                }
            }

            return new BsonDocument
            {
                ["inprog"] = batch,
                ["source"] = "$currentOp",
                ["truncated"] = batch.Count > 200
            };
        }
        catch (MongoCommandException exception) when (exception.Code is 40324 or 16872 or 59)
        {
            // Legacy servers may not support the aggregation stage. Never substitute own-ops
            // for a denied all-users request; that would silently change the scope.
            var legacy = await admin.RunCommandAsync<BsonDocument>(new BsonDocument
            {
                ["currentOp"] = 1,
                ["$all"] = true
            }, cancellationToken: cancellationToken).ConfigureAwait(false);
            var legacyOperations = legacy.GetValue("inprog", new BsonArray()).AsBsonArray;
            var truncated = legacyOperations.Count > 200;
            legacy["inprog"] = new BsonArray(legacyOperations.Take(201));
            legacy["truncated"] = truncated;
            legacy["source"] = "currentOp (legacy)";
            return legacy;
        }
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

    public static async Task<ProfilerConfigurationResult> ConfigureProfilerAsync(
        MongoOperationContext context,
        ConnectionProfile profile,
        ProfilerConfigurationRequest request,
        CancellationToken cancellationToken)
    {
        profile.EnsureWriteAllowed();
        request.Validate();
        var client = context.CreateClient();
        var database = client.GetDatabase(request.Database);
        var currentStatus = await GetProfilerStatusAsync(context, request.Database, cancellationToken).ConfigureAwait(false);
        var currentTopology = await GetTopologyAsync(context, cancellationToken).ConfigureAwait(false);
        if (ProfilerSettings.Parse(currentStatus) != ProfilerSettings.Parse(request.ExpectedStatusJson)
            || ProfilerSettings.IsMongos(currentTopology) != ProfilerSettings.IsMongos(request.ExpectedTopologyJson))
            throw new InvalidOperationException("O estado do profiler ou a topologia mudou após a prévia. Revise novamente.");
        if (ProfilerSettings.IsMongos(currentTopology))
            throw new NotSupportedException("A configuração do profiler pelo mongos não está liberada neste fluxo; conecte-se a um mongod compatível.");

        var command = new BsonDocument("profile", request.Level);
        if (request.SlowMs is { } slow)
            command["slowms"] = slow;
        if (request.SampleRate is { } rate)
            command["sampleRate"] = (double)rate;
        var expectedFilter = request.FilterMode == ProfilerFilterMode.Set
            ? context.ParseDocument(request.FilterJson!, "filtro do profiler") : null;
        command["filter"] = expectedFilter is null ? "unset" : expectedFilter;

        await database.RunCommandAsync<BsonDocument>(command, cancellationToken: cancellationToken).ConfigureAwait(false);
        var observedStatus = await GetProfilerStatusAsync(context, request.Database, cancellationToken).ConfigureAwait(false);
        var observed = ProfilerSettings.Parse(observedStatus);
        var observedDocument = BsonDocument.Parse(observedStatus);
        var observedFilter = observedDocument.GetValue("filter", BsonNull.Value);
        var filterMatches = expectedFilter is null
            ? observedFilter.IsBsonNull
            : observedFilter.IsBsonDocument && observedFilter.AsBsonDocument.Equals(expectedFilter);
        if (observed.Level != request.Level
            || request.SlowMs is not null && observed.SlowMs != request.SlowMs
            || request.SampleRate is not null && observed.SampleRate != request.SampleRate
            || !filterMatches)
            throw new InvalidOperationException("O comando foi enviado, mas a configuração resultante não corresponde à solicitada. Releia o profiler antes de continuar.");

        return new ProfilerConfigurationResult(observed.Level, observed.SlowMs,
            observed.SampleRate, !observedFilter.IsBsonNull);
    }

    public static async Task<ProfilerCapturePage> ReadProfilerCaptureAsync(
        MongoOperationContext context, string database, DateTimeOffset fromUtc,
        DateTimeOffset throughUtc, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(database);
        if (throughUtc < fromUtc || throughUtc - fromUtc > TimeSpan.FromHours(1))
            throw new ArgumentOutOfRangeException(nameof(throughUtc), "A janela do profiler deve ter no máximo uma hora.");
        var collection = context.CreateClient().GetDatabase(database).GetCollection<BsonDocument>("system.profile");
        var filter = Builders<BsonDocument>.Filter.Gte("ts", fromUtc.UtcDateTime)
            & Builders<BsonDocument>.Filter.Lte("ts", throughUtc.UtcDateTime);
        var projection = Builders<BsonDocument>.Projection.Include("ts").Include("op")
            .Include("ns").Include("millis").Include("planSummary").Exclude("_id");
        var options = new FindOptions<BsonDocument, BsonDocument>
        {
            Projection = projection,
            Limit = 201,
            BatchSize = 64,
            MaxTime = TimeSpan.FromSeconds(5)
        };
        using var cursor = await collection.FindAsync(filter, options, cancellationToken).ConfigureAwait(false);
        var entries = new List<ProfilerCaptureEntry>(200);
        var totalBytes = 0;
        var truncated = false;
        while (await cursor.MoveNextAsync(cancellationToken).ConfigureAwait(false))
        {
            foreach (var item in cursor.Current)
            {
                totalBytes += item.ToBson().Length;
                if (entries.Count >= 200 || totalBytes > 1_048_576)
                {
                    truncated = true;
                    break;
                }
                if (!item.TryGetValue("ts", out var timestamp) || !timestamp.IsBsonDateTime)
                    continue;
                static string ReadText(BsonDocument document, string key, int maximum) =>
                    document.TryGetValue(key, out var value) && value.IsString
                        ? value.AsString[..Math.Min(value.AsString.Length, maximum)] : string.Empty;
                int? millis = item.TryGetValue("millis", out var duration) && duration.IsInt32
                    ? duration.AsInt32 : null;
                entries.Add(new ProfilerCaptureEntry(timestamp.AsBsonDateTime.ToUniversalTime(),
                    ReadText(item, "op", 32), ReadText(item, "ns", 256), millis,
                    ReadText(item, "planSummary", 256)));
            }
            if (truncated)
                break;
        }
        return new ProfilerCapturePage(entries, truncated, throughUtc);
    }

    public static async Task KillOperationAsync(MongoOperationContext context, ConnectionProfile profile, OperationKillRequest request, CancellationToken cancellationToken)
    {
        profile.EnsureWriteAllowed();
        var client = context.CreateClient();
        var admin = client.GetDatabase("admin");
        await OperationKillCoordinator.ExecuteAsync(
            request,
            async token => (await ReadCurrentOperationsAsync(context, token).ConfigureAwait(false))
                .ToJson(MongoJson.CanonicalSettings),
            async (operationId, token) =>
            {
                await admin.RunCommandAsync<BsonDocument>(
                    BuildKillOperationCommand(operationId), cancellationToken: token).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    internal static BsonDocument BuildKillOperationCommand(long operationId)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(operationId, 1);
        return new BsonDocument { ["killOp"] = 1, ["op"] = operationId };
    }

    public static async Task<string> ValidateCollectionIntegrityAsync(MongoOperationContext context, ConnectionProfile profile, CollectionIntegrityCheckRequest request, CancellationToken cancellationToken)
    {
        profile.EnsureWriteAllowed();
        request.Validate();
        await MongoCollectionMaintenanceGuard.EnsureCurrentAsync(
            context, request.Preflight!, request.Database, request.Collection, cancellationToken).ConfigureAwait(false);
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
        _ = await MongoCollectionMaintenanceGuard.ReadPostflightAsync(
            context, request.Database, request.Collection, cancellationToken).ConfigureAwait(false);
        return result.ToJson(MongoJson.CanonicalSettings);
    }

    public static async Task<string> CompactCollectionAsync(MongoOperationContext context, ConnectionProfile profile, CollectionCompactRequest request, CancellationToken cancellationToken)
    {
        profile.EnsureWriteAllowed();
        request.Validate();
        var current = await MongoCollectionMaintenanceGuard.EnsureCurrentAsync(
            context, request.Preflight!, request.Database, request.Collection, cancellationToken).ConfigureAwait(false);
        MongoCollectionMaintenanceGuard.EnsureCompactSupported(current, request.Force);
        var command = new BsonDocument("compact", request.Collection);
        if (request.Force)
        {
            command["force"] = true;
        }

        var result = await context.CreateClient()
            .GetDatabase(request.Database)
            .RunCommandAsync<BsonDocument>(command, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        _ = await MongoCollectionMaintenanceGuard.ReadPostflightAsync(
            context, request.Database, request.Collection, cancellationToken).ConfigureAwait(false);
        return result.ToJson(MongoJson.CanonicalSettings);
    }

    public static async Task<string> GetUsersAsync(MongoOperationContext context, CancellationToken cancellationToken)
    {
        var users = await context.CreateClient()
            .GetDatabase("admin")
            .RunCommandAsync<BsonDocument>(new BsonDocument
            {
                ["usersInfo"] = 1,
                ["showCredentials"] = false,
                ["showPrivileges"] = false
            }, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return users.ToJson(MongoJson.CanonicalSettings);
    }

    public static async Task<string?> GetUserRolesAsync(MongoOperationContext context, string database, string username, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        var response = await RunUserCommandAsync(context, database, new BsonDocument
        {
            ["usersInfo"] = new BsonDocument { ["user"] = username.Trim(), ["db"] = database.Trim() },
            ["showCredentials"] = false,
            ["showPrivileges"] = false
        }, cancellationToken).ConfigureAwait(false);
        if (!response.TryGetValue("users", out var usersValue) || usersValue is not BsonArray users)
            throw new InvalidOperationException("O servidor retornou uma resposta de usuários inválida.");
        foreach (var value in users)
        {
            if (value is BsonDocument user
                && user.GetValue("user", BsonNull.Value).AsString == username.Trim()
                && user.GetValue("db", BsonNull.Value).AsString == database.Trim())
            {
                var roles = user.GetValue("roles", new BsonArray()).AsBsonArray;
                return new BsonArray(roles.OrderBy(RoleSortKey, StringComparer.Ordinal)).ToJson(MongoJson.CanonicalSettings);
            }
        }
        return null;
    }

    public static async Task CreateUserAsync(MongoOperationContext context, ConnectionProfile profile, DatabaseUserCreateRequest request, CancellationToken cancellationToken)
    {
        profile.EnsureWriteAllowed();
        request.Validate();
        if (await GetUserRolesAsync(context, request.Database, request.Username, cancellationToken).ConfigureAwait(false) is not null)
            throw new MongoUserAdministrationException(MongoUserAdministrationFailureKind.Conflict);
        var roles = context.ParsePipeline(request.RolesJson);
        var command = new BsonDocument
        {
            ["createUser"] = request.Username.Trim(),
            ["pwd"] = request.Password,
            ["roles"] = new BsonArray(roles.Select(role => (BsonValue)role))
        };
        await RunUserCommandAsync(context, request.Database, command, cancellationToken).ConfigureAwait(false);
        string? afterRolesJson;
        try
        {
            afterRolesJson = await GetUserRolesAsync(context, request.Database, request.Username, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new MongoUserAdministrationException(MongoUserAdministrationFailureKind.ReadbackFailed);
        }
        if (afterRolesJson is null || !RoleArraysEqual(afterRolesJson, new BsonArray(roles).ToJson(MongoJson.CanonicalSettings)))
            throw new MongoUserAdministrationException(MongoUserAdministrationFailureKind.ReadbackFailed);
    }

    public static async Task DropUserAsync(MongoOperationContext context, ConnectionProfile profile, DatabaseUserDropRequest request, CancellationToken cancellationToken)
    {
        profile.EnsureWriteAllowed();
        request.Validate();
        if (await GetUserRolesAsync(context, request.Database, request.Username, cancellationToken).ConfigureAwait(false) is null)
            throw new MongoUserAdministrationException(MongoUserAdministrationFailureKind.Conflict);
        await RunUserCommandAsync(context, request.Database,
            new BsonDocument("dropUser", request.Username.Trim()), cancellationToken).ConfigureAwait(false);
        string? afterRolesJson;
        try
        {
            afterRolesJson = await GetUserRolesAsync(context, request.Database, request.Username, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new MongoUserAdministrationException(MongoUserAdministrationFailureKind.ReadbackFailed);
        }
        if (afterRolesJson is not null)
            throw new MongoUserAdministrationException(MongoUserAdministrationFailureKind.ReadbackFailed);
    }

    public static async Task UpdateUserRolesAsync(MongoOperationContext context, ConnectionProfile profile, DatabaseUserRoleRequest request, CancellationToken cancellationToken)
    {
        profile.EnsureWriteAllowed();
        request.Validate();
        var beforeRolesJson = await GetUserRolesAsync(context, request.Database, request.Username, cancellationToken).ConfigureAwait(false);
        if (beforeRolesJson is null)
            throw new MongoUserAdministrationException(MongoUserAdministrationFailureKind.Conflict);
        if (!RoleArraysEqual(beforeRolesJson, request.ExpectedRolesJson!))
            throw new MongoUserAdministrationException(MongoUserAdministrationFailureKind.Conflict);
        var commandName = request.Revoke ? "revokeRolesFromUser" : "grantRolesToUser";
        var changedRoles = context.ParsePipeline(request.RolesJson);
        var expectedAfter = BsonSerializer.Deserialize<BsonArray>(beforeRolesJson).ToList();
        foreach (var role in changedRoles)
        {
            var index = expectedAfter.FindIndex(existing => RoleIdentity(existing) == RoleIdentity(role));
            if (request.Revoke)
            {
                if (index >= 0) expectedAfter.RemoveAt(index);
            }
            else if (index < 0)
            {
                expectedAfter.Add(role);
            }
        }
        var command = new BsonDocument
        {
            [commandName] = request.Username.Trim(),
            ["roles"] = new BsonArray(changedRoles.Select(role => (BsonValue)role))
        };
        await RunUserCommandAsync(context, request.Database, command, cancellationToken).ConfigureAwait(false);
        string? afterRolesJson;
        try
        {
            afterRolesJson = await GetUserRolesAsync(context, request.Database, request.Username, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new MongoUserAdministrationException(MongoUserAdministrationFailureKind.ReadbackFailed);
        }
        if (afterRolesJson is null || !RoleArraysEqual(afterRolesJson, new BsonArray(expectedAfter).ToJson(MongoJson.CanonicalSettings)))
            throw new MongoUserAdministrationException(MongoUserAdministrationFailureKind.ReadbackFailed);
    }

    private static string RoleSortKey(BsonValue role) => role is BsonDocument document
        ? $"{document.GetValue("db", BsonNull.Value)}\0{document.GetValue("role", BsonNull.Value)}"
        : role.ToJson(MongoJson.CanonicalSettings);

    private static async Task<BsonDocument> RunUserCommandAsync(MongoOperationContext context,
        string database, BsonDocument command, CancellationToken cancellationToken)
    {
        try
        {
            return await context.CreateClient().GetDatabase(database.Trim())
                .RunCommandAsync<BsonDocument>(command, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (MongoCommandException exception) when (exception.Code == 13)
        {
            throw new MongoUserAdministrationException(MongoUserAdministrationFailureKind.PermissionDenied);
        }
        catch (MongoCommandException exception) when (exception.Code is 59 or 115 or 40324 or 16872)
        {
            throw new MongoUserAdministrationException(MongoUserAdministrationFailureKind.Unsupported);
        }
        catch (MongoCommandException)
        {
            throw new MongoUserAdministrationException(MongoUserAdministrationFailureKind.CommandFailed);
        }
    }

    private static string RoleIdentity(BsonValue role) => RoleSortKey(role);

    private static bool RoleArraysEqual(string left, string right)
    {
        var leftRoles = BsonSerializer.Deserialize<BsonArray>(left).Select(RoleIdentity).Order(StringComparer.Ordinal).ToArray();
        var rightRoles = BsonSerializer.Deserialize<BsonArray>(right).Select(RoleIdentity).Order(StringComparer.Ordinal).ToArray();
        return leftRoles.SequenceEqual(rightRoles, StringComparer.Ordinal);
    }

    public static async Task<string> GetRolesAsync(MongoOperationContext context, CancellationToken cancellationToken)
    {
        var roles = await context.CreateClient()
            .GetDatabase("admin")
            .RunCommandAsync<BsonDocument>(new BsonDocument("rolesInfo", 1), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return roles.ToJson(MongoJson.CanonicalSettings);
    }

    public static Task<int> GetRuntimeServerParameterAsync(
        MongoOperationContext context,
        string parameterName,
        CancellationToken cancellationToken)
    {
        ValidateRuntimeParameterName(parameterName);
        return ReadRuntimeParameterAsync(context, parameterName, cancellationToken);
    }

    public static async Task<RuntimeServerParameterMutationResult> SetRuntimeServerParameterAsync(
        MongoOperationContext context,
        ConnectionProfile profile,
        RuntimeServerParameterRequest request,
        CancellationToken cancellationToken)
    {
        profile.EnsureWriteAllowed();
        request.Validate();
        var current = await ReadRuntimeParameterAsync(context, request.ParameterName, cancellationToken).ConfigureAwait(false);
        if (current != request.PreviousValue)
            throw new InvalidOperationException("O valor runtime mudou desde a prévia. Atualize a leitura antes de confirmar.");

        await RunRuntimeParameterCommandAsync(context, BuildSetRuntimeParameterCommand(request), cancellationToken).ConfigureAwait(false);

        int readback;
        try
        {
            readback = await ReadRuntimeParameterAsync(context, request.ParameterName, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new RuntimeServerParameterException(RuntimeServerParameterFailureKind.ReadbackFailed);
        }

        if (readback != request.RequestedValue)
            throw new RuntimeServerParameterException(RuntimeServerParameterFailureKind.ReadbackFailed);

        return new RuntimeServerParameterMutationResult(
            request.ParameterName,
            request.PreviousValue,
            readback,
            DateTimeOffset.UtcNow);
    }

    private static async Task<int> ReadRuntimeParameterAsync(
        MongoOperationContext context,
        string parameterName,
        CancellationToken cancellationToken)
    {
        ValidateRuntimeParameterName(parameterName);
        var response = await RunRuntimeParameterCommandAsync(context, BuildGetRuntimeParameterCommand(parameterName), cancellationToken).ConfigureAwait(false);
        return ParseRuntimeParameterValue(parameterName, response);
    }

    internal static int ParseRuntimeParameterValue(string parameterName, BsonDocument response)
    {
        ValidateRuntimeParameterName(parameterName);
        ArgumentNullException.ThrowIfNull(response);
        if (!response.TryGetValue(parameterName, out var parameterValue)
            || parameterValue is not BsonDocument details
            || !details.TryGetValue("settableAtRuntime", out var runtimeValue)
            || !runtimeValue.IsBoolean
            || !runtimeValue.AsBoolean
            || !details.TryGetValue("value", out var value)
            || !value.IsInt32)
        {
            throw new RuntimeServerParameterException(RuntimeServerParameterFailureKind.Unsupported);
        }

        var level = value.AsInt32;
        if (level < 0 || parameterName == RuntimeServerParameters.LogLevel && level > RuntimeServerParameters.LogLevelMaximum)
            throw new InvalidOperationException("O servidor retornou um valor runtime fora do intervalo suportado.");
        return level;
    }

    private static async Task<BsonDocument> RunRuntimeParameterCommandAsync(
        MongoOperationContext context,
        BsonDocument command,
        CancellationToken cancellationToken)
    {
        try
        {
            return await context.CreateClient()
                .GetDatabase("admin")
                .RunCommandAsync<BsonDocument>(command, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (MongoCommandException exception) when (exception.Code == 13)
        {
            throw new RuntimeServerParameterException(RuntimeServerParameterFailureKind.PermissionDenied);
        }
        catch (MongoCommandException exception) when (
            exception.Code is 59 or 115 or 40324 or 16872
            || exception.Message.Contains("not supported", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("unsupported", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("not allowed on this deployment", StringComparison.OrdinalIgnoreCase))
        {
            throw new RuntimeServerParameterException(RuntimeServerParameterFailureKind.Unsupported);
        }
    }

    private static void ValidateRuntimeParameterName(string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parameterName);
        if (!RuntimeServerParameters.IsAllowed(parameterName))
            throw new ArgumentException("O parâmetro runtime não está na allowlist.", nameof(parameterName));
    }

    internal static BsonDocument BuildGetRuntimeParameterCommand(string parameterName)
    {
        ValidateRuntimeParameterName(parameterName);
        return new BsonDocument
        {
            ["getParameter"] = new BsonDocument("showDetails", true),
            [parameterName] = 1
        };
    }

    internal static BsonDocument BuildSetRuntimeParameterCommand(RuntimeServerParameterRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        return new BsonDocument
        {
            ["setParameter"] = 1,
            [request.ParameterName] = request.RequestedValue
        };
    }

    public static async Task<string> GetCustomRoleDefinitionAsync(
        MongoOperationContext context,
        string database,
        string roleName,
        CancellationToken cancellationToken)
    {
        ValidateRoleTarget(database, roleName);
        var role = await ReadCustomRoleAsync(context, database, roleName, cancellationToken).ConfigureAwait(false);
        return role?.ToJson(MongoJson.CanonicalSettings) ?? "null";
    }

    public static async Task<DatabaseRoleMutationResult> MutateCustomRoleAsync(
        MongoOperationContext context,
        ConnectionProfile profile,
        DatabaseRoleMutationRequest request,
        CancellationToken cancellationToken)
    {
        profile.EnsureWriteAllowed();
        request.Validate();
        ValidateRoleTarget(request.Database, request.RoleName);

        var current = await ReadCustomRoleAsync(context, request.Database, request.RoleName, cancellationToken).ConfigureAwait(false);
        var expected = ParseExpectedRole(request.ExpectedRoleDefinitionJson);
        if (!Equals(current, expected))
        {
            throw new InvalidOperationException("O papel mudou desde a prévia. Atualize a prévia antes de confirmar.");
        }

        if (current is { } existing && existing.GetValue("isBuiltin", false).ToBoolean())
        {
            throw new InvalidOperationException("Papéis internos do MongoDB não podem ser alterados por esta operação.");
        }

        if ((request.Kind is DatabaseRoleMutationKind.Update or DatabaseRoleMutationKind.Drop) && current is null)
        {
            throw new InvalidOperationException("O papel não existe mais no banco selecionado. Atualize a prévia.");
        }

        var command = BuildCustomRoleCommand(request);
        await RunCustomRoleCommandAsync(context, request.Database, command, cancellationToken).ConfigureAwait(false);

        BsonDocument? after;
        try
        {
            after = await ReadCustomRoleAsync(context, request.Database, request.RoleName, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new MongoRoleAdministrationException(MongoRoleAdministrationFailureKind.ReadbackFailed);
        }

        if (request.Kind == DatabaseRoleMutationKind.Drop)
        {
            if (after is not null)
                throw new MongoRoleAdministrationException(MongoRoleAdministrationFailureKind.ReadbackFailed);
        }
        else if (after is null || !RequestedRolePayloadMatches(request, after))
        {
            throw new MongoRoleAdministrationException(MongoRoleAdministrationFailureKind.ReadbackFailed);
        }

        return new DatabaseRoleMutationResult(
            current?.ToJson(MongoJson.CanonicalSettings),
            after?.ToJson(MongoJson.CanonicalSettings));
    }

    private static async Task<BsonDocument?> ReadCustomRoleAsync(
        MongoOperationContext context,
        string database,
        string roleName,
        CancellationToken cancellationToken)
    {
        var response = await RunCustomRoleCommandAsync(
            context,
            database,
            new BsonDocument
            {
                ["rolesInfo"] = new BsonDocument { ["role"] = roleName.Trim(), ["db"] = database.Trim() },
                ["showPrivileges"] = true
            },
            cancellationToken).ConfigureAwait(false);
        if (!response.TryGetValue("roles", out var rolesValue) || rolesValue is not BsonArray roles)
            throw new InvalidOperationException("O servidor retornou uma resposta de papéis inválida.");

        foreach (var value in roles)
        {
            if (value is BsonDocument role
                && role.GetValue("role", BsonNull.Value).AsString == roleName.Trim()
                && role.GetValue("db", BsonNull.Value).AsString == database.Trim())
                return role;
        }

        return null;
    }

    private static async Task<BsonDocument> RunCustomRoleCommandAsync(
        MongoOperationContext context,
        string database,
        BsonDocument command,
        CancellationToken cancellationToken)
    {
        try
        {
            return await context.CreateClient()
                .GetDatabase(database.Trim())
                .RunCommandAsync<BsonDocument>(command, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (MongoCommandException exception) when (exception.Code == 13)
        {
            throw new MongoRoleAdministrationException(MongoRoleAdministrationFailureKind.PermissionDenied);
        }
        catch (MongoCommandException exception) when (IsUnsupportedRoleCommand(exception))
        {
            throw new MongoRoleAdministrationException(MongoRoleAdministrationFailureKind.Unsupported);
        }
    }

    private static bool IsUnsupportedRoleCommand(MongoCommandException exception) =>
        exception.Code is 59 or 115 or 40324 or 16872
        || exception.Message.Contains("not supported", StringComparison.OrdinalIgnoreCase)
        || exception.Message.Contains("unsupported", StringComparison.OrdinalIgnoreCase)
        || exception.Message.Contains("not allowed on this deployment", StringComparison.OrdinalIgnoreCase);

    private static BsonDocument BuildCustomRoleCommand(DatabaseRoleMutationRequest request)
    {
        if (request.Kind == DatabaseRoleMutationKind.Drop)
            return new BsonDocument("dropRole", request.RoleName.Trim());

        var commandName = request.Kind == DatabaseRoleMutationKind.Create ? "createRole" : "updateRole";
        return new BsonDocument
        {
            [commandName] = request.RoleName.Trim(),
            ["privileges"] = BsonSerializer.Deserialize<BsonArray>(request.PrivilegesJson),
            ["roles"] = BsonSerializer.Deserialize<BsonArray>(request.InheritedRolesJson)
        };
    }

    private static bool RequestedRolePayloadMatches(DatabaseRoleMutationRequest request, BsonDocument after)
    {
        var privileges = BsonSerializer.Deserialize<BsonArray>(request.PrivilegesJson);
        var roles = BsonSerializer.Deserialize<BsonArray>(request.InheritedRolesJson);
        return after.TryGetValue("privileges", out var actualPrivileges)
            && actualPrivileges == privileges
            && after.TryGetValue("roles", out var actualRoles)
            && actualRoles == roles;
    }

    private static BsonDocument? ParseExpectedRole(string json)
    {
        var value = BsonSerializer.Deserialize<BsonValue>(json);
        return value.IsBsonNull ? null : value.AsBsonDocument;
    }

    private static void ValidateRoleTarget(string database, string roleName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(roleName);
    }
}
