namespace EsilvaSoft.KapibaraStudio.Core;

public sealed record ConsoleOperation(Guid ProfileId, string Database, string Collection, string Method, string ArgumentsJson);
