using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Application;

public interface IScriptExecutionService
{
    Task<ScriptExecutionResult> ExecuteAsync(
        ConnectionProfile profile,
        string script,
        string? inputJson = null,
        string? database = null,
        CancellationToken cancellationToken = default);
}
