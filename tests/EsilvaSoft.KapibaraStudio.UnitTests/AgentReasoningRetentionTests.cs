using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

public sealed class AgentReasoningRetentionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public void ReasoningIsAbsentByDefaultAndOnlyOptInAddsAnExpiringEntry()
    {
        var conversation = CreateConversation();
        var reasoning = Reasoning(Now.AddDays(7));
        conversation.Items.Add(reasoning);

        var defaultRecord = conversation.ToRecord(Now);
        Assert.That(defaultRecord.Entries.Any(entry => entry.Kind == AgentConversationEntryKind.ReasoningText), Is.False);
        Assert.That(defaultRecord.Entries.Single().ToolName, Is.EqualTo(AgentChatNoticeItem.ReasoningNotSavedKey));

        conversation.AllowReasoningPersistence = true;
        var record = conversation.ToRecord(Now);
        Assert.That(record.Entries.Select(entry => entry.Kind), Is.EqualTo(new[] { AgentConversationEntryKind.ReasoningText }));
        Assert.That(record.Entries[0].ExpiresAtUtc, Is.EqualTo(Now.AddDays(7)));
    }

    [Test]
    public void ExpiredReasoningIsNeitherSerializedNorRestored()
    {
        var conversation = CreateConversation();
        conversation.AllowReasoningPersistence = true;
        conversation.Items.Add(Reasoning(Now));

        var record = conversation.ToRecord(Now.AddTicks(1));
        Assert.That(record.Entries.Any(entry => entry.Kind == AgentConversationEntryKind.ReasoningText), Is.False);
        Assert.That(record.Entries.Single().ToolName, Is.EqualTo(AgentChatNoticeItem.ReasoningNotSavedKey));

        var expired = new AgentConversationEntry(AgentConversationEntryKind.ReasoningText, "privado", Now.AddDays(-7))
        {
            ExpiresAtUtc = Now,
        };
        var restored = AgentChatConversation.FromRecord(Record(expired), _ => null, now: Now.AddTicks(1));
        Assert.That(restored.Items.OfType<AgentReasoningItem>(), Is.Empty);
        Assert.That(restored.Items.OfType<AgentChatNoticeItem>().Single().IsWarning, Is.True);
    }

    [TestCase("get_query_results")]
    [TestCase("get_query_diagnostics")]
    public void CapturedDocumentToolsTaintConversationAndExcludeAllReasoning(string toolName)
    {
        var conversation = CreateConversation();
        conversation.AllowReasoningPersistence = true;
        conversation.Items.Add(Reasoning(Now.AddDays(1)));
        conversation.Items.Add(new AgentToolCallItem(AgentToolCallId.New(), toolName));

        Assert.That(conversation.HasCapturedDocumentTool, Is.True);
        var entries = conversation.ToRecord(Now).Entries;
        Assert.That(entries.Any(entry => entry.Kind == AgentConversationEntryKind.ReasoningText), Is.False);
        Assert.That(entries.Any(entry => entry.Kind == AgentConversationEntryKind.ToolCall && entry.ToolName == toolName), Is.True);
        Assert.That(entries.Any(entry => entry.ToolName == AgentChatNoticeItem.ReasoningNotSavedKey), Is.True);
    }

    private static AgentChatConversation CreateConversation() =>
        new(Guid.NewGuid(), LocalAgentProvider.Id, "model", AgentOperationMode.Agent, Now);

    private static AgentReasoningItem Reasoning(DateTimeOffset expiresAt) => new()
    {
        Content = "analysis only",
        Timestamp = Now,
        RetentionExpiresAtUtc = expiresAt,
        IsRetentionEligible = true,
        IsStreaming = false,
    };

    private static AgentConversation Record(AgentConversationEntry entry) => new(Guid.NewGuid(), LocalAgentProvider.Id,
        "conversation", "model", AgentOperationMode.Agent, null, Now, Now, 1, [entry]);
}
