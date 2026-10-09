using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class CollectionMaintenancePreflightTests
{
    [Test]
    public void FingerprintIgnoresObservationTimeButIncludesObservedState()
    {
        var first = CollectionMaintenanceTestData.Preflight();
        var later = first with { ObservedAt = first.ObservedAt.AddMinutes(1) };
        var changed = first with { StatisticsJson = "{\"count\":2}" };

        Assert.Multiple(() =>
        {
            Assert.That(first.MatchesObservedState(later), Is.True);
            Assert.That(first.MatchesObservedState(changed), Is.False);
        });
    }

    [Test]
    public void FingerprintIgnoresVolatileHelloLocalTimeButIncludesTopologyChanges()
    {
        var first = CollectionMaintenanceTestData.Preflight() with
        {
            TopologyJson = "{\"isWritablePrimary\":true,\"localTime\":{\"$date\":\"2026-10-08T12:00:00Z\"}}"
        };
        var later = first with
        {
            TopologyJson = "{\"isWritablePrimary\":true,\"localTime\":{\"$date\":\"2026-10-08T12:00:01Z\"}}"
        };
        var changed = first with
        {
            TopologyJson = "{\"isWritablePrimary\":false,\"localTime\":{\"$date\":\"2026-10-08T12:00:01Z\"}}"
        };

        Assert.Multiple(() =>
        {
            Assert.That(first.MatchesObservedState(later), Is.True);
            Assert.That(first.MatchesObservedState(changed), Is.False);
        });
    }

    [Test]
    public void StaleSnapshotIsRejectedBeforeOperationCanBeDispatched()
    {
        var preview = CollectionMaintenanceTestData.Preflight();
        var stale = preview with { DefinitionJson = "{\"type\":\"collection\",\"options\":{\"capped\":true}}" };

        Assert.That(() => preview.EnsureMatchesObservedState(stale),
            Throws.TypeOf<InvalidOperationException>().With.Message.Contain("mudou desde a prévia"));
    }

    [Test]
    public void RequestRejectsMissingSnapshotAndMismatchedTarget()
    {
        var withoutPreview = new CollectionIntegrityCheckRequest("catalogo", "clientes", "clientes");
        var wrongTarget = new CollectionCompactRequest("catalogo", "clientes", "clientes", Preflight: CollectionMaintenanceTestData.Preflight("catalogo", "pedidos"));
        var validationWithoutPreview = new CollectionValidationRequest("catalogo", "clientes", "{}",
            CollectionValidationLevel.Strict, CollectionValidationAction.Error, "clientes");

        Assert.Multiple(() =>
        {
            Assert.That(() => withoutPreview.Validate(), Throws.ArgumentException.With.Message.Contain("prévia obrigatória"));
            Assert.That(() => validationWithoutPreview.Validate(), Throws.ArgumentException.With.Message.Contain("prévia obrigatória"));
            Assert.That(() => wrongTarget.Validate(), Throws.ArgumentException.With.Message.Contain("não corresponde ao alvo"));
        });
    }

    [Test]
    public void SnapshotRejectsUnknownSourceAndViewDefinition()
    {
        var unknownSource = CollectionMaintenanceTestData.Preflight() with { Source = "unknown" };
        var viewDefinition = CollectionMaintenanceTestData.Preflight() with { DefinitionJson = "{\"type\":\"view\"}" };

        Assert.Multiple(() =>
        {
            Assert.That(() => unknownSource.Validate("catalogo", "clientes"), Throws.ArgumentException.With.Message.Contain("origem"));
            Assert.That(() => viewDefinition.Validate("catalogo", "clientes"), Throws.ArgumentException.With.Message.Contain("coleção regular"));
        });
    }
}

internal static class CollectionMaintenanceTestData
{
    public static CollectionMaintenancePreflight Preflight(string database = "catalogo", string collection = "clientes") =>
        new(database, collection,
            "{\"isWritablePrimary\":true}",
            "{\"type\":\"collection\",\"options\":{}}",
            "{\"count\":1,\"storageSize\":64}",
            new CollectionValidationInfo("{}", CollectionValidationLevel.Strict, CollectionValidationAction.Error),
            CollectionMaintenancePreflight.ExpectedSource,
            DateTimeOffset.UtcNow);
}
