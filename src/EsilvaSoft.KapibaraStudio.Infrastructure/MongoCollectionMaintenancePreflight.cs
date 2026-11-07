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
