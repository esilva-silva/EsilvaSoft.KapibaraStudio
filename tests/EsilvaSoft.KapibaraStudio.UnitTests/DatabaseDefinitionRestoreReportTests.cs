using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class DatabaseDefinitionRestoreReportTests
{
    private static readonly string[] NoDependencies = [];
    private static readonly string[] CollectionDependency = ["collection:movies"];

    [Test]
    public void ValidateAcceptsRestoredOmittedAndBlockedItemsWithCollisionAndDependencies()
    {
        var report = new DatabaseDefinitionRestoreReport(
        [
            new DatabaseDefinitionRestoreItem(
                "collection:movies", DatabaseDefinitionKind.CollectionOptions, "movies", NoDependencies,
                DatabaseDefinitionCollision.None, DatabaseDefinitionRestoreStatus.Restored),
            new DatabaseDefinitionRestoreItem(
                "index:movies:title_1", DatabaseDefinitionKind.Index, "movies/title_1", CollectionDependency,
                DatabaseDefinitionCollision.Identical, DatabaseDefinitionRestoreStatus.Omitted),
            new DatabaseDefinitionRestoreItem(
                "view:popular", DatabaseDefinitionKind.View, "popular", CollectionDependency,
                DatabaseDefinitionCollision.Conflicting, DatabaseDefinitionRestoreStatus.Blocked,
                "DependencyConflict", "A dependência não foi restaurada.")
        ]);

        Assert.That(report.Validate(), Is.EqualTo(report));
    }

    [Test]
    public void ValidateRejectsMissingDependencyAndBlockedItemWithoutFailureCode()
    {
        var missingDependency = new DatabaseDefinitionRestoreReport(
        [
            new DatabaseDefinitionRestoreItem(
                "view:popular", DatabaseDefinitionKind.View, "popular", ["unknown"],
                DatabaseDefinitionCollision.None, DatabaseDefinitionRestoreStatus.Blocked, "DependencyMissing")
        ]);
        var missingErrorCode = new DatabaseDefinitionRestoreReport(
        [
            new DatabaseDefinitionRestoreItem(
                "view:popular", DatabaseDefinitionKind.View, "popular", NoDependencies,
                DatabaseDefinitionCollision.None, DatabaseDefinitionRestoreStatus.Failed)
        ]);

        Assert.That(() => missingDependency.Validate(), Throws.TypeOf<ArgumentException>());
        Assert.That(() => missingErrorCode.Validate(), Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void ValidateRejectsDependencyCycles()
    {
        var cycle = new DatabaseDefinitionRestoreReport(
        [
            new DatabaseDefinitionRestoreItem(
                "one", DatabaseDefinitionKind.View, "one", ["two"], DatabaseDefinitionCollision.None,
                DatabaseDefinitionRestoreStatus.Failed, "ServerError"),
            new DatabaseDefinitionRestoreItem(
                "two", DatabaseDefinitionKind.View, "two", ["one"], DatabaseDefinitionCollision.None,
                DatabaseDefinitionRestoreStatus.Failed, "ServerError")
        ]);
        Assert.That(() => cycle.Validate(), Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void ValidateRejectsDuplicateIdsAndErrorCodeOnSuccessfulItem()
    {
        var duplicateIds = new DatabaseDefinitionRestoreReport(
        [
            new DatabaseDefinitionRestoreItem(
                "same", DatabaseDefinitionKind.Index, "movies/a", NoDependencies,
                DatabaseDefinitionCollision.None, DatabaseDefinitionRestoreStatus.Restored),
            new DatabaseDefinitionRestoreItem(
                "same", DatabaseDefinitionKind.Index, "movies/b", NoDependencies,
                DatabaseDefinitionCollision.None, DatabaseDefinitionRestoreStatus.Restored)
        ]);
        var successfulWithError = new DatabaseDefinitionRestoreReport(
        [
            new DatabaseDefinitionRestoreItem(
                "index:movies:title_1", DatabaseDefinitionKind.Index, "movies/title_1", NoDependencies,
                DatabaseDefinitionCollision.None, DatabaseDefinitionRestoreStatus.Restored, "Unexpected")
        ]);

        Assert.That(() => duplicateIds.Validate(), Throws.TypeOf<ArgumentException>());
        Assert.That(() => successfulWithError.Validate(), Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void ValidateRejectsInvalidTargetAndDependencyIdentifiers()
    {
        var invalidTarget = new DatabaseDefinitionRestoreReport(
        [
            new DatabaseDefinitionRestoreItem(
                "index:movies:title_1", DatabaseDefinitionKind.Index, "system.views", NoDependencies,
                DatabaseDefinitionCollision.None, DatabaseDefinitionRestoreStatus.Restored)
        ]);
        var invalidDependency = new DatabaseDefinitionRestoreReport(
        [
            new DatabaseDefinitionRestoreItem(
                "view:popular", DatabaseDefinitionKind.View, "popular", ["bad dependency"],
                DatabaseDefinitionCollision.None, DatabaseDefinitionRestoreStatus.Blocked, "DependencyMissing")
        ]);
        var dottedTarget = new DatabaseDefinitionRestoreReport(
        [
            new DatabaseDefinitionRestoreItem(
                "collection:movie_archive", DatabaseDefinitionKind.CollectionOptions, "movie.archive", NoDependencies,
                DatabaseDefinitionCollision.None, DatabaseDefinitionRestoreStatus.Restored)
        ]);

        Assert.That(() => invalidTarget.Validate(), Throws.TypeOf<ArgumentException>());
        Assert.That(() => invalidDependency.Validate(), Throws.TypeOf<ArgumentException>());
        Assert.That(() => dottedTarget.Validate(), Throws.TypeOf<ArgumentException>());
    }
}
