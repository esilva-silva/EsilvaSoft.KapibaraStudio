using EsilvaSoft.KapibaraStudio.Application.Agents;

namespace EsilvaSoft.KapibaraStudio.SystemAdapters;

public sealed class LocalAgentBoundedFileReader : IAgentBoundedFileReader
{
    public AgentFileReadResult Read(string fullPath, int maximumBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumBytes);
        var info = new FileInfo(fullPath);
        if (!info.Exists) return new(AgentFileReadState.NotFound, []);
        if (info.Length > maximumBytes) return new(AgentFileReadState.TooLarge, []);
        using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var buffer = new byte[checked(maximumBytes + 1)];
        var read = 0;
        int chunk;
        while (read < buffer.Length && (chunk = stream.Read(buffer.AsSpan(read))) > 0) read += chunk;
        return read > maximumBytes ? new(AgentFileReadState.TooLarge, []) : new(AgentFileReadState.Read, buffer[..read]);
    }
    public async Task<AgentFileReadResult> ReadAsync(string fullPath, int maximumBytes, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumBytes);
        cancellationToken.ThrowIfCancellationRequested();
        var info = new FileInfo(fullPath);
        if (!info.Exists) return new(AgentFileReadState.NotFound, []);
        if (info.Length > maximumBytes) return new(AgentFileReadState.TooLarge, []);
        await using var stream = new FileStream(fullPath, new FileStreamOptions
        {
            Mode = FileMode.Open, Access = FileAccess.Read,
            Share = FileShare.ReadWrite | FileShare.Delete,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan
        });
        // A file may grow after the metadata check. One extra byte distinguishes truncation from an exact fit.
        var buffer = new byte[checked(maximumBytes + 1)];
        var read = 0;
        int chunk;
        while (read < buffer.Length && (chunk = await stream.ReadAsync(buffer.AsMemory(read), cancellationToken)
                   .ConfigureAwait(false)) > 0) read += chunk;
        return read > maximumBytes ? new(AgentFileReadState.TooLarge, []) : new(AgentFileReadState.Read, buffer[..read]);
    }
}
