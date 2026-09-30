namespace EsilvaSoft.KapibaraStudio.Application;

/// <summary>Writes serialized text to a new file, creating its parent directory and never replacing an existing file.</summary>
public interface ITextExportFileService
{
    Task WriteNewAsync(string path, string content, CancellationToken cancellationToken = default);
}

/// <summary>Raised when a requested text export would replace an existing file.</summary>
public sealed class TextExportFileAlreadyExistsException(string destinationPath, Exception? innerException = null)
    : IOException("O arquivo de destino já existe; escolha outro caminho para não sobrescrever.", innerException)
{
    public string DestinationPath { get; } = destinationPath;
}
