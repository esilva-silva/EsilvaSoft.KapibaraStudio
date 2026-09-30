namespace EsilvaSoft.KapibaraStudio.SystemAdapters;

// Files and directories share a gate because moving a directory affects every open child file.
internal static class LocalFileMutationGate
{
    internal static SemaphoreSlim Semaphore { get; } = new(1, 1);
}
