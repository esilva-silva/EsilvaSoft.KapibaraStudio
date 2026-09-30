using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Infrastructure;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture, Category("Unit")]
public sealed class OperationEnvironmentMemoryTests
{
    [Test]
    public void CapturesHostOnceAndDoesNotObserveLaterMutations()
    {
        var host = new MemoryHost();
        host.Values["captured"] = "before";
        var operation = new OperationEnvironment(null, null, Guid.NewGuid(), hostEnvironment: host);
        host.Values["captured"] = "after";
        Assert.Multiple(() =>
        {
            Assert.That(operation.GetLegacy("captured"), Is.EqualTo("before"));
            Assert.That(operation.ScriptValues["captured"], Is.EqualTo("before"));
            Assert.That(host.Captures, Is.EqualTo(1));
        });
    }

    [Test]
    public void VaultOverridesCapturedHostForScriptValues()
    {
        using var repository = new MemoryWorkspaceRepository();
        var vault = repository.LoadEnvironments();
        vault.Environments[0].Values["shared"] = "vault";
        repository.SaveEnvironments(vault);
        var host = new MemoryHost();
        host.Values["shared"] = "host";
        var operation = new OperationEnvironment(repository, null, Guid.NewGuid(), hostEnvironment: host);
        Assert.Multiple(() =>
        {
            Assert.That(operation.Get("shared"), Is.EqualTo("vault"));
            Assert.That(operation.GetLegacy("shared"), Is.EqualTo("host"));
            Assert.That(operation.ScriptValues["shared"], Is.EqualTo("vault"));
        });
    }

    [Test]
    public void AbsentHostAdapterHasNoAmbientFallback()
    {
        var operation = new OperationEnvironment(null, null, Guid.NewGuid());
        Assert.Multiple(() =>
        {
            Assert.That(operation.GetLegacy("PATH"), Is.Null);
            Assert.That(operation.ScriptValues, Is.Empty);
        });
    }

    [TestCase(true, "path", "PATH")]
    [TestCase(false, "Path", null)]
    public void CapturedEnvironmentUsesTheInjectedPlatformNameComparer(bool windows, string lookup, string? expected)
    {
        var host = new MemoryHost();
        host.Values["PATH"] = "captured-path";
        var operation = new OperationEnvironment(null, null, Guid.NewGuid(), hostEnvironment: host,
            hostPlatform: new FakePlatform(windows));

        Assert.That(operation.GetLegacy(lookup), Is.EqualTo(expected is null ? null : "captured-path"));
    }

    private sealed class MemoryHost : IHostEnvironmentSnapshot
    {
        public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);
        public int Captures { get; private set; }
        public IReadOnlyDictionary<string, string> Capture()
        {
            Captures++;
            return Values;
        }
    }

    private sealed record FakePlatform(bool IsWindows) : IHostPlatformSnapshot
    {
        public bool IsLinux => !IsWindows;
    }
}
