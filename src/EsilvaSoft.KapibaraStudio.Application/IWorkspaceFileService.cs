namespace EsilvaSoft.KapibaraStudio.Application;

public sealed record WorkspaceFileEntry(string Name, string FullPath, bool IsDirectory);

public interface IWorkspaceFileService
{
    Task<IReadOnlyList<WorkspaceFileEntry>> ListAsync(string rootPath, string? relativeDirectory = null,
        CancellationToken cancellationToken = default);
    Task<string> CreateFileAsync(string rootPath, string relativePath, CancellationToken cancellationToken = default);
    Task<string> CreateDirectoryAsync(string rootPath, string relativePath, CancellationToken cancellationToken = default);
    Task<string> RenameAsync(string rootPath, string relativePath, string newName, CancellationToken cancellationToken = default);
    Task MoveToTrashAsync(string rootPath, string relativePath, CancellationToken cancellationToken = default);
}
