using System.Text;
using EsilvaSoft.KapibaraStudio.Application.Agents;

namespace EsilvaSoft.KapibaraStudio.SystemAdapters;

public sealed class LocalAgentWorkspaceFileCreator : IAgentWorkspaceFileCreator
{
    private static readonly UTF8Encoding Encoding = new(false, true);
    public async Task<AgentFileCreationStatus> CreateAsync(string root, string relativePath, string content,
        IReadOnlyList<string> exclusions, Func<CancellationToken, Task<bool>> authorizeCommit, CancellationToken cancellationToken)
    {
        var bytes = Encoding.GetBytes(content);
        if (bytes.Length > 256 * 1024 || content.Contains('\0') || Path.IsPathRooted(relativePath) || AgentWorkspacePaths.HasUnsafeSegment(relativePath))
            return AgentFileCreationStatus.PathRejected;
        var probe = new LocalAgentWorkspacePathProbe();
        string? temporary = null;
        await LocalFileMutationGate.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!SafePath(out var path)) return AgentFileCreationStatus.PathRejected;
            var parent = Path.GetDirectoryName(path)!;
            if (!Directory.Exists(parent)) return AgentFileCreationStatus.ParentMissing;
            if (File.Exists(path) || Directory.Exists(path)) return AgentFileCreationStatus.AlreadyExists;
            temporary = Path.Combine(parent, ".kapibara-create-" + Guid.NewGuid().ToString("N") + ".tmp");
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             4096, FileOptions.Asynchronous))
            {
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            if (!await authorizeCommit(cancellationToken).ConfigureAwait(false)) return AgentFileCreationStatus.PermissionDenied;
            cancellationToken.ThrowIfCancellationRequested();
            if (!SafePath(out var current) || !string.Equals(path, current, StringComparison.Ordinal))
                return AgentFileCreationStatus.PathRejected;
            if (File.Exists(path) || Directory.Exists(path)) return AgentFileCreationStatus.AlreadyExists;
            File.Move(temporary, path, overwrite: false);
            temporary = null;
            return AgentFileCreationStatus.Created;
        }
        catch (IOException) { return AgentFileCreationStatus.Failed; }
        catch (UnauthorizedAccessException) { return AgentFileCreationStatus.Failed; }
        finally
        {
            try { if (temporary is not null) File.Delete(temporary); }
            finally { LocalFileMutationGate.Semaphore.Release(); }
        }

        bool SafePath(out string path)
        {
            path = string.Empty;
            return !probe.TraversesLink(root, null) &&
                AgentWorkspacePaths.TryResolveInside(root, relativePath, exclusions, out path, out _, out _, probe);
        }
    }
}
