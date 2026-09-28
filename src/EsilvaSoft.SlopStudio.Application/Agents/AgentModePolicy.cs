using EsilvaSoft.SlopStudio.Core.Agents;

namespace EsilvaSoft.SlopStudio.Application.Agents;

/// <summary>
/// Platform facts captured by the caller. <see cref="ProductToolsAvailable"/> is false where the authenticated local
/// MCP channel cannot be established (today: Linux, without a transport proof store).
/// </summary>
public sealed record AgentPlatformFacts(bool HasWorkspaceFolder, bool ProductToolsAvailable);

/// <summary>
/// The single decision point that turns (mode, persisted permissions, platform) into an <see cref="AgentTurnPlan"/>.
/// Pure and deterministic. Rules:
/// <list type="bullet">
/// <item>Malformed permissions (see <see cref="AgentProviderPermissions.IsWellFormed"/>: null section, undefined enum,
/// newer format) block the turn (<see cref="AgentTurnBlockReason.InvalidPermissions"/>); nothing is read as allowed.</item>
/// <item>Without persisted consent nothing is sent to an external destination (<see cref="AgentTurnBlockReason.ConsentMissing"/>).</item>
/// <item>Confirmation: <see cref="AgentOperationMode.AskConfirmations"/> confirms every exposed tool;
/// <see cref="AgentOperationMode.Automatic"/> confirms none; Agent/Planning use the persisted categories.
/// Confirmations need the product permission-prompt tool, so where product tools are unavailable a tool that would
/// need a confirmation is dropped instead of running unconfirmed.</item>
/// <item>Native Read/Glob/Grep only inside a workspace folder, when native reads and workspace files are permitted and
/// every exclusion is valid (exclusions become deny rules; a null list means the defaults). While any exclusion is
/// active native Grep is not exposed, because the CLI applies <c>Read</c> rules to Grep only best-effort
/// (<see cref="NoticeGrepDisabledByExclusions"/>).</item>
/// <item><c>propose_file_edit</c> is never exposed in Planning; Automatic applies to the buffer; otherwise review.</item>
/// <item>No MongoDB write tool is ever exposed. The CLI permission mode stays <c>default</c> (not part of the plan).</item>
/// </list>
/// </summary>
public static class AgentModePolicy
{
    public const string NoticeProductToolsUnavailable = "ProductToolsUnavailableOnPlatform";
    public const string NoticeNoWorkspaceFolder = "NativeReadsRequireWorkspaceFolder";
    public const string NoticeInvalidExclusion = "InvalidWorkspaceExclusion";
    public const string NoticeConfirmationUnavailable = "ConfirmationToolUnavailable";
    public const string NoticeGrepDisabledByExclusions = "NativeGrepDisabledByExclusions";

    public static AgentTurnPlan Plan(AgentOperationMode mode, AgentProviderPermissions permissions, AgentPlatformFacts facts,
        bool requireExternalDestinationConsent = true)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentNullException.ThrowIfNull(facts);
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        if (!permissions.IsWellFormed)
        {
            return AgentTurnPlan.Blocked(mode, AgentTurnBlockReason.InvalidPermissions);
        }

        if (requireExternalDestinationConsent && !permissions.HasExternalDestinationConsent)
        {
            return AgentTurnPlan.Blocked(mode, AgentTurnBlockReason.ConsentMissing);
        }

        var notices = new List<string>();
        var requested = mode switch
        {
            AgentOperationMode.AskConfirmations => AgentConfirmationCategories.All,
            AgentOperationMode.Automatic => AgentConfirmationCategories.None,
            _ => permissions.ConfirmationCategories & AgentConfirmationCategories.All,
        };

        // A confirmation is only possible through the product permission-prompt tool (MCP channel).
        bool CanRun(AgentConfirmationCategories category) =>
            (requested & category) == 0 || facts.ProductToolsAvailable;

        var native = PlanNativeTools(permissions, facts, notices, CanRun(AgentConfirmationCategories.NativeFileRead));
        var denyRules = native.Count == 0
            ? []
            : AgentWorkspaceExclusions.ToNativeReadRules(permissions.Workspace.EffectiveExclusions);

        var product = new List<string>();
        if (!facts.ProductToolsAvailable)
        {
            notices.Add(NoticeProductToolsUnavailable);
        }
        else
        {
            var enabled = new HashSet<string>(permissions.EnabledReadTools ?? [], StringComparer.Ordinal);
            foreach (var tool in AgentProductToolNames.ReadTools)
            {
                if (enabled.Contains(tool) && IsReadToolPermitted(tool, permissions))
                {
                    product.Add(tool);
                }
            }

            // A proposal reveals whether its old text applies, so it also needs the data-sending permission of its
            // target: the active file (buffer) or the workspace files.
            if (mode != AgentOperationMode.Planning &&
                (permissions.EditProposals.ActiveFile && permissions.DataSending.ActiveFile ||
                 permissions.EditProposals.OtherWorkspaceFiles && permissions.DataSending.WorkspaceFiles &&
                 permissions.Workspace.UseFilesFolder))
            {
                product.Add(AgentProductToolNames.ProposeFileEdit);
            }
        }

        var handling = !product.Contains(AgentProductToolNames.ProposeFileEdit)
            ? AgentProposalHandling.Disabled
            : mode == AgentOperationMode.Automatic
                ? AgentProposalHandling.AutoApplyToBuffer
                : AgentProposalHandling.ReviewRequired;

        // Only categories that are actually exposed keep a confirmation.
        var exposed = AgentConfirmationCategories.None;
        foreach (var tool in native.Concat(product))
        {
            exposed |= AgentProductToolNames.CategoryOf(tool);
        }

        var confirmations = requested & exposed;
        IReadOnlyList<string> askRules = (confirmations & AgentConfirmationCategories.NativeFileRead) != 0
            ? [.. native]
            : [];

        return new AgentTurnPlan(
            mode,
            [.. native],
            askRules,
            denyRules,
            [.. product],
            handling,
            confirmations != AgentConfirmationCategories.None,
            confirmations)
        {
            AllowedConnectionIds = permissions.ConnectionScope == AgentConnectionScope.Selected
                ? [.. (permissions.SelectedConnectionIds ?? []).Where(static id => id != Guid.Empty).Distinct()]
                : null,
            Notices = [.. notices.Distinct(StringComparer.Ordinal)],
        };
    }

    private static List<string> PlanNativeTools(
        AgentProviderPermissions permissions, AgentPlatformFacts facts, List<string> notices, bool canConfirm)
    {
        if (!permissions.NativeFileRead || !permissions.DataSending.WorkspaceFiles || !permissions.Workspace.UseFilesFolder)
        {
            return [];
        }

        if (!facts.HasWorkspaceFolder)
        {
            notices.Add(NoticeNoWorkspaceFolder);
            return [];
        }

        var exclusions = permissions.Workspace.EffectiveExclusions;
        if (!exclusions.All(AgentWorkspaceExclusions.IsValidPattern))
        {
            // Fail closed: an exclusion that cannot be expressed as a rule must not silently allow reads.
            notices.Add(NoticeInvalidExclusion);
            return [];
        }

        if (!canConfirm)
        {
            notices.Add(NoticeConfirmationUnavailable);
            return [];
        }

        if (exclusions.Count > 0)
        {
            notices.Add(NoticeGrepDisabledByExclusions);
            return [AgentProductToolNames.NativeRead, AgentProductToolNames.NativeGlob];
        }

        return [.. AgentProductToolNames.NativeFileReadTools];
    }

    private static bool IsReadToolPermitted(string tool, AgentProviderPermissions permissions) => tool switch
    {
        AgentProductToolNames.GetCachedSchema => permissions.DataSending.InferredSchema,
        AgentProductToolNames.GetWorkspaceContext => permissions.DataSending.TabMetadata,
        _ => true,
    };
}
