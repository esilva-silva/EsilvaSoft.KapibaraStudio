using System.Security.Cryptography;
using EsilvaSoft.SlopStudio.Application;

namespace EsilvaSoft.SlopStudio.Infrastructure;

// One wire instance belongs to one operation. Cancelling it disconnects only that operation.
internal interface ISecretServiceWire : IAsyncDisposable
{
    Task ConnectAsync();
    Task<string> ReadDefaultCollectionAsync();
    Task<SecretServiceSearch> SearchAsync(IReadOnlyDictionary<string, string> attributes);
    Task<SecretServiceSession> OpenSessionAsync(byte[] publicKey);
    Task<bool> IsLockedAsync(string path, bool collection);
    Task<SecretServiceUnlock> UnlockAsync(string path);
    Task<SecretServicePromptResult> PromptAsync(string path);
    Task<SecretServiceSecret> GetSecretAsync(string item, string session);
    Task SetSecretAsync(string item, SecretServiceSecret secret);
    Task<SecretServiceCreated> CreateItemAsync(string collection, IReadOnlyDictionary<string, string> attributes, SecretServiceSecret secret);
    Task<string> DeleteAsync(string item);
}

internal sealed record SecretServiceSearch(string[] Unlocked, string[] Locked);
internal sealed record SecretServiceSession(string Path, byte[] PublicKey);
internal sealed record SecretServiceUnlock(string[] Unlocked, string Prompt);
internal sealed record SecretServiceCreated(string Item, string Prompt);
internal sealed record SecretServicePromptResult(bool Dismissed, string? Item, string[]? Unlocked);

// Does not use record-generated ToString: even encrypted material stays out of diagnostics.
internal sealed class SecretServiceSecret(string session, byte[] parameters, byte[] value, string contentType) : IDisposable
{
    public string Session { get; } = session;
    public byte[] Parameters { get; } = parameters;
    public byte[] Value { get; } = value;
    public string ContentType { get; } = contentType;

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(Parameters);
        CryptographicOperations.ZeroMemory(Value);
    }
}

internal sealed class SecretServiceException(SecretStoreFailureCode code) : Exception("A operação do cofre Linux não foi concluída.")
{
    public SecretStoreFailureCode Code { get; } = code;
}
