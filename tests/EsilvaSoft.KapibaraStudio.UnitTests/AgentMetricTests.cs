using System.Text;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core.Agents;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class AgentMetricTests
{
    [Test]
    public void Utf8MeasurementUsesActualRedactedSnapshotRatherThanUntrustedDescriptorSize()
    {
        var attachment = new AgentContextAttachment(AgentAttachmentKind.ActiveFile, "buffer", null, "ação 中文 [redigido]", 9999, "hash");
        var request = new AgentTurnRequest(AgentTurnId.New(), "Olá 世界", "origin", 4, "metadados")
        { SystemPrompt = "instruções", Attachments = [attachment] };
        var measured = AgentContextMeasurement.Capture(request);
        request = request with { UserMessage = "changed", Attachments = [] };
        Assert.Multiple(() =>
        {
            Assert.That(measured.MessageBytes, Is.EqualTo(Encoding.UTF8.GetByteCount("Olá 世界")));
            Assert.That(measured.Items.Single().Bytes, Is.EqualTo(Encoding.UTF8.GetByteCount(attachment.Content)));
            Assert.That(measured.MeasuredBytes, Is.EqualTo(Encoding.UTF8.GetByteCount("Olá 世界metadadosinstruçõesação 中文 [redigido]")));
            Assert.That(measured.IsComplete, Is.True);
            Assert.That(request.Attachments, Is.Empty);
        });
    }

    [Test]
    public void DuplicatedAndOutOfOrderCallTotalsNeverDoubleCountAndTurnTotalSupersedesCalls()
    {
        var usage = new AgentUsageAccumulator();
        var first = new AgentUsageMetrics("call-a", AgentUsageScope.CallTotal, 2, "fixture", 100, 20, 30, 4);
        Assert.That(usage.Observe(first, DateTimeOffset.UnixEpoch), Is.True);
        Assert.That(usage.Observe(first, DateTimeOffset.UnixEpoch), Is.False);
        Assert.That(usage.Observe(first with { Revision = 1, InputTokens = 999 }, DateTimeOffset.UnixEpoch), Is.False);
        usage.Observe(first with { ObservationId = "call-b", InputTokens = 50, OutputTokens = 0 }, DateTimeOffset.UnixEpoch);
        Assert.That((usage.Total!.InputTokens, usage.Total.OutputTokens), Is.EqualTo((150L, 20L)));
        usage.Observe(first with { ObservationId = "turn", Scope = AgentUsageScope.TurnTotal, InputTokens = 180 }, DateTimeOffset.UnixEpoch);
        Assert.That(usage.Total!.InputTokens, Is.EqualTo(180));
        Assert.That(usage.Total.CacheReadTokens, Is.EqualTo(30), "Cache is separate; it is not added to input.");
    }

    [Test]
    public void PartialLaterCallRetainsKnownConsumptionInsteadOfErasingEarlierNumbers()
    {
        var usage = new AgentUsageAccumulator();
        usage.Observe(new("first", AgentUsageScope.CallTotal, 0, "fixture", 10, 3), DateTimeOffset.UnixEpoch);
        usage.Observe(new("second", AgentUsageScope.CallTotal, 0, "fixture", null, 2), DateTimeOffset.UnixEpoch);
        usage.Finish(AgentTurnOutcome.Completed);
        Assert.That(usage.Total!.InputTokens, Is.EqualTo(10));
        Assert.That(usage.Total.OutputTokens, Is.EqualTo(5));
        Assert.That(usage.IsPartial, Is.True);
    }

    [Test]
    public void InvalidValuesOverflowMissingDataAndCancellationDoNotBecomeZeroOrSuccess()
    {
        var usage = new AgentUsageAccumulator();
        var valid = new AgentUsageMetrics("a", AgentUsageScope.CallTotal, 0, "fixture", long.MaxValue, 0);
        Assert.That(usage.Observe(valid with { InputTokens = -1 }, DateTimeOffset.UnixEpoch), Is.False);
        Assert.That(usage.Total, Is.Null);
        Assert.That(usage.Observe(valid, DateTimeOffset.UnixEpoch), Is.True);
        Assert.That(usage.Observe(valid with { ObservationId = "b", InputTokens = 1 }, DateTimeOffset.UnixEpoch), Is.False);
        usage.Finish(AgentTurnOutcome.Cancelled);
        Assert.That(usage.Total!.OutputTokens, Is.Zero, "Official zero is valid.");
        Assert.That(usage.Total.CacheReadTokens, Is.Null, "Absence remains unknown.");
        Assert.That(usage.IsPartial, Is.True);
        Assert.That(usage.Total.InputTokens, Is.EqualTo(long.MaxValue));
    }
}
