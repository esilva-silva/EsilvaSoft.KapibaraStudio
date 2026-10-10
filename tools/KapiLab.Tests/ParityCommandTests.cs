using System.Text.Json;
using EsilvaSoft.KapibaraStudio.KapiLab.Cli;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Tests;

[TestFixture]
public sealed class ParityCommandTests
{
    [Test]
    public void MatchedObservationProducesVersionedReportAndProvenanceHash()
    {
        using var workspace = new TemporaryWorkspace();
        workspace.Write("data/lab/observations.json", """
            {"schema":"kapilab-parity-observations-v1","level":"tokenizer",
             "expected":{"schema":"kapilab-parity-observation-v1","tokenIds":[1,2]},
             "actual":{"schema":"kapilab-parity-observation-v1","tokenIds":[1,2]}}
            """);

        var result = CaptureStdout(() => ParityCommand.Run("data/lab/observations.json", null, null, workspace.Path));

        Assert.That(result.ExitCode, Is.Zero);
        using var report = JsonDocument.Parse(result.Output);
        Assert.That(report.RootElement.GetProperty("schema").GetString(), Is.EqualTo("kapilab-parity-report-v1"));
        Assert.That(report.RootElement.GetProperty("status").GetString(), Is.EqualTo("matched"));
        Assert.That(report.RootElement.GetProperty("observationsSha256").GetString(), Has.Length.EqualTo(64));
    }

    [Test]
    public void DivergenceReturnsParityMismatchCode()
    {
        using var workspace = new TemporaryWorkspace();
        workspace.Write("data/lab/observations.json", """
            {"schema":"kapilab-parity-observations-v1","level":"tokenizer",
             "expected":{"schema":"kapilab-parity-observation-v1","tokenIds":[1,2]},
             "actual":{"schema":"kapilab-parity-observation-v1","tokenIds":[1,3]}}
            """);

        var result = CaptureStdout(() => ParityCommand.Run("data/lab/observations.json", null, null, workspace.Path));

        Assert.That(result.ExitCode, Is.EqualTo(11));
        using var report = JsonDocument.Parse(result.Output);
        Assert.That(report.RootElement.GetProperty("status").GetString(), Is.EqualTo("mismatched"));
        Assert.That(report.RootElement.GetProperty("differenceCount").GetInt32(), Is.EqualTo(1));
    }

    [Test]
    public void BlindSetInputIsRejected()
    {
        using var workspace = new TemporaryWorkspace();
        workspace.Write("data/eval/blind/observations.json", "{}");

        var result = CaptureStdout(() => ParityCommand.Run("data/eval/blind/observations.json", null, null, workspace.Path));

        Assert.That(result.ExitCode, Is.EqualTo(8));
        Assert.That(result.Output, Is.Empty);
    }

    [Test]
    public void DuplicateCaseVariantPropertiesAreRejectedBeforeDeserialization()
    {
        using var workspace = new TemporaryWorkspace();
        workspace.Write("data/lab/observations.json", """
            {"schema":"kapilab-parity-observations-v1","Schema":"kapilab-parity-observations-v1","level":"tokenizer",
             "expected":{"schema":"kapilab-parity-observation-v1","tokenIds":[1]},
             "actual":{"schema":"kapilab-parity-observation-v1","tokenIds":[1]}}
            """);

        var result = CaptureStdout(() => ParityCommand.Run("data/lab/observations.json", null, null, workspace.Path));

        Assert.That(result.ExitCode, Is.EqualTo(3));
        Assert.That(result.Output, Is.Empty);
    }

    private static (int ExitCode, string Output) CaptureStdout(Func<int> action)
    {
        var original = Console.Out;
        using var output = new StringWriter();
        try
        {
            Console.SetOut(output);
            return (action(), output.ToString());
        }
        finally { Console.SetOut(original); }
    }

    private sealed class TemporaryWorkspace : IDisposable
    {
        public TemporaryWorkspace() => Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kapilab-parity-tests", Guid.NewGuid().ToString("N"));
        public string Path { get; }

        public void Write(string relativePath, string content)
        {
            var path = System.IO.Path.Combine(Path, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
