using System.Security.Cryptography;
using System.Text;

namespace EsilvaSoft.KapibaraStudio.Core;

public enum ImportCheckpointKind { LogicalPackage, StandaloneFile }
public enum ImportCheckpointState { Prepared, Writing, NeedsReview }

/// <summary>Durable import receipt without source data, credentials, URI or raw file path.</summary>
public sealed record ImportCheckpoint(
    int Version,
    Guid Id,
    ImportCheckpointKind Kind,
    Guid ProfileId,
    Guid? SourceGenerationId,
    string SourcePathSha256,
    string SourceSha256,
    string PlanSha256,
    string TargetDatabase,
    string? TargetCollection,
    long ProcessedDocuments,
    long TotalDocuments,
    long InsertedDocuments,
    long ModifiedDocuments,
    long IgnoredDocuments,
    ImportCheckpointState State,
    DateTimeOffset UpdatedAtUtc)
{
    public const int CurrentVersion = 1;

    public ImportCheckpoint Validate()
    {
        if (Version != CurrentVersion || Id == Guid.Empty || ProfileId == Guid.Empty
            || !Enum.IsDefined(Kind) || !Enum.IsDefined(State)
            || !ValidHash(SourcePathSha256) || !ValidHash(SourceSha256) || !ValidHash(PlanSha256)
            || string.IsNullOrWhiteSpace(TargetDatabase) || TargetDatabase.Contains('\0')
            || (Kind == ImportCheckpointKind.StandaloneFile) != !string.IsNullOrWhiteSpace(TargetCollection)
            || TargetCollection?.Contains('\0') == true
            || ProcessedDocuments < 0 || TotalDocuments < 0 || ProcessedDocuments > TotalDocuments
            || InsertedDocuments < 0 || ModifiedDocuments < 0 || IgnoredDocuments < 0
            || (decimal)InsertedDocuments + ModifiedDocuments + IgnoredDocuments > ProcessedDocuments
            || State == ImportCheckpointState.Prepared && ProcessedDocuments != 0
            || UpdatedAtUtc == default)
            throw new InvalidDataException("Checkpoint de importação inválido ou incompatível.");
        return this;
    }

    private static bool ValidHash(string? value) => value is { Length: 64 }
        && value.All(Uri.IsHexDigit);
}

public sealed record ImportRestartDecision(bool CanRestartFromBeginning, string ReasonCode);

/// <summary>Only authorizes an explicit restart from zero, never skipping already reported batches.</summary>
public static class ImportCheckpointRecovery
{
    public static string Sha256OfText(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public static ImportRestartDecision Evaluate(
        ImportCheckpoint checkpoint,
        Guid profileId,
        Guid? sourceGenerationId,
        string sourcePathSha256,
        string sourceSha256,
        string planSha256,
        bool targetIsEmpty)
    {
        checkpoint.Validate();
        if (checkpoint.ProfileId != profileId || checkpoint.SourceGenerationId != sourceGenerationId)
            return new(false, "profile-changed");
        if (!string.Equals(checkpoint.SourcePathSha256, sourcePathSha256, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(checkpoint.SourceSha256, sourceSha256, StringComparison.OrdinalIgnoreCase))
            return new(false, "source-changed");
        if (!string.Equals(checkpoint.PlanSha256, planSha256, StringComparison.OrdinalIgnoreCase))
            return new(false, "plan-changed");
        if (!targetIsEmpty)
            return new(false, "target-not-empty");
        return new(true, "restart-from-beginning");
    }

    public static ImportRestartDecision EvaluateBound(
        ImportCheckpoint checkpoint, ImportCheckpointKind kind, string targetDatabase, string? targetCollection,
        Guid profileId, Guid? sourceGenerationId, string sourcePathSha256, string sourceSha256,
        string planSha256, bool targetIsEmpty)
    {
        checkpoint.Validate();
        if (checkpoint.Kind != kind) return new(false, "kind-changed");
        if (!string.Equals(checkpoint.TargetDatabase, targetDatabase, StringComparison.Ordinal)
            || !string.Equals(checkpoint.TargetCollection, targetCollection, StringComparison.Ordinal))
            return new(false, "destination-changed");
        return Evaluate(checkpoint, profileId, sourceGenerationId, sourcePathSha256, sourceSha256,
            planSha256, targetIsEmpty);
    }
}
