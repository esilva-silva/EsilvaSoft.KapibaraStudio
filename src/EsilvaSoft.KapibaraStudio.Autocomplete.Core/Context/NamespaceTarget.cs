namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core.Context;

/// <summary>A syntactic namespace, not proof that a connection or collection exists.</summary>
public sealed record NamespaceTarget(
    string? ConnectionName, string? Database, string? Collection, NamespaceTargetConfidence Confidence)
{
    /// <summary>Captured metadata identity, without credentials. Named roots clear it unless they name the tab connection.</summary>
    public ConnectionIdentity? Connection { get; init; }

    public static NamespaceTarget Unknown { get; } = new(null, null, null, NamespaceTargetConfidence.Unknown);
}
