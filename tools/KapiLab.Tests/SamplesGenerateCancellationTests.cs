using EsilvaSoft.KapibaraStudio.KapiLab.Cli;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Tests;

[TestFixture]
public sealed class SamplesGenerateCancellationTests
{
    private static readonly string[] StdoutRecords = ["record one", "record two"];

    [Test]
    public async Task CancellationDuringGenerationReturnsCancelledWithoutPublishingPartialStdout()
    {
        var workspace = CreateWorkspace();
        var originalOut = Console.Out;
        var capture = new StringWriter();
        using var cancellation = new CancellationTokenSource();
        Console.SetOut(capture);
        try
        {
            var exit = await SamplesGenerateCommand.RunAsync(17, 3, null, null, workspace,
                (_, _, _, _) =>
                {
                    cancellation.Cancel();
                    return ["first generated record", "partial second record"];
                }, cancellation.Token);

            Assert.Multiple(() =>
            {
                Assert.That(exit, Is.EqualTo(12));
                Assert.That(capture.ToString(), Is.Empty);
            });
        }
        finally
        {
            Console.SetOut(originalOut);
            capture.Dispose();
            Directory.Delete(workspace, recursive: true);
        }
    }

    [Test]
    public async Task CancellationDuringGenerationDoesNotCreateOrReplaceOutputArtifact()
    {
        var workspace = CreateWorkspace();
        const string relativeOutput = "data/lab/samples.jsonl";
        var output = Path.Combine(workspace, "data", "lab", "samples.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        await File.WriteAllTextAsync(output, "keep existing artifact\n");
        using var cancellation = new CancellationTokenSource();
        try
        {
            var exit = await SamplesGenerateCommand.RunAsync(17, 3, null, relativeOutput, workspace,
                (_, _, _, _) =>
                {
                    cancellation.Cancel();
                    return ["partially generated record"];
                }, cancellation.Token);

            Assert.Multiple(() =>
            {
                Assert.That(exit, Is.EqualTo(12));
                Assert.That(File.ReadAllText(output), Is.EqualTo("keep existing artifact\n"));
                Assert.That(Directory.GetFiles(Path.GetDirectoryName(output)!), Has.Length.EqualTo(1));
            });
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    [Test]
    public async Task CancellationAfterStdoutFlushBeginsFinishesTheWholeBatch()
    {
        var workspace = CreateWorkspace();
        var originalOut = Console.Out;
        using var cancellation = new CancellationTokenSource();
        using var capture = new CancellingWriter(cancellation);
        Console.SetOut(capture);
        try
        {
            var expected = string.Join("\n", StdoutRecords) + "\n";
            var exit = await SamplesGenerateCommand.RunAsync(17, 2, null, null, workspace,
                (_, _, _, _) => StdoutRecords, cancellation.Token);

            Assert.Multiple(() =>
            {
                Assert.That(exit, Is.EqualTo(0));
                Assert.That(capture.ToString(), Is.EqualTo(expected));
            });
        }
        finally
        {
            Console.SetOut(originalOut);
            Directory.Delete(workspace, recursive: true);
        }
    }

    private static string CreateWorkspace()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "kapilab-cancel-samples-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        return workspace;
    }

    private sealed class CancellingWriter(CancellationTokenSource cancellation) : StringWriter
    {
        public override Task WriteAsync(string? value)
        {
            var write = base.WriteAsync(value);
            cancellation.Cancel();
            return write;
        }
    }
}
