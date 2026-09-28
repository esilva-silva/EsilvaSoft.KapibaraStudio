namespace EsilvaSoft.KapibaraStudio.Core;

public sealed record WorkspaceDraft
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid? ProfileId { get; init; }
    public string? TargetHost { get; init; }
    public bool ContainsResultData { get; init; }
    public string Database { get; init; } = "";
    public string Collection { get; init; } = "";
    public string Mode { get; init; } = "Script";
    public string Text { get; init; } = "";
    public string? InputJson { get; init; }
    public string FilePath { get; init; } = "";
    public string FileEncoding { get; init; } = "Utf8";
    public bool FileHasBom { get; init; }
    public long? FileRevisionLength { get; init; }
    public DateTime? FileRevisionLastWriteTimeUtc { get; init; }
    public string? FileRevisionSha256 { get; init; }
    public string? SavedText { get; init; }
    public bool IsDirty { get; init; }
    public string Projection { get; init; } = "";
    public string Sort { get; init; } = "";
    public int Limit { get; init; } = 100;
    public int Skip { get; init; }
    public string Hint { get; init; } = "";
    public string Comment { get; init; } = "";
    public string Collation { get; init; } = "";
    public int BatchSize { get; init; } = 100;
    public int MaxTimeMs { get; init; } = 30000;
    public bool HistoryEnabled { get; init; }
    public bool ScriptHistoryEnabled { get; init; } = true;
}
