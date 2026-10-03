using System.Text;
using EsilvaSoft.KapibaraStudio.Core.Agents;

namespace EsilvaSoft.KapibaraStudio.Application.Agents;

/// <summary>UTF-8 textual content only. Contains no contents, file paths, tokenizer guesses or transport sizes.</summary>
public sealed record AgentContextMeasurement(long MessageBytes, long InstructionsBytes, long AuthorizedContextBytes,
    IReadOnlyList<AgentContextMeasuredItem> Items, int PendingItems = 0, int FailedItems = 0)
{
    public long MeasuredBytes => checked(MessageBytes + InstructionsBytes + AuthorizedContextBytes + Items.Sum(static item => item.Bytes));
    public bool IsComplete => PendingItems == 0 && FailedItems == 0;

    public static AgentContextMeasurement Capture(AgentTurnRequest request) => new(
        Encoding.UTF8.GetByteCount(request.UserMessage), Encoding.UTF8.GetByteCount(request.SystemPrompt ?? ""),
        Encoding.UTF8.GetByteCount(request.AuthorizedContext ?? ""), request.Attachments.Select(static attachment =>
            new AgentContextMeasuredItem(attachment.Kind, Encoding.UTF8.GetByteCount(attachment.Content))).ToArray());
}

public sealed record AgentContextMeasuredItem(AgentAttachmentKind Kind, long Bytes);
