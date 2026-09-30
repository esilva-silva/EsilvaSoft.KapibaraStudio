using EsilvaSoft.KapibaraStudio.Application;
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
        WorkspaceSessionPersistencePolicy.ValidateDrafts(session);
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
        var filtered = WorkspaceSessionPersistencePolicy.Prepare(session);
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

}
