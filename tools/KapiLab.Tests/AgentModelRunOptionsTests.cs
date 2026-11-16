using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.KapiLab.Core;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Tests;

/// <summary>
/// KS-US10: opções de medição do <c>agent run</c>. Validação, escolha de raciocínio por tarefa e resumo são puros; rodar
/// um pacote real continua manual.
/// </summary>
[TestFixture]
public sealed class AgentModelRunOptionsTests
{
    private static readonly int[] ThreeSeeds = [1, 2, 3];

    private static AgentModelRunCommand.AgentTask Task(int? budget = null) =>
        new("t", "liste os bancos", "kapi_sim", "pedidos", ReasoningBudgetTokens: budget);

    private static AgentModelRunCommand.TaskResult Result(bool passed, int? seed = null, double? firstAnswerMs = null,
        string? error = null, int truncatedBlocks = 0, params AgentModelRunCommand.ToolCall[] calls) =>
        new("t", passed ? "Completed" : "Failed", error, calls, "", 0, null, null, 0, 100, true, true, true, true, true, passed, [], [], [])
        {
            Seed = seed, FirstAnswerMs = firstAnswerMs, ReasoningBlocks = truncatedBlocks, ReasoningBlocksTruncated = truncatedBlocks,
        };

    [Test]
    public void DefaultsKeepTheRunAsItWasBeforeTheOptions()
    {
        Assert.That(AgentModelRunCommand.TryParseRunOptions(null, null, null, out var options, out var error), Is.True, error);
        Assert.Multiple(() =>
        {
            Assert.That(options.Seeds, Is.Empty);
            Assert.That(options.Thinking, Is.Null);
            Assert.That(options.ThinkBudgetTokens, Is.Null);
            Assert.That(AgentModelRunCommand.ReasoningFor(Task(), options), Is.Null, "Sem opção nem orçamento na tarefa, vale o padrão do pacote.");
            Assert.That(AgentModelRunCommand.ReasoningFor(Task(256), options), Is.EqualTo(new AgentReasoningOptions(true, 256)));
        });
    }

    [Test]
    public void SeedsAreParsedInOrderAndBounded()
    {
        Assert.That(AgentModelRunCommand.TryParseRunOptions(" 1, 2,3 ", "default", null, out var options, out var error), Is.True, error);
        Assert.That(options.Seeds, Is.EqualTo(ThreeSeeds));
    }

    [TestCase("", null, null, "ao menos uma")]
    [TestCase("1,1", null, null, "repetidas")]
    [TestCase("1,-2", null, null, "inteiros")]
    [TestCase("1,x", null, null, "inteiros")]
    [TestCase("1,2,3,4,5,6,7,8,9,10,11", null, null, "até 10")]
    [TestCase(null, "sempre", null, "default, on ou off")]
    [TestCase(null, "on", 0, "entre 1 e 8192")]
    [TestCase(null, "on", 8193, "entre 1 e 8192")]
    [TestCase(null, "off", 512, "não se aplica")]
    public void InvalidOptionsAreRefusedWithTheReason(string? seeds, string? thinking, int? budget, string reason)
    {
        Assert.That(AgentModelRunCommand.TryParseRunOptions(seeds, thinking, budget, out _, out var error), Is.False);
        Assert.That(error, Does.Contain(reason));
    }

    [Test]
    public void TheRunModeOverridesTheTaskAndTheBudgetAppliesToEveryTask()
    {
        Assert.That(AgentModelRunCommand.TryParseRunOptions(null, "off", null, out var off, out _), Is.True);
        Assert.That(AgentModelRunCommand.TryParseRunOptions(null, "on", null, out var on, out _), Is.True);
        Assert.That(AgentModelRunCommand.TryParseRunOptions(null, null, 1024, out var budget, out _), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(AgentModelRunCommand.ReasoningFor(Task(256), off), Is.EqualTo(new AgentReasoningOptions(false)),
                "Desligado vale também para a tarefa que pedia orçamento; o provider decide se o pacote aceita.");
            Assert.That(AgentModelRunCommand.ReasoningFor(Task(), on), Is.EqualTo(new AgentReasoningOptions(true)));
            Assert.That(AgentModelRunCommand.ReasoningFor(Task(256), on), Is.EqualTo(new AgentReasoningOptions(true, 256)));
            Assert.That(AgentModelRunCommand.ReasoningFor(Task(256), budget), Is.EqualTo(new AgentReasoningOptions(true, 1024)));
            Assert.That(AgentModelRunCommand.ReasoningFor(Task(), budget), Is.EqualTo(new AgentReasoningOptions(true, 1024)));
        });
    }

    [Test]
    public void TheSummaryReportsPassRatePerSeedFirstAnswerTimesAndFailureKinds()
    {
        Assert.That(AgentModelRunCommand.TryParseRunOptions("7,9", null, null, out var options, out _), Is.True);
        var results = new[]
        {
            Result(true, seed: 7, firstAnswerMs: 100, calls: new AgentModelRunCommand.ToolCall("list_databases", "Succeeded", null)),
            Result(true, seed: 7, firstAnswerMs: 300, truncatedBlocks: 1),
            Result(true, seed: 9, firstAnswerMs: 200),
            Result(false, seed: 9, error: "InvalidToolCallFormat"),
        };

        var summary = AgentModelRunCommand.Summarize(results, options);

        Assert.Multiple(() =>
        {
            Assert.That((summary.Tasks, summary.Passed, summary.PassRate), Is.EqualTo((4, 3, 0.75)));
            Assert.That(summary.BySeed, Is.EqualTo(new[]
            {
                new AgentModelRunCommand.SeedSummary(7, 2, 2, 1), new AgentModelRunCommand.SeedSummary(9, 2, 1, 0.5),
            }));
            Assert.That(summary.FirstAnswerMsMean, Is.EqualTo(200), "Só as execuções que responderam entram na média.");
            Assert.That(summary.FirstAnswerMsP95, Is.EqualTo(300), "Com poucas execuções o percentil é o maior valor observado.");
            Assert.That(summary.InvalidToolCallTurns, Is.EqualTo(1));
            Assert.That(summary.ReasoningBudgetExceededTurns, Is.EqualTo(1));
            Assert.That((summary.ToolCalls, summary.ToolCallsSucceeded), Is.EqualTo((1, 1)));
        });
    }

    [Test]
    public void ARunWithoutAnswersOrSeedsHasNoTimingAndNoSeedBreakdown()
    {
        var summary = AgentModelRunCommand.Summarize([Result(false), Result(false)], AgentModelRunCommand.RunOptions.Default);

        Assert.Multiple(() =>
        {
            Assert.That(summary.FirstAnswerMsMean, Is.Null);
            Assert.That(summary.FirstAnswerMsP95, Is.Null);
            Assert.That(summary.BySeed, Is.Empty);
            Assert.That(summary.PassRate, Is.Zero);
        });
    }
}
