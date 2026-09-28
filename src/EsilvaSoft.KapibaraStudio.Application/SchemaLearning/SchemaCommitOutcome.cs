namespace EsilvaSoft.KapibaraStudio.Application.SchemaLearning;

/// <summary>Result of applying one delta; persistence failure is visible, never silently durable.</summary>
public enum SchemaCommitOutcome
{
    /// <summary>The increment was applied and the batch recorded in the same transaction.</summary>
    Applied = 0,
    /// <summary>The batch id was already committed; a retry must not increment twice.</summary>
    AlreadyCommitted,
    /// <summary>The source generation advanced: previous totals were discarded and rebuilt in the same transaction.</summary>
    RolledOver,
    /// <summary>Persistence is off or the namespace is isolated; the delta stays transient and nothing was written.</summary>
    NotPersisted,
    /// <summary>The write failed; the caller must surface a not saved state and never claim durability.</summary>
    Failed
}
