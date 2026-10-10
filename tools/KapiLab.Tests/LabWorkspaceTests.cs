using EsilvaSoft.KapibaraStudio.KapiLab.Core;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Tests;

[TestFixture]
public sealed class LabWorkspaceTests
{
    [Test]
    public void BlindDatasetInputIsRejectedWhenWorkspaceIsSelected()
    {
        using var workspace = new TemporaryWorkspace();
        var blindPath = Path.Combine(workspace.Path, "data", "eval", "blind", "cases.json");
        Directory.CreateDirectory(Path.GetDirectoryName(blindPath)!);
        File.WriteAllText(blindPath, "{}");

        Assert.That(() => LabWorkspace.RefuseBlindInput(blindPath, workspace.Path), Throws.TypeOf<UnauthorizedAccessException>());
    }

    [TestCase("reports/lab/./run.json", "reports/lab/run.json")]
    [TestCase("reports/lab/run.json", "reports/lab/run.json")]
    [TestCase("reports/lab/sub/../run.json", "reports/lab/run.json")]
    public void OutputCannotOverwriteAnInputArtifactThroughPathAliases(string output, string input)
    {
        using var workspace = new TemporaryWorkspace();

        Assert.That(() => LabWorkspace.RefuseInputOverwrite(workspace.Path, output, [input]),
            Throws.TypeOf<UnauthorizedAccessException>());
    }

    [TestCase("../data/generated/output.jsonl")]
    [TestCase("C:/external/result.jsonl")]
    [TestCase("data/eval/blind/result.jsonl")]
    public void OutputOutsideLabRootsIsRejected(string output)
    {
        using var workspace = new TemporaryWorkspace();

        Assert.That(() => LabWorkspace.ResolveOutput(workspace.Path, output), Throws.TypeOf<UnauthorizedAccessException>());
    }

    [Test]
    public void OutputMayReplaceASeparateLabArtifact()
    {
        using var workspace = new TemporaryWorkspace();

        Assert.DoesNotThrow(() => LabWorkspace.RefuseInputOverwrite(workspace.Path,
            "reports/lab/output.json", ["data/lab/input.json"]));
    }

    [Test]
    public void ResolveRequiresAnExplicitWorkspaceOrEnvironmentOverride()
    {
        var original = Environment.GetEnvironmentVariable("KAPILAB_WORKSPACE");
        try
        {
            Environment.SetEnvironmentVariable("KAPILAB_WORKSPACE", null);
            Assert.That(LabWorkspace.Resolve(null), Is.Null);
            using var workspace = new TemporaryWorkspace();
            Environment.SetEnvironmentVariable("KAPILAB_WORKSPACE", workspace.Path);
            Assert.That(LabWorkspace.Resolve(null), Is.EqualTo(Path.GetFullPath(workspace.Path)));
        }
        finally { Environment.SetEnvironmentVariable("KAPILAB_WORKSPACE", original); }
    }

    [TestCase("reports/lab/run/report.json")]
    [TestCase("tmp/output.tmp")]
    [TestCase("data/lab/render.jsonl")]
    public void OutputCanBeWrittenOnlyToDeclaredLabRoots(string output)
    {
        using var workspace = new TemporaryWorkspace();

        var resolved = LabWorkspace.ResolveOutput(workspace.Path, output);

        Assert.That(resolved, Is.EqualTo(Path.GetFullPath(output, workspace.Path)));
    }

    [Test]
    public void LimitedReadRejectsOversizeBeforeReturningAndRequiresStrictUtf8()
    {
        var path = Path.Combine(Path.GetTempPath(), "kapilab-limited-read-" + Guid.NewGuid().ToString("N"));
        try
        {
            File.WriteAllBytes(path, [0x61, 0x62, 0x63, 0x64]);
            Assert.That(LabWorkspace.ReadUtf8FileLimited(path, 4), Is.EqualTo("abcd"));
            Assert.That(() => LabWorkspace.ReadUtf8FileLimited(path, 3), Throws.TypeOf<InvalidDataException>());

            File.WriteAllBytes(path, [0xC3, 0x28]);
            Assert.That(() => LabWorkspace.ReadUtf8FileLimited(path, 2), Throws.TypeOf<System.Text.DecoderFallbackException>());
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private sealed class TemporaryWorkspace : IDisposable
    {
        public TemporaryWorkspace()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kapilab-workspace-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
