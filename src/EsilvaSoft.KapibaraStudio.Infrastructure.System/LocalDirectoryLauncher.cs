using System.Diagnostics;
using EsilvaSoft.KapibaraStudio.Application;

namespace EsilvaSoft.KapibaraStudio.SystemAdapters;

/// <summary>Native file-manager launch boundary; the directory is a shell-open target, never command text.</summary>
public sealed class LocalDirectoryLauncher : ILocalDirectoryLauncher
{
    /// <inheritdoc />
    public Task<bool> OpenAsync(string directory, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        cancellationToken.ThrowIfCancellationRequested();
        if (!Path.IsPathFullyQualified(directory) || !Directory.Exists(directory)) return Task.FromResult(false);
        using var process = Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
        // A successful shell-open may reuse an existing file manager and return no new process handle.
        return Task.FromResult(true);
    }
}
