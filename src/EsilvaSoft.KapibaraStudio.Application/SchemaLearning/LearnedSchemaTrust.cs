namespace EsilvaSoft.KapibaraStudio.Application.SchemaLearning;

/// <summary>Whether the observations still describe the profile's current source, or an origin it has since left behind.</summary>
public enum LearnedSchemaOrigin
{
    /// <summary>The observed generation matches the profile's current generation, or neither is known yet.</summary>
    Current,

    /// <summary>The profile's generation advanced since the last observation: the structure describes another origin.</summary>
    Superseded
}

/// <summary>Whether this process has already connected successfully to the profile under its current generation.</summary>
public enum LearnedSchemaSessionState
{
    /// <summary>Nobody in this process confirmed the current origin yet (fresh start, offline, still connecting).</summary>
    Unconfirmed,

    /// <summary>This session connected successfully to the profile under the generation being served.</summary>
    Confirmed
}

/// <summary>
/// Derived trust of a learned snapshot (DEC-L-TRUST). Never persisted: computed at hydration time from the one
/// durable column (<see cref="LearnedSchemaSnapshot.LastObservedGenerationId"/>) and from whether this session
/// already confirmed the profile's current origin. Two independent axes, three servable states:
/// <list type="bullet">
/// <item><description><see cref="LearnedSchemaOrigin.Current"/> + <see cref="LearnedSchemaSessionState.Confirmed"/> —
/// served as live learned evidence.</description></item>
/// <item><description><see cref="LearnedSchemaOrigin.Current"/> + <see cref="LearnedSchemaSessionState.Unconfirmed"/> —
/// served as marked historical evidence, without claiming coverage or an active connection.</description></item>
/// <item><description><see cref="LearnedSchemaOrigin.Superseded"/> — never served, the snapshot stays on disk
/// untouched until the next commit under the new generation rolls it over.</description></item>
/// </list>
/// </summary>
public readonly record struct LearnedSchemaTrust(LearnedSchemaOrigin Origin, LearnedSchemaSessionState Session)
{
    /// <summary>True for the two <see cref="LearnedSchemaOrigin.Current"/> states; false for <see cref="LearnedSchemaOrigin.Superseded"/>.</summary>
    public bool IsServable => Origin == LearnedSchemaOrigin.Current;

    /// <summary>
    /// True only for the marked-historical state: origin not contradicted, but nobody in this session confirmed it
    /// yet. A caller uses this to apply <c>SymbolTraits.Stale</c> and the "observações de documentos analisadas"
    /// wording — never "total de documentos da coleção" — instead of presenting it as live coverage.
    /// </summary>
    public bool IsHistorical => Origin == LearnedSchemaOrigin.Current && Session == LearnedSchemaSessionState.Unconfirmed;

    /// <summary>
    /// Computes trust for one snapshot. <paramref name="lastObservedGenerationId"/> is the namespace's stored,
    /// non-key column; <paramref name="currentGenerationId"/> is the profile's live <c>SourceGenerationId</c>
    /// (DEC-L-GENERATION); <paramref name="sessionConfirmed"/> is supplied by the caller — this type never tracks
    /// connection state on its own, by design (there is already a session-scoped signal for "connected successfully
    /// under the live identity" in <c>IMetadataCache.IsConnected</c>, and duplicating it here would only risk the
    /// two disagreeing).
    /// <para>
    /// Absence on either side (a namespace observed before generations existed, or a profile never persisted with
    /// one) is treated as "not contradicted", i.e. <see cref="LearnedSchemaOrigin.Current"/>: a null cannot prove a
    /// different origin, and treating unknown as Superseded would silently discard every namespace learned before
    /// L14-a shipped, which is strictly worse than serving it marked historical until a real edit of the origin
    /// produces a first non-null generation to compare against.
    /// </para>
    /// </summary>
    public static LearnedSchemaTrust Compute(Guid? lastObservedGenerationId, Guid? currentGenerationId, bool sessionConfirmed)
    {
        var contradicted = lastObservedGenerationId is { } observed && currentGenerationId is { } current && observed != current;
        var origin = contradicted ? LearnedSchemaOrigin.Superseded : LearnedSchemaOrigin.Current;
        var session = sessionConfirmed ? LearnedSchemaSessionState.Confirmed : LearnedSchemaSessionState.Unconfirmed;
        return new LearnedSchemaTrust(origin, session);
    }
}
