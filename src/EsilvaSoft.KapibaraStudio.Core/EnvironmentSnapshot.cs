using System.Collections.Frozen;

namespace EsilvaSoft.KapibaraStudio.Core;

/// <summary>Immutable values captured before an operation starts; never persisted in workspace drafts.</summary>
public sealed class EnvironmentSnapshot(string name, IReadOnlyDictionary<string, string> values)
{
    public string Name { get; } = name;
    public IReadOnlyDictionary<string, string> Values { get; } = values.ToFrozenDictionary(StringComparer.Ordinal);
    public string Get(string key) => Values.TryGetValue(key, out var value) ? value
        : throw new InvalidOperationException($"Chave {key} não definida no ambiente {Name}.");
}
