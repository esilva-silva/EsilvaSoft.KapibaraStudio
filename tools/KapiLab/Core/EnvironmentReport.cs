using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Core;

internal sealed record EnvironmentReport(string Schema, string Backend, string Runtime, string OperatingSystem,
    string Architecture, string GenAiVersion, string? IdeCommit, bool? IdeDirty, string? KapiLabAssemblySha256,
    int ProcessorCount, long WorkingSetBytes)
{
    public static EnvironmentReport Create() => Create(AppContext.BaseDirectory);

    internal static EnvironmentReport Create(string baseDirectory)
    {
        var root = FindRepositoryRoot(baseDirectory);
        var (commit, dirty) = root is null ? (null, null) : ReadGitIdentity(root);
        var genAiVersion = AssemblyMetadata("KapibaraStudio.GenAiVersion") ?? "unavailable";
        var backend = AssemblyMetadata("KapibaraStudio.OnnxBackend") ?? BuildBackend;
        var assemblySha256 = HashExecutingAssembly();
        using var process = Process.GetCurrentProcess();
        return new("kapilab-env-v2", backend, Environment.Version.ToString(), RuntimeInformation.OSDescription,
            RuntimeInformation.ProcessArchitecture.ToString(), genAiVersion, commit, dirty, assemblySha256,
            Environment.ProcessorCount, process.WorkingSet64);
    }

    private static string? HashExecutingAssembly()
    {
        var location = typeof(EnvironmentReport).Assembly.Location;
        if (string.IsNullOrWhiteSpace(location) || !File.Exists(location)) return null;
        try
        {
            using var stream = new FileStream(location, FileMode.Open, FileAccess.Read, FileShare.Read,
                64 * 1024, FileOptions.SequentialScan);
            if (stream.Length > 256L * 1024 * 1024) return null;
            return Convert.ToHexStringLower(SHA256.HashData(stream));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    private static string? AssemblyMetadata(string key) =>
        typeof(EnvironmentReport).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => string.Equals(attribute.Key, key, StringComparison.Ordinal))?.Value;

    private static string BuildBackend
    {
        get
        {
#if KAPILAB_BACKEND_WINML
            return "WinML";
#elif KAPILAB_BACKEND_CUDA
            return "Cuda";
#else
            return "Cpu";
#endif
        }
    }

    private static string? FindRepositoryRoot(string path)
    {
        for (var directory = new DirectoryInfo(path); directory is not null; directory = directory.Parent)
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")) || File.Exists(Path.Combine(directory.FullName, ".git")))
                return directory.FullName;
        return null;
    }

    private static (string? Commit, bool? Dirty) ReadGitIdentity(string workingDirectory)
    {
        var commit = RunGit(workingDirectory, "rev-parse", "HEAD");
        var status = RunGit(workingDirectory, "status", "--porcelain");
        return (commit, status is null ? null : status.Length > 0);
    }

    private static string? RunGit(string workingDirectory, params string[] arguments)
    {
        try
        {
            var startInfo = new ProcessStartInfo("git")
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("-C");
            startInfo.ArgumentList.Add(workingDirectory);
            foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
            using var process = Process.Start(startInfo);
            if (process is null) return null;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = process.StandardError.ReadToEndAsync(timeout.Token);
            process.WaitForExitAsync(timeout.Token).GetAwaiter().GetResult();
            _ = error.GetAwaiter().GetResult();
            return process.ExitCode == 0 ? output.GetAwaiter().GetResult().Trim() : null;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or IOException or OperationCanceledException)
        {
            return null;
        }
    }
}
