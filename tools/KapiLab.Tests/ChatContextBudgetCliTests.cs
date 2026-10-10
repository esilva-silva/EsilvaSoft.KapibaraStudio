using EsilvaSoft.KapibaraStudio.KapiLab.Cli;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Tests;

[TestFixture]
public sealed class ChatContextBudgetCliTests
{
    [TestCase("model", "run", "chat")]
    [TestCase("bench", "chat", null)]
    public void ChatCommandsAcceptAnOptionalContextBudget(string first, string second, string? third)
    {
        var command = KapiLabCommandLine.Build();
        var args = third is null
            ? new[] { first, second, "--package", "package", "--in", "input.jsonl" }
            : new[] { first, second, third, "--package", "package", "--in", "input.jsonl" };
        var defaultParse = command.Parse(args);
        var explicitParse = command.Parse([.. args, "--context-tokens", "1024"]);

        Assert.Multiple(() =>
        {
            Assert.That(defaultParse.Errors, Is.Empty);
            Assert.That(explicitParse.Errors, Is.Empty);
        });
    }
}
