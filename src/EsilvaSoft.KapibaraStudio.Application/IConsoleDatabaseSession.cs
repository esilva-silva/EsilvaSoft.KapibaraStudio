using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Application;

/// <summary>Only validated proxy operations reach this boundary; no arbitrary commands or CLR objects.</summary>
public interface IConsoleDatabaseSession : IDisposable
{
    Task<string> ExecuteAsync(ConsoleOperation operation, CancellationToken cancellationToken);
}
