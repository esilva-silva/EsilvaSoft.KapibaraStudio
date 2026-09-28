using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.SchemaLearning;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Infrastructure;

/// <summary>
/// Composition-layer implementation of <see cref="ILearnedSchemaOptOut"/> (L15), reading the two opt-out flags
/// already delivered instead of inventing a third configuration mechanism:
/// <list type="bullet">
/// <item><description>the general switch is <see cref="AutocompleteSettings.LearnedSchemaEnabled"/>, read live from
/// <see cref="IAutocompleteService.Settings"/> — the singleton that already owns the effective autocomplete
/// settings and is refreshed by the preferences window through <c>ConfigureAsync</c>, so turning the feature off
/// takes effect on the very next <c>Collect</c>, with no restart and no cached copy of its own;</description></item>
/// <item><description>the per-connection exclusion is <see cref="WorkspacePreferences.LearnedSchemaExcludedProfileIds"/>,
/// pushed in by <see cref="ApplyPreferences"/> when the session is loaded and by <see cref="SetServingExcluded"/>
/// when the user toggles one connection — exactly the convention <c>IMetadataCache.SetSchemaSamplingAllowed</c> /
/// <c>SchemaSamplingProfiles</c> already uses for the twin preference of Fase 1. The persisted list is not read
/// from LiteDB here on purpose: <see cref="IsServingAllowed"/> runs on the autocomplete path and must stay
/// in-memory and allocation-free (the interface's own contract is "purely in memory").</description></item>
/// </list>
/// <para>This lives in Infrastructure, next to the composition root, because it is precisely the glue between
/// configuration already resolved by DI and an Application-level port; <c>Application/SchemaLearning</c> declares
/// the port and stays free of any knowledge of where settings come from.</para>
/// <para><b>Thread safety.</b> Copy-on-write: the exclusion set is replaced, never mutated after publication, so
/// readers on the catalog thread never lock and never observe a half-updated set.</para>
/// </summary>
public sealed class LearnedSchemaOptOut : ILearnedSchemaOptOut
{
    private readonly IAutocompleteService _autocomplete;
    private readonly object _gate = new();
    private volatile HashSet<Guid> _excluded = [];

    public LearnedSchemaOptOut(IAutocompleteService autocomplete)
    {
        _autocomplete = autocomplete ?? throw new ArgumentNullException(nameof(autocomplete));
    }

    /// <summary>Profiles currently excluded from being served learned schema; a snapshot, safe to enumerate.</summary>
    public IReadOnlyCollection<Guid> ExcludedProfiles => _excluded;

    /// <summary>True when the general flag is on and <paramref name="profileId"/> was not excluded.</summary>
    public bool IsServingAllowed(Guid profileId) =>
        _autocomplete.Settings.LearnedSchemaEnabled && !_excluded.Contains(profileId);

    /// <summary>Replaces the exclusion list with the one persisted in the session (absent list excludes nobody).</summary>
    public void ApplyPreferences(WorkspacePreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        var excluded = preferences.LearnedSchemaExcludedProfileIds is { Length: > 0 } ids
            ? new HashSet<Guid>(ids.Where(id => id != Guid.Empty))
            : [];
        lock (_gate) _excluded = excluded;
    }

    /// <summary>Excludes or restores one connection; <see cref="Guid.Empty"/> is never a valid profile.</summary>
    public void SetServingExcluded(Guid profileId, bool excluded)
    {
        if (profileId == Guid.Empty) return;
        lock (_gate)
        {
            if (_excluded.Contains(profileId) == excluded) return;
            var updated = new HashSet<Guid>(_excluded);
            if (excluded) updated.Add(profileId); else updated.Remove(profileId);
            _excluded = updated;
        }
    }
}
