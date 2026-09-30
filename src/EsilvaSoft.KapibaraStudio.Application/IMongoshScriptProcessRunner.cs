namespace EsilvaSoft.KapibaraStudio.Application;

/// <summary>Runs one captured script. The adapter owns its temporary file and process tree;
/// cancellation stops that execution and does not undo operations already sent to MongoDB.</summary>
public interface IMongoshScriptProcessRunner
{
    Task<MongoshProcessResult> RunAsync(MongoshProcessRequest request, CancellationToken cancellationToken = default);
}

public sealed record MongoshProcessRequest(string ConnectionString, string ScriptSource,
    IReadOnlyDictionary<string, string> EnvironmentValues);

public sealed record MongoshProcessResult(int ExitCode, string StandardOutput, string StandardError);
