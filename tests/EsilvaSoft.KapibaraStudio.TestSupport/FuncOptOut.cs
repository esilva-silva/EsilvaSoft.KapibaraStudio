using EsilvaSoft.KapibaraStudio.Application.SchemaLearning;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Testing;

/// <summary>Delegate-backed <see cref="ILearnedSchemaOptOut"/> for tests, avoiding a one-off mock per case.</summary>
internal sealed class FuncOptOut(Func<Guid, bool> allowed) : ILearnedSchemaOptOut
{
    public bool IsServingAllowed(Guid profileId) => allowed(profileId);
    public IReadOnlyCollection<Guid> ExcludedProfiles => [];
    public void ApplyPreferences(WorkspacePreferences preferences) => throw new NotSupportedException();
    public void SetServingExcluded(Guid profileId, bool excluded) => throw new NotSupportedException();
}
