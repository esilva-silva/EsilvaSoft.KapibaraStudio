namespace EsilvaSoft.KapibaraStudio.Application;

public interface IResultPageExportService
{
    Task ExportAsync(string path, IReadOnlyList<string> documents, bool csv, Action<int, int>? progress = null, CancellationToken cancellationToken = default);
}
