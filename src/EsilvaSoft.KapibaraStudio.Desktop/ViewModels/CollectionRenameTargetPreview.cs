using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

internal sealed record CollectionRenameTargetPreview(
    ConnectionProfile Profile,
    string Database,
    string Source,
    string Target,
    string DefinitionJson)
{
    public bool MatchesContext(ConnectionProfile? profile, string? database, string? source, string? target) =>
        ReferenceEquals(profile, Profile)
        && string.Equals(database, Database, StringComparison.Ordinal)
        && string.Equals(source, Source, StringComparison.Ordinal)
        && string.Equals(target, Target, StringComparison.Ordinal);

    public bool MatchesObservedDefinition(string definitionJson) =>
        !string.IsNullOrWhiteSpace(DefinitionJson)
        && DefinitionJson != "{}"
        && string.Equals(definitionJson, DefinitionJson, StringComparison.Ordinal);
}
