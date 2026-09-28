using System.Collections.Concurrent;
using EsilvaSoft.KapibaraStudio.Application;

namespace EsilvaSoft.KapibaraStudio.Infrastructure;

/// <summary>Session-only credential store. Passwords are never written to LiteDB or disk.</summary>
public sealed class SessionConnectionSecretStore : IConnectionSecretStore
{
    private readonly ConcurrentDictionary<Guid, string> _passwords = new();
    public void SetPassword(Guid profileId, string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        _passwords[profileId] = password;
    }
    public string? GetPassword(Guid profileId) => _passwords.TryGetValue(profileId, out var password) ? password : null;
    public void Remove(Guid profileId) => _passwords.TryRemove(profileId, out _);
}
