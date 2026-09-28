using System.Diagnostics;
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Infrastructure;

public sealed class MongoshScriptExecutionService(IEnvironmentVaultRepository? environments = null,
    IConnectionSecretStore? secrets = null, ISecretStore? credentialStore = null) : IScriptExecutionService
{
    public async Task<ScriptExecutionResult> ExecuteAsync(
        ConnectionProfile profile,
        string script,
        string? inputJson = null,
        string? database = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(script);
        profile.EnsureWriteAllowed();
        var environment = new OperationEnvironment(environments, secrets, profile.Id, credentialStore);
        await environment.PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        var connectionString = environment.ResolvedConnection;
        var safeInput = MongoshScriptTemplate.ValidateInput(inputJson);
        var scriptPath = Path.Combine(Path.GetTempPath(), $"slopdataadmin-{Guid.NewGuid():N}.js");
        var source = MongoshScriptTemplate.BuildScript(safeInput, script, database ?? profile.DefaultDatabase);
        var startInfo = BuildStartInfo(connectionString, scriptPath, environment.ScriptValues);
        var startedAt = Stopwatch.GetTimestamp();

        try
        {
            await File.WriteAllTextAsync(scriptPath, source, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            using var process = new Process { StartInfo = startInfo };
            process.Start();
            using var cancellationRegistration = cancellationToken.Register(() => TryTerminate(process));
            var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var standardOutput = await standardOutputTask.ConfigureAwait(false);
            var standardError = await standardErrorTask.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return CreateResult(process.ExitCode, standardOutput, standardError, Stopwatch.GetElapsedTime(startedAt),
                SecretsOf(connectionString, profile));
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

    internal const int MaxDiagnosticCharacters = 32 * 1024;
    private const string Redacted = "[redigido]";

    private static readonly System.Text.RegularExpressions.Regex UriPattern = new(
        @"mongodb(?:\+srv)?://[^\s""'<>`]+", System.Text.RegularExpressions.RegexOptions.IgnoreCase |
        System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly System.Text.RegularExpressions.Regex UserInfoPattern = new(
        @"(?<=//)[^\s/@""']+:[^\s/@""']*@", System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly System.Text.RegularExpressions.Regex PasswordOptionPattern = new(
        @"(?i)(password|passwd|pwd|secret|token|sslPassword|tlsCertificateKeyFilePassword)(\s*[=:]\s*)(""[^""]*""|'[^']*'|[^\s&;,""']+)",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly System.Text.RegularExpressions.Regex VaultReferencePattern = new(
        @"\{\{[^{}]*\}\}|\$\{[^{}]*\}|\b[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}\b",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>
    /// Builds the result. Failures keep the (sanitized, bounded) diagnostic so syntax/runtime errors stay
    /// actionable; structured results are only published for a successful process.
    /// </summary>
    internal static ScriptExecutionResult CreateResult(int exitCode, string standardOutput, string standardError,
        TimeSpan duration, IEnumerable<string>? knownSecrets = null)
    {
        var secrets = (knownSecrets ?? []).Where(value => !string.IsNullOrEmpty(value)).ToArray();
        var parsed = MongoshOutputParser.Parse(standardOutput, MongoshScriptTemplate.ResultPrefix);
        var console = Sanitize(parsed.ConsoleOutput, secrets);
        var error = Sanitize(standardError, secrets);
        if (exitCode == 0)
        {
            return new ScriptExecutionResult(exitCode, parsed.Results, console, error, duration);
        }

        var header = $"O mongosh terminou com código {exitCode}.";
        error = string.IsNullOrWhiteSpace(error) ? header : header + Environment.NewLine + error;
        return new ScriptExecutionResult(exitCode, [], console, error, duration);
    }

    internal static string Sanitize(string? text, IReadOnlyCollection<string> knownSecrets)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var value = text.Length > MaxDiagnosticCharacters * 4 ? text[..(MaxDiagnosticCharacters * 4)] : text;
        try
        {
            foreach (var secret in knownSecrets.OrderByDescending(item => item.Length))
            {
                value = value.Replace(secret, Redacted, StringComparison.Ordinal);
                var encoded = Uri.EscapeDataString(secret);
                if (!string.Equals(encoded, secret, StringComparison.Ordinal))
                    value = value.Replace(encoded, Redacted, StringComparison.Ordinal);
            }
            value = UriPattern.Replace(value, "mongodb://" + Redacted);
            value = UserInfoPattern.Replace(value, Redacted + "@");
            value = PasswordOptionPattern.Replace(value, "$1$2" + Redacted);
            value = VaultReferencePattern.Replace(value, Redacted);
        }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
        {
            return "[saída omitida: não foi possível sanear com segurança]";
        }

        return value.Length <= MaxDiagnosticCharacters
            ? value
            : value[..MaxDiagnosticCharacters] + Environment.NewLine + "[saída truncada]";
    }

    private static IEnumerable<string> SecretsOf(string connectionString, ConnectionProfile profile)
    {
        yield return connectionString;
        if (Uri.TryCreate(connectionString.Replace("mongodb+srv://", "mongodb://", StringComparison.OrdinalIgnoreCase), UriKind.Absolute, out var uri)
            && uri.UserInfo.IndexOf(':') is var colon and >= 0)
        {
            var password = uri.UserInfo[(colon + 1)..];
            yield return password;
            yield return Uri.UnescapeDataString(password);
        }

        if (profile.SecretReference is { } reference) yield return reference.Id.ToString();
    }

    public static ProcessStartInfo BuildStartInfo(string connectionString, string scriptPath, IReadOnlyDictionary<string, string>? values = null)
    {
        var executable = Environment.GetEnvironmentVariable("SLOPDATAADMIN_MONGOSH_PATH");
        var startInfo = new ProcessStartInfo
        {
            FileName = string.IsNullOrWhiteSpace(executable)
                ? OperatingSystem.IsWindows() ? "mongosh.exe" : "mongosh"
                : executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        startInfo.ArgumentList.Add("--norc");
        startInfo.ArgumentList.Add("--nodb");
        startInfo.Environment["SLOP_CONNECTION_URI"] = connectionString;
        startInfo.Environment["SLOP_ENVIRONMENT_VALUES"] = JsonSerializer.Serialize(values ?? new Dictionary<string, string>());
        startInfo.ArgumentList.Add("--file");
        startInfo.ArgumentList.Add(scriptPath);
        return startInfo;
    }

    private static void TryTerminate(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // O processo já terminou antes do cancelamento.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // O arquivo temporário é inofensivo e será removido pelo sistema quando possível.
        }
    }
}
