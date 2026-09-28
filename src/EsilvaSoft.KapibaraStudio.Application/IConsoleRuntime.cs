using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Application;

public interface IConsoleRuntime
{
    /// <summary>Optional UI localizer; the runtime keeps its Portuguese compatibility text when not configured.</summary>
    void SetLocalization(Func<string, string> localize) { }

    Task<ConsoleExecutionResult> ExecuteAsync(ConsoleRequest request,
        Func<ConsoleWriteConfirmation, CancellationToken, Task<bool>> confirmWrite, CancellationToken cancellationToken = default);
    ConsoleStatement GetStatement(string script, int caret);
}
