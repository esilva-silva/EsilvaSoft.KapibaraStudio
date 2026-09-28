using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Completion;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

/// <summary>
/// Resolves a saved profile only while a catalog query is being assembled. Completion contexts retain only a
/// <c>ConnectionIdentity</c>, never a URI or credentials.
/// </summary>
public sealed class WorkspaceCompletionProfileResolver(Func<IEnumerable<ConnectionProfile>> profiles) : ICompletionProfileResolver
{
    private readonly Func<IEnumerable<ConnectionProfile>> _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));

    public ConnectionProfile? Resolve(Guid profileId) => _profiles().FirstOrDefault(profile => profile.Id == profileId);
}
