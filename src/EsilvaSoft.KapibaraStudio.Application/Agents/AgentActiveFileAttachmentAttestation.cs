using EsilvaSoft.KapibaraStudio.Core.Agents;

namespace EsilvaSoft.KapibaraStudio.Application.Agents;

/// <summary>Proves that active-buffer proposals use the exact redacted editor snapshot sent to the provider.</summary>
internal static class AgentActiveFileAttachmentAttestation
{
    public static bool HasActiveFileAttachment(AgentTurnRequest request) =>
        request.Attachments.Any(static attachment => attachment.Kind == AgentAttachmentKind.ActiveFile);

    public static bool MatchesCapturedBuffer(AgentTurnRequest request)
    {
        var attachments = request.Attachments
            .Where(static attachment => attachment.Kind == AgentAttachmentKind.ActiveFile)
            .Take(2)
            .ToArray();
        var snapshot = request.WorkspaceContext;
        if (attachments.Length != 1 || snapshot is null || snapshot.BufferText is null ||
            string.IsNullOrWhiteSpace(snapshot.TabId) || snapshot.DocumentVersion is null ||
            !string.Equals(snapshot.TabId, request.TabId, StringComparison.Ordinal) ||
            snapshot.DocumentVersion.Value != request.DocumentVersion)
        {
            return false;
        }

        try
        {
            var redactedBuffer = AgentContextProvider.Redact(snapshot.BufferText) ?? string.Empty;
            return string.Equals(attachments[0].Content, redactedBuffer, StringComparison.Ordinal);
        }
        catch (AgentRuntimeException)
        {
            return false;
        }
    }
}
