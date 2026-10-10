using EsilvaSoft.KapibaraStudio.KapiLab.Cli;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Tests;

[TestFixture]
public sealed class GuidanceSmokeCommandTests
{
    [Test]
    public void CommandContractAcceptsRequiredCallerInputsAndBoundedOptions()
    {
        var parse = KapiLabCommandLine.Build().Parse([
            "guidance", "smoke", "--package", "package", "--prompt-file", "prompt.txt",
            "--grammar-file", "grammar.lark", "--device", "cpu", "--max-tokens", "64", "--workspace", ".",
        ]);

        Assert.That(parse.Errors, Is.Empty);
    }

    [Test]
    public async Task SmokeHelpListsPackagePromptGrammarDeviceAndGenerationLimit()
    {
        var parse = KapiLabCommandLine.Build().Parse(["guidance", "smoke", "--help"]);
        var original = Console.Out;
        using var output = new StringWriter();
        try
        {
            Console.SetOut(output);
            var exit = await parse.InvokeAsync();
            Assert.That(exit, Is.Zero);
        }
        finally { Console.SetOut(original); }

        Assert.Multiple(() =>
        {
            Assert.That(parse.Errors, Is.Empty);
            Assert.That(output.ToString(), Does.Contain("--package"));
            Assert.That(output.ToString(), Does.Contain("--prompt-file"));
            Assert.That(output.ToString(), Does.Contain("--grammar-file"));
            Assert.That(output.ToString(), Does.Contain("--device"));
            Assert.That(output.ToString(), Does.Contain("--max-tokens"));
        });
    }

    [Test]
    public async Task MissingPackageReturnsStructuredNotExecutedResultWithoutEchoingFileContents()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "kapilab-guidance-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        var prompt = "caller-private-prompt-content";
        var grammar = "caller-private-grammar-content";
        await File.WriteAllTextAsync(Path.Combine(workspace, "prompt.txt"), prompt);
        await File.WriteAllTextAsync(Path.Combine(workspace, "grammar.lark"), grammar);
        var original = Console.Out;
        using var output = new StringWriter();
        int exit;
        try
        {
            Console.SetOut(output);
            exit = await GuidanceSmokeCommand.RunAsync("missing-package", "prompt.txt", "grammar.lark", "cpu", 64, workspace);
        }
        finally
        {
            Console.SetOut(original);
            Directory.Delete(workspace, recursive: true);
        }

        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.EqualTo(4));
            Assert.That(output.ToString(), Does.Contain("\"executed\":false"));
            Assert.That(output.ToString(), Does.Contain("\"path\":\"genai-direct\""));
            Assert.That(output.ToString(), Does.Not.Contain(prompt));
            Assert.That(output.ToString(), Does.Not.Contain(grammar));
            Assert.That(output.ToString(), Does.Contain("pacote ausente"));
        });
    }
}
