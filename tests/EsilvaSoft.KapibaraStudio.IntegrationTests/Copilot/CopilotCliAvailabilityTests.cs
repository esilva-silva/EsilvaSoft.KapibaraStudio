using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.SystemAdapters.Copilot;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests.Copilot;

[TestFixture, Category("Integration")]
public sealed class CopilotCliAvailabilityTests
{
    private string _root = null!;
    [SetUp]
    public void CreateRoot()
    {
        _root = Path.Combine(Path.GetTempPath(), "KapibaraStudioCliAvailability", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }
    [TearDown]
    public void RemoveRoot()
    {
        var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "KapibaraStudioCliAvailability"));
        var root = Path.GetFullPath(_root);
        if (root.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            Directory.Delete(root, recursive: true);
    }

    private sealed class Configuration(string? path, Func<string?> resolve) : ICopilotCliConfiguration
    {
        public string? ExecutablePath { get; set; } = path;
        public string? ResolveExecutablePath() => resolve();
        public string? ValidateExecutablePath(string? value) => value;
    }

    [TestCase("copilot.exe", CopilotCliAvailability.InvalidPath)]
    [TestCase("copilot.cmd", CopilotCliAvailability.InvalidPath)]
    [TestCase("copilot.cmd", CopilotCliAvailability.UnsupportedExecutable)]
    [TestCase("copilot.CMD", CopilotCliAvailability.UnsupportedExecutable)]
    [TestCase("copilot.bat", CopilotCliAvailability.UnsupportedExecutable)]
    [TestCase("copilot.ps1", CopilotCliAvailability.UnsupportedExecutable)]
    public void MissingPathAndWindowsShimsHaveDifferentDiagnostics(string name, CopilotCliAvailability expected)
    {
        var path = Path.Combine(_root, name);
        if (expected == CopilotCliAvailability.UnsupportedExecutable) File.WriteAllText(path, "echo must-not-run");
        Assert.That(LocalCopilotAccountCommands.ProbeCandidate(path, true), Is.EqualTo(expected));
    }

    [Test]
    public void RelativePathIsRejectedWithoutSelectingAnotherInstallation() =>
        Assert.That(LocalCopilotAccountCommands.ProbeCandidate("relative/copilot.exe", true), Is.EqualTo(CopilotCliAvailability.InvalidPath));

    [Test]
    public void ScriptRenamedAsNativeIsUnsupportedAndAccountCommandNeverStartsIt()
    {
        var path = Path.Combine(_root, OperatingSystem.IsWindows() ? "copilot.exe" : "copilot");
        File.WriteAllText(path, "#!/bin/sh\ntouch must-not-be-created\n");
        var commands = new LocalCopilotAccountCommands(new Configuration(path,
            () => throw new InvalidOperationException("Resolution must not run after the invalid selected candidate.")));
        Assert.Multiple(() =>
        {
            Assert.That(commands.ProbeCli(), Is.EqualTo(CopilotCliAvailability.UnsupportedExecutable));
            Assert.That(commands.IsCliInstalled(), Is.False);
            Assert.That(Directory.GetFiles(_root), Has.Length.EqualTo(1));
        });
        Assert.That(commands.RunVisibleAsync("login", CancellationToken.None).GetAwaiter().GetResult(),
            Is.EqualTo(CopilotAccountCommandState.RuntimeUnavailable));
        Assert.That(Directory.GetFiles(_root), Has.Length.EqualTo(1));
    }

    [Test]
    public void AutomaticDiscoveryDistinguishesAbsentInstallationFromScriptShim()
    {
        Assert.That(LocalCopilotAccountCommands.ProbeAutomaticCandidates(_root, true), Is.EqualTo(CopilotCliAvailability.NotFound));
        File.WriteAllText(Path.Combine(_root, "copilot.cmd"), "echo must-not-run");
        Assert.That(LocalCopilotAccountCommands.ProbeAutomaticCandidates(_root, true), Is.EqualTo(CopilotCliAvailability.UnsupportedExecutable));
        Assert.That(Directory.GetFiles(_root), Has.Length.EqualTo(1));
    }

    [TestCase(true, CopilotCliAvailability.Available)]
    [TestCase(false, CopilotCliAvailability.NotExecutable)]
    public void NativeCandidateUsesOnlyReadAndPermissionProbe(bool executable, CopilotCliAvailability expected)
    {
        var path = Path.Combine(_root, OperatingSystem.IsWindows() ? "copilot.exe" : "copilot");
        File.Copy(Environment.ProcessPath!, path);
        var calls = 0;
        var result = LocalCopilotAccountCommands.ProbeCandidate(path, OperatingSystem.IsWindows(), _ => { calls++; return executable; });
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(expected));
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(Directory.GetFiles(_root), Has.Length.EqualTo(1));
        });
    }

    [Test]
    public void ProbeFailureDoesNotExposeExceptionContents()
    {
        var commands = new LocalCopilotAccountCommands(new Configuration(null,
            () => throw new IOException("private-path-and-account-canary")));
        Assert.That(commands.ProbeCli(), Is.EqualTo(CopilotCliAvailability.ProbeFailed));
        Assert.That(commands.IsCliInstalled(), Is.False);
    }
}
