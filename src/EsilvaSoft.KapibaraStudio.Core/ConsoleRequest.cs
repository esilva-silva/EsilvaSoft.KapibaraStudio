namespace EsilvaSoft.KapibaraStudio.Core;

public sealed record ConsoleRequest(ConnectionProfile Primary, string Database, string Script,
    int DocumentLimit = 100, int TimeoutMs = 30000, bool SaveHistory = true);
