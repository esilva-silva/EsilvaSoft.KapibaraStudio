using System.Text;
using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application.Agents;

namespace EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Tests.ClaudeCode;

internal sealed class MemoryClaudeCodeSystem : IClaudeCodeSystem
{
    private static readonly string[] MissingResumeErrors = ["No conversation found"];

    public static string Root => Path.DirectorySeparatorChar == '\\' ? @"C:\claude-tests" : "/claude-tests";
    public string HomeDirectory => Path.Combine(Root, "home");
    public string TemporaryDirectory => Path.Combine(Root, "temp");
    public string DefaultDatabasePath => Path.Combine(Root, "appdata", "workspace.db");
    public string Executable { get; } = Path.Combine(Root, "bin", "claude.exe");
    public string AuthOutput { get; set; } = """{"loggedIn":true,"authMethod":"claude.ai","apiProvider":"firstParty","subscriptionType":"pro"}""";
    public string VersionOutput { get; set; } = "2.1.268 (Claude Code)";
    public string? BlockingVariable { get; set; }
    public bool Missing { get; set; }
    public bool Unsupported { get; set; }
    public bool VersionTimedOut { get; set; }
    public int VersionExitCode { get; set; }
    public int AuthExitCode { get; set; }
    public bool ExecutableValid { get; set; } = true;
    public bool HangTurn { get; set; }
    public bool PersistedResumeMissingOnce { get; set; }
    public bool OmitResultFrame { get; set; }
    public int ProcessExitCode { get; set; }
    public string? UnexpectedTool { get; set; }
    public IReadOnlyList<string> ProductTools { get; set; } = [];
    public Action? BeforeProcessStart { get; set; }
    public Exception? FingerprintFailure { get; set; }
    public ClaudeCodeExecutableFingerprint Fingerprint { get; set; } = new(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), 100);
    public HashSet<string> Directories { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> Links { get; } = new(StringComparer.Ordinal);
    public List<string> CreatedDirectories { get; } = [];
    public List<string> DebugLines { get; } = [];
    public List<string[]> Probes { get; } = [];
    public List<MemoryClaudeCodeProcess> Processes { get; } = [];
    public List<string[]> AccountArguments { get; } = [];
    public ClaudeCodeAccountCommandState AccountCommandState { get; set; } = ClaudeCodeAccountCommandState.Completed;
    public TaskCompletionSource<MemoryClaudeCodeProcess> ProcessStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int AccountCalls { get; private set; }
    public bool IsEnvironmentVariableSet(string name) => name == BlockingVariable;
    public ClaudeCodeExecutableCandidate Locate(string? configuredPath) => new(Missing || Unsupported ? null : Executable, Unsupported);
    public string? ValidateExecutable(string path) => ExecutableValid ? path : null;
    public ClaudeCodeExecutableFingerprint GetExecutableFingerprint(string path) => FingerprintFailure is null ? Fingerprint : throw FingerprintFailure;
    public bool DirectoryExists(string path) => Directories.Contains(path);
    public string ResolveDirectoryLink(string path) => Links.GetValueOrDefault(path, path);
    public void CreateDirectory(string path) { CreatedDirectories.Add(path); Directories.Add(path); }
    public void AppendDebugLog(string directory, string safeJson) => DebugLines.Add(safeJson);
    public Task<ClaudeCodeAccountCommandState> RunVisibleAccountCommandAsync(string executable, IReadOnlyList<string> arguments,
        string workingDirectory, TimeSpan timeout, CancellationToken cancellationToken)
    {
        AccountCalls++;
        AccountArguments.Add(arguments.ToArray());
        return Task.FromResult(AccountCommandState);
    }
    public Task<ClaudeCodeProbeResult> ProbeAsync(string executable, IReadOnlyList<string> arguments, string workingDirectory,
        TimeSpan timeout, int maxOutputBytes, int maxStderrBytes, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Probes.Add(arguments.ToArray());
        return Task.FromResult(arguments.Contains("--version")
            ? new ClaudeCodeProbeResult(VersionTimedOut, false, VersionExitCode, VersionOutput)
            : new ClaudeCodeProbeResult(false, false, AuthExitCode, AuthOutput));
    }
    public IClaudeCodeProcess Start(string executable, IReadOnlyList<string> arguments, string workingDirectory, int maxStderrBytes)
    {
        BeforeProcessStart?.Invoke();
        var args = arguments.ToArray();
        string Value(string flag) => args[Array.IndexOf(args, flag) + 1];
        var id = Value(args.Contains("--resume") ? "--resume" : "--session-id");
        // A CLI anuncia o identificador completo do modelo; aliases pertencem somente ao argv.
        var requestedModel = Value("--model");
        var observedModel = requestedModel switch
        {
            "haiku" => "claude-haiku-4-5-20251001",
            "sonnet" => "claude-sonnet-4-5",
            "opus" => "claude-opus-4-5",
            _ => requestedModel,
        };
        var tools = Value("--tools").Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (PersistedResumeMissingOnce && args.Contains("--resume"))
        {
            PersistedResumeMissingOnce = false;
            var missing = JsonSerializer.Serialize(new
            {
                type = "result",
                session_id = id,
                is_error = true,
                errors = MissingResumeErrors,
            });
            var failed = new MemoryClaudeCodeProcess(args, new MemoryStream(Encoding.UTF8.GetBytes(missing + "\n")), 1);
            Processes.Add(failed);
            ProcessStarted.TrySetResult(failed);
            return failed;
        }

        var hasMcp = args.Contains("--mcp-config");
        if (hasMcp) tools.AddRange(ProductTools.Select(McpServerLaunchSpec.ToolName));
        if (UnexpectedTool is not null) tools.Add(UnexpectedTool);
        var frames = new List<string>
        {
            JsonSerializer.Serialize(new { type = "system", subtype = "init", session_id = id, tools,
                mcp_servers = hasMcp ? new object[] { new { name = McpServerLaunchSpec.DefaultServerName, status = "connected" } }
                    : Array.Empty<object>(), model = observedModel, permissionMode = "default", apiKeySource = "none", claude_code_version = "2.1.268" }),
            JsonSerializer.Serialize(new { type = "assistant", session_id = id,
                message = new { id = "msg-memory", role = "assistant", content = new[] { new { type = "text", text = "ok" } } } }),
        };
        if (!OmitResultFrame)
            frames.Add(JsonSerializer.Serialize(new { type = "result", subtype = "success", session_id = id, is_error = false, num_turns = 1, result = "ok" }));
        var bytes = Encoding.UTF8.GetBytes(string.Join('\n', frames) + "\n");
        var process = new MemoryClaudeCodeProcess(args, HangTurn ? new PendingOutputStream(bytes)
            : new MemoryStream(bytes), ProcessExitCode);
        Processes.Add(process);
        ProcessStarted.TrySetResult(process);
        return process;
    }
}

internal sealed class MemoryClaudeCodeProcess(string[] arguments, Stream output, int processExitCode = 0) : IClaudeCodeProcess
{
    private bool _exited;
    public Task ReadStarted => output is PendingOutputStream pending ? pending.ReadStarted.Task : Task.CompletedTask;
    public void ReleaseOutput()
    {
        if (output is PendingOutputStream pending) pending.Release();
    }
    private readonly MemoryStream _input = new();
    public string[] Arguments { get; } = arguments;
    public Stream StandardOutput => output;
    public StreamWriter StandardInput { get; } = new(new MemoryStream(), new UTF8Encoding(false));
    public bool HasExited => _exited || WasKilled;
    public int? ExitCode => HasExited ? processExitCode : null;
    public bool WasKilled { get; private set; }
    public int KillRequests { get; private set; }
    public string StderrSnapshot => string.Empty;
    public bool Disposed { get; private set; }
    public void KillTree()
    {
        KillRequests++;
        WasKilled = true;
    }
    public Task<bool> WaitForExitAsync(TimeSpan timeout)
    {
        // EOF significa que o filho encerrou stdout; preservar o exit code deixa a camada de turno classificar crash vs. truncamento.
        if (output is MemoryStream memory && memory.Position >= memory.Length ||
            output is PendingOutputStream pending && pending.IsAtEnd)
            _exited = true;
        return Task.FromResult(HasExited);
    }
    public ValueTask DisposeAsync()
    {
        Disposed = true;
        StandardInput.Dispose();
        output.Dispose();
        _input.Dispose();
        return ValueTask.CompletedTask;
    }
}

internal sealed class PendingOutputStream(byte[] frames) : Stream
{
    private readonly MemoryStream _output = new(frames);
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource ReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool IsReleased => _release.Task.IsCompletedSuccessfully;
    public bool IsAtEnd => _output.Position >= _output.Length;
    public void Release() => _release.TrySetResult();
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ReadStarted.TrySetResult();
        await _release.Task.WaitAsync(cancellationToken);
        return await _output.ReadAsync(buffer, cancellationToken);
    }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    protected override void Dispose(bool disposing)
    {
        if (disposing) _output.Dispose();
        base.Dispose(disposing);
    }
}
