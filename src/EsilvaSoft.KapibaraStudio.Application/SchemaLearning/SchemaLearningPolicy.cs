namespace EsilvaSoft.KapibaraStudio.Application.SchemaLearning;

/// <summary>
/// Learning policy captured with the execution. Turning collection off stops new observations; turning
/// persistence off keeps transient learning only. Neither deletes anything already stored: that is the
/// explicit "Limpar aprendizado" command (DEC-L-RETENTION).
/// </summary>
/// <param name="CollectionEnabled">Whether new observations may be collected for this profile.</param>
/// <param name="PersistenceEnabled">Whether an accepted delta may be committed to the local owner.</param>
public readonly record struct SchemaLearningPolicy(bool CollectionEnabled, bool PersistenceEnabled)
{
    /// <summary>Default for this feature: collecting and persisting.</summary>
    public static SchemaLearningPolicy Default { get; } = new(true, true);

    /// <summary>Collection allowed, persistence off: learning stays in memory for the session.</summary>
    public static SchemaLearningPolicy TransientOnly { get; } = new(true, false);

    /// <summary>No collection at all.</summary>
    public static SchemaLearningPolicy Disabled { get; } = new(false, false);
}
