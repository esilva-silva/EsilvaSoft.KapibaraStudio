namespace EsilvaSoft.KapibaraStudio.Application;

public interface ICodeValidator
{
    Task<CodeValidationResult> ValidateAsync(string text, bool aggregation, CancellationToken cancellationToken = default);
}
