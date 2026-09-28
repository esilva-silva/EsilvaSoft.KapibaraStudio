namespace EsilvaSoft.KapibaraStudio.Application;

public sealed record CodeValidationResult(bool IsValid, string Message, int Offset = 0, int Length = 0);
