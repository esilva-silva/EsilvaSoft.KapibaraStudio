using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.SystemAdapters;

public sealed class LocalResultPageExportService : IResultPageExportService
{
    public Task ExportAsync(string path, IReadOnlyList<string> documents, bool csv, Action<int, int>? progress = null, CancellationToken cancellationToken = default) =>
        Task.Run(async () =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            cancellationToken.ThrowIfCancellationRequested();
            var partial = path + "." + Guid.NewGuid().ToString("N") + ".partial";
            try
            {
                await using (var stream = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan))
                    await QueryResultExportSerializer.WriteAsync(stream, documents, csv, progress, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(partial, path, overwrite: false);
            }
            finally { if (File.Exists(partial)) File.Delete(partial); }
        }, cancellationToken);
}
