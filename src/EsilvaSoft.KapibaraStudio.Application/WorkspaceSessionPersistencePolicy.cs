using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Application;

/// <summary>Pure validation and privacy policy applied before handing a snapshot to persistence.</summary>
public static class WorkspaceSessionPersistencePolicy
{
    public static WorkspaceSession Prepare(WorkspaceSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.Version != 2) throw new ArgumentException("Versão da sessão local não suportada.", nameof(session));
        ValidateDrafts(session);
        if (session.Preferences?.Autocomplete is not { } autocomplete) throw new InvalidDataException("Preferências locais inválidas.");
        autocomplete.Validate();
        session.Preferences.ValidateUuid();
        session.Preferences.ValidateMetadata();
        session.Preferences.ValidateKeyBindings();
        var preferences = session.Preferences with { Language = ApplicationLanguages.Normalize(session.Preferences.Language) };
        var allowed = preferences.RecoverDrafts
            ? session.Tabs.Where(tab => !tab.ContainsResultData && (tab.ProfileId is null || !preferences.ExcludedProfileIds.Contains(tab.ProfileId.Value))).ToArray()
            : [];
        return session with
        {
            Preferences = preferences,
            Tabs = allowed,
            ActiveTabId = allowed.Any(tab => tab.Id == session.ActiveTabId) ? session.ActiveTabId : null
        };
    }

    public static void ValidateDrafts(WorkspaceSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.Tabs is null) throw new InvalidDataException("Rascunhos da sessão local inválidos.");
        foreach (var draft in session.Tabs)
        {
            if (draft is null || draft.Text is null || draft.FilePath is null ||
                !Enum.TryParse<TextFileEncoding>(draft.FileEncoding, out var encoding) || !Enum.IsDefined(encoding) ||
                (encoding != TextFileEncoding.Utf8 && !draft.FileHasBom))
                throw new InvalidDataException("Metadados do arquivo na sessão local inválidos.");
            var hasRevision = draft.FileRevisionLength is not null || draft.FileRevisionLastWriteTimeUtc is not null || draft.FileRevisionSha256 is not null;
            if (hasRevision && (draft.FileRevisionLength is null or < 0 || draft.FileRevisionLastWriteTimeUtc is null ||
                draft.FileRevisionSha256 is not { Length: 64 } hash || !hash.All(Uri.IsHexDigit)))
                throw new InvalidDataException("Revisão do arquivo na sessão local inválida.");
        }
    }
}
