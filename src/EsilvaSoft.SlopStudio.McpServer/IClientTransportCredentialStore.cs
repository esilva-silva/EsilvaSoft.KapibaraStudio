using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.McpServer;

/// <summary>
/// Minimal, read-only access of the proxy to its own IPC channel proof in the OS store. It cannot enumerate, write or
/// resolve MongoDB/provider secrets; the proof never comes from argv, environment or configuration files.
/// </summary>
internal interface IClientTransportCredentialStore
{
    /// <returns>The proof, or <see langword="null"/> when the store is unavailable or the entry is missing.</returns>
    Task<string?> ReadAsync(SecretReference reference, CancellationToken cancellationToken);
}
