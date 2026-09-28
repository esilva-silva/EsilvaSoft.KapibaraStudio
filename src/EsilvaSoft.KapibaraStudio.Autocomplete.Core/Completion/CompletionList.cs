using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Text;

namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core.Completion;

public sealed record CompletionList(TextSnapshotVersion Version, IReadOnlyList<CompletionItem> Items, bool IsIncomplete)
{
    public static CompletionList Empty(TextSnapshotVersion version) => new(version, [], false);
}
