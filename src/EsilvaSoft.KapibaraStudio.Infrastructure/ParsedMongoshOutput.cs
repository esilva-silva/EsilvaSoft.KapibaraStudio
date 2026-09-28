namespace EsilvaSoft.KapibaraStudio.Infrastructure;

public sealed record ParsedMongoshOutput(IReadOnlyList<string> Results, string ConsoleOutput);
