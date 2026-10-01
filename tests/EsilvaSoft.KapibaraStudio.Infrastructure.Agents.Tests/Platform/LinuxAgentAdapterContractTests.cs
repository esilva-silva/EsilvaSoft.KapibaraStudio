using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.SystemAdapters;
using EsilvaSoft.KapibaraStudio.SystemAdapters.ClaudeCode;
using EsilvaSoft.KapibaraStudio.SystemAdapters.Copilot;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Tests.Platform;

[TestFixture, Category("Unit")]
internal sealed class LinuxAgentAdapterContractTests
{
    private static readonly string[] ExpectedSetsidProbes = ["/usr/bin/setsid", "/bin/setsid"];
    private static readonly byte[] ElfHeader = [0x7f, (byte)'E', (byte)'L', (byte)'F'];
    private static string Root => Path.GetFullPath("synthetic-agent-adapter");

    [Test]
    public void ClaudeProcessGroupLauncherUsesOnlyExecutableSystemCandidatesAndFailsClosed()
    {
        var probes = new List<string>();
        var selected = ClaudeCodeProcess.ResolveProcessGroupLauncher(true, path =>
        {
            probes.Add(path);
            return path == "/bin/setsid";
        });
        Assert.That(selected, Is.EqualTo("/bin/setsid"));
        Assert.That(probes, Is.EqualTo(ExpectedSetsidProbes));
        Assert.That(ClaudeCodeProcess.ResolveProcessGroupLauncher(false, _ => throw new AssertionException("Non-Linux must not probe setsid.")), Is.Null);
        Assert.Throws<InvalidOperationException>(() => ClaudeCodeProcess.ResolveProcessGroupLauncher(true, _ => false));
    }

    [TestCase(0, ClaudeCodeAccountCommandState.Completed)]
    [TestCase(1, ClaudeCodeAccountCommandState.CommandFailed)]
    [TestCase(-1, ClaudeCodeAccountCommandState.CommandFailed)]
    public void ClaudeVisibleCommandExitCodeHasStableClassification(int exitCode, ClaudeCodeAccountCommandState expected) =>
        Assert.That(ClaudeCodeAccountCommands.ClassifyExitCode(exitCode), Is.EqualTo(expected));

    [Test]
    public void ClaudeLocatorSkipsNonExecutableResolvedTargetAndAcceptsExecutableElfWithoutIo()
    {
        var firstDirectory = Path.Combine(Root, "first");
        var secondDirectory = Path.Combine(Root, "second");
        var rejectedLink = Path.Combine(firstDirectory, "claude");
        var accepted = Path.Combine(secondDirectory, "claude");
        var finalTarget = Path.Combine(Root, "claude-without-execute");
        var metadata = new Dictionary<string, ClaudeCodeExecutableLocator.CandidateMetadata>
        {
            [rejectedLink] = new(finalTarget, false, UnixFileMode.UserRead,
                () => throw new AssertionException("A non-executable target must be rejected before reading its bytes.")),
            [accepted] = new(accepted, false, UnixFileMode.UserRead | UnixFileMode.UserExecute, () => ElfHeader)
        };
        var probes = new List<string>();
        string? Validate(string candidate) => ClaudeCodeExecutableLocator.Validate(candidate, false, true, path =>
        {
            probes.Add(path);
            return metadata.GetValueOrDefault(path);
        });
        var locator = new ClaudeCodeExecutableLocator(string.Join(Path.PathSeparator, firstDirectory, secondDirectory),
            Root, null, Validate, metadata.ContainsKey, isWindows: false);
        var result = locator.Locate(null);

        Assert.That(Validate("relative-claude"), Is.Null);
        Assert.That(result.State, Is.EqualTo(ClaudeCodeExecutableState.Found));
        Assert.That(result.Path, Is.EqualTo(accepted));
        Assert.That(probes, Is.EqualTo(new[] { rejectedLink, accepted }));
        Assert.That(locator.Locate(rejectedLink).State, Is.EqualTo(ClaudeCodeExecutableState.UnsupportedExecutable));
        Assert.That(locator.Locate(Path.Combine(Root, "missing")).State, Is.EqualTo(ClaudeCodeExecutableState.NotFound));
    }

    [Test]
    public void ClaudeCandidatePolicyRejectsDirectoryBrokenLinkShortHeaderAndProbeFailures()
    {
        var path = Path.Combine(Root, "claude");
        string? Validate(ClaudeCodeExecutableLocator.CandidateMetadata? metadata) =>
            ClaudeCodeExecutableLocator.Validate(path, false, true, _ => metadata);
        Assert.That(Validate(null), Is.Null, "A broken/missing target is not an executable.");
        Assert.That(Validate(new(path, true, UnixFileMode.UserExecute, () => ElfHeader)), Is.Null);
        Assert.That(Validate(new(path, false, UnixFileMode.UserExecute, () => [0x7f])), Is.Null);
        Assert.That(Validate(new(path, false, UnixFileMode.UserExecute, () => [(byte)'M', (byte)'Z', 0, 0])), Is.Null);
        Assert.That(ClaudeCodeExecutableLocator.Validate(path, false, true, _ => throw new UnauthorizedAccessException()), Is.Null);
        Assert.That(Validate(new(path, false, UnixFileMode.UserExecute, () => throw new IOException())), Is.Null);
    }

    [Test]
    public void CopilotCliAndTerminalDiscoveryUseInjectedExecuteModeWithoutIo()
    {
        var firstDirectory = Path.Combine(Root, "first");
        var secondDirectory = Path.Combine(Root, "second");
        var rejectedCli = Path.Combine(firstDirectory, "copilot");
        var acceptedCli = Path.Combine(secondDirectory, "copilot");
        var rejectedTerminal = Path.Combine(firstDirectory, "x-terminal-emulator");
        var acceptedTerminal = Path.Combine(secondDirectory, "x-terminal-emulator");
        var modes = new Dictionary<string, UnixFileMode>
        {
            [rejectedCli] = UnixFileMode.UserRead,
            [acceptedCli] = UnixFileMode.UserRead | UnixFileMode.UserExecute,
            [rejectedTerminal] = UnixFileMode.UserRead,
            [acceptedTerminal] = UnixFileMode.UserRead | UnixFileMode.GroupExecute
        };
        bool Executable(string candidate) => LinuxExecutableProbe.IsExecutable(candidate, true,
            modes.ContainsKey, path => modes[path]);
        Assert.That(LocalCopilotAccountCommands.FindCliExecutable(firstDirectory, isWindows: false,
            exists: modes.ContainsKey, executableProbe: Executable), Is.Null);
        Assert.That(LocalCopilotAccountCommands.FindCliExecutable(string.Join(Path.PathSeparator, firstDirectory, secondDirectory),
            isWindows: false, exists: modes.ContainsKey, executableProbe: Executable), Is.EqualTo(acceptedCli));
        Assert.That(Executable(rejectedTerminal), Is.False);
        Assert.That(Executable("relative-terminal"), Is.False);
        Assert.That(ClaudeCodeAccountCommands.FindLinuxTerminal(string.Join(Path.PathSeparator, firstDirectory, secondDirectory),
            executableProbe: Executable)?.Path, Is.EqualTo(acceptedTerminal));
    }

    [Test]
    public void LinuxExecuteModeProbeRejectsMissingInaccessibleAndNonLinuxWithoutStartingCandidates()
    {
        var path = Path.Combine(Root, "candidate");
        Assert.That(LinuxExecutableProbe.IsExecutable(path, false,
            _ => throw new AssertionException("Non-Linux must not access a file."),
            _ => throw new AssertionException("Non-Linux must not query permissions.")), Is.False);
        Assert.That(LinuxExecutableProbe.IsExecutable(path, true, _ => false,
            _ => throw new AssertionException("A missing file must not query permissions.")), Is.False);
        Assert.That(LinuxExecutableProbe.IsExecutable(path, true, _ => true, _ => throw new UnauthorizedAccessException()), Is.False);
        Assert.That(LinuxExecutableProbe.IsExecutable(path, true, _ => throw new IOException(), _ => UnixFileMode.UserExecute), Is.False);
        Assert.That(LinuxExecutableProbe.IsExecutable(path, true, _ => true, _ => UnixFileMode.OtherExecute), Is.True);
    }
}
