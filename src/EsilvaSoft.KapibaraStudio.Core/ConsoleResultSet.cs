namespace EsilvaSoft.KapibaraStudio.Core;

public sealed record ConsoleResultSet(int Number, string Json, Guid? ProfileId = null, string? Database = null,
    string? Collection = null, IReadOnlyList<string>? Documents = null, bool IsTruncated = false)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public ConnectionProfile? SourceProfile { get; init; }
    /// <summary>Collection method that produced the value (find, findOne, aggregate…), when known.</summary>
    public string? Method { get; init; }
    /// <summary>True when find/findOne received a non-empty projection, so documents may omit stored fields.</summary>
    public bool IsProjected { get; init; }
    public string DisplayText => $"[{Number}] {SourceProfile?.Name} › {Database}{(Collection is null ? "" : "." + Collection)} · {Documents?.Count ?? 0} documento(s){(IsTruncated ? " · limitado" : "")}";
}
