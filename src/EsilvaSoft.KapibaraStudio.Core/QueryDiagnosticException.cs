namespace EsilvaSoft.KapibaraStudio.Core;

/// <summary>Public query diagnostic containing safe metadata, never the server response or command document.</summary>
public sealed class QueryDiagnosticException(string message, int serverCode) : Exception(message)
{
    public int ServerCode { get; } = serverCode;
}
