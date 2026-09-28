using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

internal sealed class ControlledScripts : IScriptExecutionService
{
    public List<(Guid Profile, string? Database, string Script, TaskCompletionSource<ScriptExecutionResult> Completion)> Calls { get; } = [];
    public Task<ScriptExecutionResult> ExecuteAsync(ConnectionProfile profile, string script, string? inputJson = null, string? database = null, CancellationToken cancellationToken = default)
    {
        profile.EnsureWriteAllowed();
        var source = new TaskCompletionSource<ScriptExecutionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        Calls.Add((profile.Id, database, script, source));
        return source.Task.WaitAsync(cancellationToken);
    }
}
