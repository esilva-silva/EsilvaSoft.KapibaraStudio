using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Infrastructure;

internal static class MongoCollectionMaintenanceGuard
{
    public static async Task<CollectionMaintenancePreflight> CaptureAsync(
        MongoOperationContext context,
        string database,
        string collection,
        CancellationToken cancellationToken)
    {
        var topology = await MongoServerAdministrator.GetTopologyAsync(context, cancellationToken).ConfigureAwait(false);
        var definition = await MongoDatabaseAdministrator.GetCollectionDefinitionAsync(context, database, collection, cancellationToken).ConfigureAwait(false);
        var statistics = await MongoDatabaseAdministrator.GetCollectionStatsAsync(context, database, collection, cancellationToken).ConfigureAwait(false);
        var validation = await MongoDatabaseAdministrator.GetCollectionValidationAsync(context, database, collection, cancellationToken).ConfigureAwait(false);
        return new CollectionMaintenancePreflight(
            database,
            collection,
            topology,
            definition,
            statistics,
            validation,
            CollectionMaintenancePreflight.ExpectedSource,
            DateTimeOffset.UtcNow).Validate(database, collection);
    }

    public static async Task<CollectionMaintenancePreflight> EnsureCurrentAsync(
        MongoOperationContext context,
        CollectionMaintenancePreflight expected,
        string database,
        string collection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(expected);
        expected.Validate(database, collection);
        var current = await CaptureAsync(context, database, collection, cancellationToken).ConfigureAwait(false);
        expected.EnsureMatchesObservedState(current);
        return current;
    }

    public static void EnsureCompactSupported(CollectionMaintenancePreflight current, bool force)
    {
        using var topology = System.Text.Json.JsonDocument.Parse(current.TopologyJson);
        var root = topology.RootElement;
        if (root.TryGetProperty("msg", out var message)
            && message.ValueKind == System.Text.Json.JsonValueKind.String
            && string.Equals(message.GetString(), "isdbgrid", StringComparison.Ordinal))
            throw new NotSupportedException("Compactação não está liberada por mongos neste fluxo; conecte-se diretamente a um mongod compatível.");

        var replicaSet = root.TryGetProperty("setName", out var setName)
            && setName.ValueKind == System.Text.Json.JsonValueKind.String;
        var primary = root.TryGetProperty("isWritablePrimary", out var writable)
            && writable.ValueKind == System.Text.Json.JsonValueKind.True;
        if (replicaSet && primary && !force)
            throw new InvalidOperationException("Em primário de replica set, a compactação exige a opção force e confirmação revisada.");
    }

    public static async Task<CollectionMaintenancePreflight> ReadPostflightAsync(
        MongoOperationContext context,
        string database,
        string collection,
        CancellationToken cancellationToken)
    {
        try
        {
            return await CaptureAsync(context, database, collection, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                "O comando foi enviado, mas a releitura posterior falhou; o efeito pode ter ocorrido. Releia o estado antes de continuar.",
                exception);
        }
    }
}
