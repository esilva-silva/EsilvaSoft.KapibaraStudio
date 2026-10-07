using System.Buffers.Binary;
using EsilvaSoft.KapibaraStudio.SystemAdapters.Copilot;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests.Copilot;

[TestFixture, Category("Integration")]
public sealed class CopilotNativeExecutableDiscoveryTests
{
    private string _root = null!;

    [SetUp]
    public void CreateRoot()
    {
        _root = Path.Combine(Path.GetTempPath(), "KapibaraStudioNativeCliTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void RemoveRoot()
    {
        var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "KapibaraStudioNativeCliTests"));
        var root = Path.GetFullPath(_root);
        if (root.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            Directory.Delete(root, recursive: true);
    }

    [TestCase("#!/bin/sh\ntouch must-not-be-created\n", false)]
    [TestCase("@echo must-not-run", true)]
    [TestCase("MZ", true)]
    [TestCase("\u007fELF", false)]
    public void ShellShimsAndTruncatedNativeHeadersAreRejectedWithoutRunning(string contents, bool windows)
    {
        var candidate = Path.Combine(_root, windows ? "copilot.exe" : "copilot");
        File.WriteAllText(candidate, contents);
        Assert.That(NativeCopilotExecutableProbe.IsNativeExecutable(candidate, windows), Is.False);
        Assert.That(Directory.GetFiles(_root), Has.Length.EqualTo(1));
    }

    [TestCase(0x0002, true)]
    [TestCase(0x2002, false)]
    [TestCase(0x0000, false)]
    public void WindowsCandidateMustHavePeSignatureAndExecutableRatherThanLibraryHeader(int characteristics, bool expected)
    {
        var candidate = Path.Combine(_root, "copilot.exe");
        var image = new byte[88];
        image[0] = (byte)'M';
        image[1] = (byte)'Z';
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(60), 64);
        image[64] = (byte)'P';
        image[65] = (byte)'E';
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(86), (ushort)characteristics);
        File.WriteAllBytes(candidate, image);
        Assert.That(NativeCopilotExecutableProbe.IsNativeExecutable(candidate, true), Is.EqualTo(expected));
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(60), int.MaxValue);
        File.WriteAllBytes(candidate, image);
        Assert.That(NativeCopilotExecutableProbe.IsNativeExecutable(candidate, true), Is.False);
    }

    [TestCase(2, 1, true)]
    [TestCase(3, 1, true)]
    [TestCase(3, 2, true)]
    [TestCase(1, 1, false)]
    [TestCase(4, 1, false)]
    public void LinuxCandidateMustBeAnElfExecutableWithKnownByteOrder(int type, int byteOrder, bool expected)
    {
        var candidate = Path.Combine(_root, "copilot");
        var image = new byte[64];
        image[0] = 0x7f;
        image[1] = (byte)'E';
        image[2] = (byte)'L';
        image[3] = (byte)'F';
        image[4] = 2;
        image[5] = (byte)byteOrder;
        image[6] = 1;
        if (byteOrder == 1) BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(16), (ushort)type);
        else BinaryPrimitives.WriteUInt16BigEndian(image.AsSpan(16), (ushort)type);
        File.WriteAllBytes(candidate, image);
        Assert.That(NativeCopilotExecutableProbe.IsNativeExecutable(candidate, false), Is.EqualTo(expected));
    }

    [TestCase(1, 20, false)]
    [TestCase(1, 51, false)]
    [TestCase(1, 52, true)]
    [TestCase(2, 20, false)]
    [TestCase(2, 63, false)]
    [TestCase(2, 64, true)]
    public void ElfCandidateRequiresCompleteHeaderForItsDeclaredClass(int elfClass, int length, bool expected)
    {
        var candidate = Path.Combine(_root, "copilot");
        var image = new byte[length];
        image[0] = 0x7f;
        image[1] = (byte)'E';
        image[2] = (byte)'L';
        image[3] = (byte)'F';
        image[4] = (byte)elfClass;
        image[5] = 1;
        image[6] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(16), 2);
        // The complete cases carry the class's declared header size. They are synthetic headers, not runnable CLIs.
        var headerSizeOffset = elfClass == 1 ? 40 : 52;
        if (length >= headerSizeOffset + 2)
            BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(headerSizeOffset), (ushort)(elfClass == 1 ? 52 : 64));
        File.WriteAllBytes(candidate, image);

        Assert.That(NativeCopilotExecutableProbe.IsNativeExecutable(candidate, false), Is.EqualTo(expected));
        Assert.That(Directory.GetFiles(_root), Has.Length.EqualTo(1), "Detection only reads the synthetic candidate.");
    }

    [Test]
    public void InvalidSavedExecutableDoesNotSelectAnotherNativeInstallation()
    {
        var candidate = Path.Combine(_root, OperatingSystem.IsWindows() ? "copilot.exe" : "copilot");
        File.WriteAllText(candidate, "#!/bin/sh\necho must-not-run\n");
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(candidate, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        var configuration = new LocalCopilotCliConfiguration { ExecutablePath = candidate };
        Assert.Throws<InvalidOperationException>(() => configuration.ResolveExecutablePath());
        Assert.That(new LocalCopilotAccountCommands(configuration).IsCliInstalled(), Is.False);
        Assert.That(configuration.ExecutablePath, Is.EqualTo(candidate));
    }

    [Test]
    public void AutomaticPathDiscoverySkipsShimAndContinuesToNativeExecutable()
    {
        var shimDirectory = Path.Combine(_root, "shim");
        var nativeDirectory = Path.Combine(_root, "native");
        Directory.CreateDirectory(shimDirectory);
        Directory.CreateDirectory(nativeDirectory);
        var executableName = OperatingSystem.IsWindows() ? "copilot.exe" : "copilot";
        var shim = Path.Combine(shimDirectory, executableName);
        var native = Path.Combine(nativeDirectory, executableName);
        File.WriteAllText(shim, "#!/bin/sh\nprintf 'must-not-run'\n");
        File.Copy(Environment.ProcessPath!, native);

        var resolved = LocalCopilotCliConfiguration.Resolve(null,
            string.Join(Path.PathSeparator, shimDirectory, nativeDirectory), null,
            OperatingSystem.IsWindows(), executableProbe: _ => true);

        Assert.That(resolved, Is.EqualTo(native),
            "Automatic PATH discovery must reject a non-native shim and continue to a later valid executable.");
    }

    [Test]
    public void MalformedOrInaccessiblePathEntryDoesNotHideLaterCandidate()
    {
        var inaccessible = Path.Combine(_root, "inaccessible");
        var valid = Path.Combine(_root, "valid");
        var expected = Path.Combine(valid, "copilot.exe");
        var path = string.Join(Path.PathSeparator, _root + '\0', inaccessible, valid);
        var result = LocalCopilotAccountCommands.FindCliExecutable(path, true, candidate =>
        {
            if (candidate.StartsWith(inaccessible, StringComparison.Ordinal)) throw new UnauthorizedAccessException();
            return candidate == expected;
        }, _ => true);
        Assert.That(result, Is.EqualTo(expected));
    }
}
