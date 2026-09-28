namespace EsilvaSoft.KapibaraStudio.Application.SchemaLearning;

/// <summary>Outcome of one delta commit, with the revision to publish when it succeeded.</summary>
/// <param name="Outcome">What the owner did with the delta.</param>
/// <param name="Revision">Revision after the commit; 0 when nothing was written.</param>
/// <param name="Detail">Human readable detail for the status area; never carries values or connection data.</param>
public readonly record struct SchemaCommitResult(SchemaCommitOutcome Outcome, long Revision, string? Detail)
{
    /// <summary>True when the stored state changed.</summary>
    public bool IsPersisted => Outcome is SchemaCommitOutcome.Applied or SchemaCommitOutcome.RolledOver;
}
