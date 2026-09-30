namespace EsilvaSoft.KapibaraStudio.Application;

/// <summary>Opens an existing directory in the platform file manager after an explicit user action.</summary>
public interface ILocalDirectoryLauncher
{
    /// <summary>Submits the directory to the native file manager; never creates it or executes a shell command.</summary>
    /// <param name="directory">Captured absolute directory to open.</param>
    /// <param name="cancellationToken">Cancellation observed before submitting the launch.</param>
    /// <returns>Whether the platform accepted the launch request.</returns>
    Task<bool> OpenAsync(string directory, CancellationToken cancellationToken = default);
}
