using System.Security.Cryptography;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests.Mcp;

/// <summary>Checks that this Windows session can write, read back, and remove a disposable credential.</summary>
internal static class WindowsCredentialStoreWritePreflight
{
    public static async Task<string?> CheckAsync(ISecretStore store)
    {
        var reference = new SecretReference(Guid.NewGuid());
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        string? failureReason = null;
        string? cleanupFailure = null;
        var cleanupCompleted = false;
        try
        {
            var written = await store.SetAsync(reference, secret).ConfigureAwait(false);
            if (!written.IsSuccess)
                failureReason = $"Credential Manager não permite gravar um segredo descartável nesta sessão ({written.Failure!.Code}).";
            else
            {
                var readback = await store.GetAsync(reference).ConfigureAwait(false);
                if (!readback.IsSuccess || !string.Equals(readback.Value, secret, StringComparison.Ordinal))
                    failureReason = "Credential Manager não permite validar por leitura um segredo descartável nesta sessão.";
                else
                {
                    var deleted = await store.DeleteAsync(reference).ConfigureAwait(false);
                    if (!deleted.IsSuccess)
                        cleanupFailure = "O pré-check não conseguiu remover o segredo descartável do Credential Manager.";
                    else cleanupCompleted = true;
                }
            }
        }
        finally
        {
            if (!cleanupCompleted)
            {
                var cleanup = await store.DeleteAsync(reference).ConfigureAwait(false);
                if (cleanup.IsSuccess || cleanup.Failure!.Code == SecretStoreFailureCode.NotFound)
                    cleanupCompleted = true;
                else
                    cleanupFailure = "O pré-check não conseguiu remover o segredo descartável do Credential Manager.";
            }
        }

        if (cleanupFailure is not null) throw new InvalidOperationException(cleanupFailure);
        return failureReason;
    }
}
