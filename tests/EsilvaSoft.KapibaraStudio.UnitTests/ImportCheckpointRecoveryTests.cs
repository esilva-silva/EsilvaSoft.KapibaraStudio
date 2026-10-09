using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class ImportCheckpointRecoveryTests
{
    [Test]
    public void ExplicitRestartRequiresSameSourcePlanProfileAndEmptyTarget()
    {
        var checkpoint = CreateCheckpoint();
        var accepted = Evaluate(checkpoint, targetIsEmpty: true);
        var changedSource = ImportCheckpointRecovery.Evaluate(checkpoint, checkpoint.ProfileId,
            checkpoint.SourceGenerationId, checkpoint.SourcePathSha256, Hash("different"), checkpoint.PlanSha256, true);
        var changedPlan = ImportCheckpointRecovery.Evaluate(checkpoint, checkpoint.ProfileId,
            checkpoint.SourceGenerationId, checkpoint.SourcePathSha256, checkpoint.SourceSha256, Hash("different"), true);
        var changedProfile = ImportCheckpointRecovery.Evaluate(checkpoint, Guid.NewGuid(),
            checkpoint.SourceGenerationId, checkpoint.SourcePathSha256, checkpoint.SourceSha256, checkpoint.PlanSha256, true);
        var occupied = Evaluate(checkpoint, targetIsEmpty: false);

        Assert.Multiple(() =>
        {
            Assert.That(accepted, Is.EqualTo(new ImportRestartDecision(true, "restart-from-beginning")));
            Assert.That(changedSource.ReasonCode, Is.EqualTo("source-changed"));
            Assert.That(changedPlan.ReasonCode, Is.EqualTo("plan-changed"));
            Assert.That(changedProfile.ReasonCode, Is.EqualTo("profile-changed"));
            Assert.That(occupied.ReasonCode, Is.EqualTo("target-not-empty"));
        });
    }

    [Test]
    public void InvalidReceiptIsRejectedBeforeRecovery()
    {
        var checkpoint = CreateCheckpoint() with { SourceSha256 = "invalid" };
        Assert.That(() => Evaluate(checkpoint, true), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void CounterSumCannotOverflowPastProcessedDocuments()
    {
        var checkpoint = CreateCheckpoint() with
        {
            ProcessedDocuments = long.MaxValue,
            TotalDocuments = long.MaxValue,
            InsertedDocuments = long.MaxValue,
            ModifiedDocuments = 1
        };

        Assert.That(() => checkpoint.Validate(), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void RestartRejectsDifferentKindOrDestinationEvenWhenHashesMatch()
    {
        var checkpoint = CreateCheckpoint();
        ImportRestartDecision Check(ImportCheckpointKind kind, string database, string? collection) =>
            ImportCheckpointRecovery.EvaluateBound(checkpoint, kind, database, collection,
                checkpoint.ProfileId, checkpoint.SourceGenerationId, checkpoint.SourcePathSha256,
                checkpoint.SourceSha256, checkpoint.PlanSha256, true);

        Assert.Multiple(() =>
        {
            Assert.That(Check(ImportCheckpointKind.LogicalPackage, "target", "items").ReasonCode,
                Is.EqualTo("kind-changed"));
            Assert.That(Check(ImportCheckpointKind.StandaloneFile, "other", "items").ReasonCode,
                Is.EqualTo("destination-changed"));
            Assert.That(Check(ImportCheckpointKind.StandaloneFile, "target", "other").ReasonCode,
                Is.EqualTo("destination-changed"));
        });
    }

    internal static ImportCheckpoint CreateCheckpoint() => new(
        ImportCheckpoint.CurrentVersion, Guid.NewGuid(), ImportCheckpointKind.StandaloneFile,
        Guid.NewGuid(), Guid.NewGuid(), Hash("path"), Hash("source"), Hash("plan"),
        "target", "items", 2, 10, 2, 0, 0, ImportCheckpointState.Writing, DateTimeOffset.UtcNow);

    private static string Hash(string value) => ImportCheckpointRecovery.Sha256OfText(value);

    private static ImportRestartDecision Evaluate(ImportCheckpoint checkpoint, bool targetIsEmpty) =>
        ImportCheckpointRecovery.Evaluate(checkpoint, checkpoint.ProfileId, checkpoint.SourceGenerationId,
            checkpoint.SourcePathSha256, checkpoint.SourceSha256, checkpoint.PlanSha256, targetIsEmpty);
}
