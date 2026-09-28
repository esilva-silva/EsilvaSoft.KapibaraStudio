namespace EsilvaSoft.KapibaraStudio.Core;

/// <summary>Origin captured with the execution. <paramref name="Profile"/> is an in-memory snapshot and never enters session snapshots.</summary>
public sealed record ResultOrigin(string Source, Guid? ProfileId, ConnectionProfile? Profile, string? Database, string? Collection)
{
    public static ResultOrigin Unknown { get; } = new("Resultado", null, null, null, null);

    public bool HasCollection => Profile is not null && !string.IsNullOrWhiteSpace(Database) && !string.IsNullOrWhiteSpace(Collection);

    public string Destination => (Profile?.Name ?? "Conexão desconhecida") + " › " + (string.IsNullOrWhiteSpace(Database) ? "banco desconhecido" : Database)
        + (string.IsNullOrWhiteSpace(Collection) ? "" : " › " + Collection);
}
