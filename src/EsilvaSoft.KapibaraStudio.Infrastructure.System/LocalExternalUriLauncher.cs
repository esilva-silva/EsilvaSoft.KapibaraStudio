using System.Diagnostics;
using EsilvaSoft.KapibaraStudio.Application;

namespace EsilvaSoft.KapibaraStudio.SystemAdapters;

/// <summary>System boundary for submitting validated web addresses to the default browser.</summary>
public sealed class LocalExternalUriLauncher : IExternalUriLauncher
{
    /// <inheritdoc />
    public Task OpenAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uri);
        cancellationToken.ThrowIfCancellationRequested();
        if (!uri.IsAbsoluteUri || uri.Scheme is not ("http" or "https"))
            throw new ArgumentException("O navegador precisa receber um endereço HTTP ou HTTPS absoluto.", nameof(uri));
        using var process = Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        if (process is null) throw new InvalidOperationException("Não foi possível abrir o navegador.");
        return Task.CompletedTask;
    }
}
