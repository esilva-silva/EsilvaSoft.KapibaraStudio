using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Infrastructure;

public sealed partial class LiteDbConnectionProfileRepository
{
    public Task<WorkspaceSession> LoadSessionAsync(CancellationToken cancellationToken = default) => RunAsync(ReadSession, cancellationToken);

    private WorkspaceSession ReadSession()
    {
        var document = _database.GetCollection("workspaceSession").FindById("current");
        if (document is null) return new WorkspaceSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<WorkspaceSession>(document["json"].AsString)
            ?? throw new InvalidDataException("Sessão local inválida.");
        if (session.Version is < 1 or > 2) throw new InvalidDataException("Versão da sessão local não suportada.");
        if (session.Version == 1) session = session with { Version = 2 };
        ValidateTextDrafts(session);
        if (session.Preferences?.Autocomplete is not { } autocomplete) throw new InvalidDataException("Preferências locais inválidas.");
        autocomplete.Validate();
        session = session with
        {
            Preferences = session.Preferences with { Language = ApplicationLanguages.Normalize(session.Preferences.Language) }
        };
        session.Preferences.ValidateUuid();
        session.Preferences.ValidateMetadata();
        session.Preferences.ValidateKeyBindings();
        return session;
    }

    public Task SaveSessionAsync(WorkspaceSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.Version != 2) throw new ArgumentException("Versão da sessão local não suportada.", nameof(session));
        ValidateTextDrafts(session);
        session.Preferences.Autocomplete.Validate();
        session.Preferences.ValidateUuid();
        session.Preferences.ValidateMetadata();
        session.Preferences.ValidateKeyBindings();
        // Persist policy at the boundary as well as in the UI. No credentials or results in this DTO.
        var preferences = session.Preferences with { Language = ApplicationLanguages.Normalize(session.Preferences.Language) };
        var normalized = session with { Preferences = preferences };
        var allowed = normalized.Preferences.RecoverDrafts
            ? normalized.Tabs.Where(tab => !tab.ContainsResultData && (tab.ProfileId is null || !normalized.Preferences.ExcludedProfileIds.Contains(tab.ProfileId.Value))).ToArray()
            : [];
        var filtered = normalized with { Tabs = allowed, ActiveTabId = allowed.Any(tab => tab.Id == normalized.ActiveTabId) ? normalized.ActiveTabId : null };
        return RunAsync(() =>
        {
            _ = ReadSession(); // Preserve corrupt or newer snapshots, including autocomplete configuration.
            _database.GetCollection("workspaceSession").Upsert(new LiteDB.BsonDocument
            {
                ["_id"] = "current",
                ["json"] = System.Text.Json.JsonSerializer.Serialize(filtered)
            });
        }, cancellationToken);
    }

    private static void ValidateTextDrafts(WorkspaceSession session)
    {
        if (session.Tabs is null) throw new InvalidDataException("Rascunhos da sessão local inválidos.");
        foreach (var draft in session.Tabs)
        {
            if (draft is null || draft.Text is null || draft.FilePath is null ||
                !Enum.TryParse<Application.TextFileEncoding>(draft.FileEncoding, out var encoding) || !Enum.IsDefined(encoding) ||
                (encoding != Application.TextFileEncoding.Utf8 && !draft.FileHasBom))
                throw new InvalidDataException("Metadados do arquivo na sessão local inválidos.");
            var hasRevision = draft.FileRevisionLength is not null || draft.FileRevisionLastWriteTimeUtc is not null || draft.FileRevisionSha256 is not null;
            if (hasRevision && (draft.FileRevisionLength is null or < 0 || draft.FileRevisionLastWriteTimeUtc is null ||
                draft.FileRevisionSha256 is not { Length: 64 } hash || !hash.All(Uri.IsHexDigit)))
                throw new InvalidDataException("Revisão do arquivo na sessão local inválida.");
        }
    }
}
