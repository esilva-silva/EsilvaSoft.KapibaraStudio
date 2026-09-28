namespace EsilvaSoft.KapibaraStudio.Application;

public interface ICodeFormatter
{
    Task<string> FormatAsync(string text, CancellationToken cancellationToken = default);
}
