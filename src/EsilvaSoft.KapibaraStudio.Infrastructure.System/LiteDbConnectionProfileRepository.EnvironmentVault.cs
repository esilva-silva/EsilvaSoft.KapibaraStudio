using EsilvaSoft.KapibaraStudio.Core;
using LiteDB;

namespace EsilvaSoft.KapibaraStudio.Infrastructure;

public sealed partial class LiteDbConnectionProfileRepository
{
    private string? _environmentJson;
    private Exception? _environmentReadError;

    public EnvironmentVault LoadEnvironments()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_environmentReadError is { } error) throw new InvalidDataException("Cofre de ambientes ilegível.", error);
        var json = Volatile.Read(ref _environmentJson);
        if (json is null) return EnvironmentVault.CreateDefault();
        // Return an independent form/snapshot; editing a dialog must not change the active environment.
        var vault = System.Text.Json.JsonSerializer.Deserialize<EnvironmentVault>(json)
            ?? throw new InvalidDataException("Cofre de ambientes ilegível.");
        vault.Validate();
        return vault;
    }

    public void SaveEnvironments(EnvironmentVault vault)
    {
        vault.Validate();
        var json = System.Text.Json.JsonSerializer.Serialize(vault);
        lock (_gate)
        {
            _ = LoadEnvironments(); // Do not overwrite unreadable/newer data.
            _database.GetCollection("environmentVault").Upsert(new BsonDocument
            {
                ["_id"] = "current", ["json"] = json
            });
            Volatile.Write(ref _environmentJson, json);
        }
    }
}
