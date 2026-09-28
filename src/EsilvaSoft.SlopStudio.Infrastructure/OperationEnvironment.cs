using System.Collections;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Infrastructure;

internal sealed class OperationEnvironment
{
    public EnvironmentSnapshot Vault { get; }
    private readonly Dictionary<string, string> _legacy;
    private readonly ISecretStore? _credentialStore;
    public string ResolvedConnection { get; private set; } = "";

    public OperationEnvironment(IEnvironmentVaultRepository? repository, IConnectionSecretStore? secrets, Guid profileId,
        ISecretStore? credentialStore = null)
    {
        _credentialStore = credentialStore;
        Vault = (repository?.LoadEnvironments() ?? EnvironmentVault.CreateDefault()).Capture();
        _legacy = Environment.GetEnvironmentVariables().Cast<DictionaryEntry>()
            .ToDictionary(e => (string)e.Key, e => (string)e.Value!, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        if (secrets?.GetPassword(profileId) is { } password) _legacy["MONGODB_PASSWORD"] = password;
    }

    public string Get(string key) => Vault.Values.TryGetValue(key, out var value) ? value
        : _legacy.TryGetValue(key, out value) ? value : Vault.Get(key);
    public string? GetLegacy(string key) => _legacy.GetValueOrDefault(key);
    public string ResolveConnection(ConnectionProfile profile) => ConnectionRouting.Apply(profile.ResolveConnectionString(
        key => _legacy.GetValueOrDefault(key), Get), profile.TargetHost);
    public async Task PrepareAsync(ConnectionProfile profile, CancellationToken cancellationToken = default)
    {
        var source = await ResolveStoredConnectionUriAsync(profile, _credentialStore, cancellationToken).ConfigureAwait(false);
        var uri = (profile with { ConnectionString = source }).ResolveConnectionString(
            key => _legacy.GetValueOrDefault(key), Get);
        ResolvedConnection = await ConnectionRouting.ApplyAsync(uri, profile.TargetHost, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<string> ResolveStoredConnectionUriAsync(ConnectionProfile profile,
        ISecretStore? credentialStore, CancellationToken cancellationToken)
    {
        if (profile.SecretReference is not { } reference) return profile.ConnectionString;
        if (credentialStore is null)
            throw new InvalidOperationException("O cofre da conexão não está disponível.");
        SecretStoreResult<string> secret;
        try { secret = await credentialStore.GetAsync(reference, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { throw new InvalidOperationException("Não foi possível ler a credencial da conexão."); }
        if (!secret.IsSuccess ||
            !string.Equals(LiteDbConnectionProfileRepository.RedactInlinePassword(secret.Value),
                profile.ConnectionString, StringComparison.Ordinal))
            throw new InvalidOperationException("A credencial da conexão não pôde ser validada.");
        return secret.Value;
    }
    public IReadOnlyDictionary<string, string> ScriptValues => _legacy.Concat(Vault.Values)
        .GroupBy(pair => pair.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Last().Value);
}
