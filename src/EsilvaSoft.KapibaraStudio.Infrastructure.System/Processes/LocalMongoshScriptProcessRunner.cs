using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application;

namespace EsilvaSoft.KapibaraStudio.SystemAdapters;

public sealed class LocalMongoshScriptProcessRunner : IMongoshScriptProcessRunner
{
    public async Task<MongoshProcessResult> RunAsync(MongoshProcessRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var scriptPath = Path.Combine(Path.GetTempPath(), $"slopdataadmin-{Guid.NewGuid():N}.js");
        try
        {
            await File.WriteAllTextAsync(scriptPath, request.ScriptSource, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            using var process = new Process { StartInfo = BuildStartInfo(request, scriptPath) };
            process.Start();
            using var cancellationRegistration = cancellationToken.Register(() => TryTerminate(process));
            var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var standardOutput = await standardOutputTask.ConfigureAwait(false);
            var standardError = await standardErrorTask.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return new(process.ExitCode, standardOutput, standardError);
        }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException("Não foi possível localizar o executável mongosh. Configure SLOPDATAADMIN_MONGOSH_PATH ou instale o MongoDB Shell.", exception);
        }
        finally
        {
            TryDelete(scriptPath);
        }
    }

    internal static ProcessStartInfo BuildStartInfo(MongoshProcessRequest request, string scriptPath)
    {
        var executable = Environment.GetEnvironmentVariable("SLOPDATAADMIN_MONGOSH_PATH");
        var startInfo = new ProcessStartInfo
        {
            FileName = string.IsNullOrWhiteSpace(executable) ? OperatingSystem.IsWindows() ? "mongosh.exe" : "mongosh" : executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        startInfo.ArgumentList.Add("--norc");
        startInfo.ArgumentList.Add("--nodb");
        startInfo.Environment["SLOP_CONNECTION_URI"] = request.ConnectionString;
        startInfo.Environment["SLOP_ENVIRONMENT_VALUES"] = JsonSerializer.Serialize(request.EnvironmentValues);
        startInfo.ArgumentList.Add("--file");
        startInfo.ArgumentList.Add(scriptPath);
        return startInfo;
    }

    private static void TryTerminate(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { /* The process already exited. */ }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { /* Retains the previous best-effort cleanup behavior. */ }
    }
}
