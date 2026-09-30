using EsilvaSoft.KapibaraStudio.Application.SchemaLearning;

namespace EsilvaSoft.KapibaraStudio.Infrastructure;

/// <summary>
/// L15 addition, kept in its own file so the L14 facet (<c>LiteDbConnectionProfileRepository.SchemaLearning.cs</c>)
/// stays untouched. Explicitly implements the additive <see cref="ILearnedSchemaRepository.ReadAvailabilityAsync"/>
/// default-interface member by delegating to <see cref="ReadLearnedSchemaAsync"/>, which already computes the
/// richer three-way availability (<see cref="LearnedSchemaAvailability"/>) internally — this facet only maps it
/// onto the Application-layer shape so <c>LearnedSchemaCatalogSource</c> can tell "never learned" apart from
/// "corrupt/future format" without depending on <c>EsilvaSoft.KapibaraStudio.Infrastructure</c>.
/// </summary>
public sealed partial class LiteDbConnectionProfileRepository
{
    /// <inheritdoc />
    public async Task<LearnedSchemaHydrationResult> ReadAvailabilityAsync(LearnedSchemaKey key, CancellationToken cancellationToken)
    {
        var result = await ReadLearnedSchemaAsync(key, cancellationToken).ConfigureAwait(false);
        var state = result.Availability switch
        {
            LearnedSchemaAvailability.Available => LearnedSchemaHydrationState.Available,
            LearnedSchemaAvailability.Unavailable => LearnedSchemaHydrationState.Unavailable,
            _ => LearnedSchemaHydrationState.NotLearned
        };
        return new LearnedSchemaHydrationResult(state, result.Snapshot, result.Detail);
    }
}
