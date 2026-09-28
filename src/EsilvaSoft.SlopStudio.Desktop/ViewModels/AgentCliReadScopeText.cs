using EsilvaSoft.SlopStudio.Desktop.Agents;

namespace EsilvaSoft.SlopStudio.Desktop.ViewModels;

/// <summary>
/// Localized read notice of a CLI-delegated provider, built from the provider's own working-directory preview:
/// accepted workspace folder, no folder, or a refused folder with the reason (protected area named, never its
/// content), always stating that what is read goes to the recipient. Shared by the chat and the settings.
/// </summary>
internal static class AgentCliReadScopeText
{
    private static LocalizationViewModel Text => LocalizationViewModel.Current;

    public static string Describe(AgentCliReadScope scope, AgentCliProviderProfile profile)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(profile);
        if (scope.BlocksSending)
        {
            return Text.Format(scope.Rejection == AgentCliReadScopeRejection.ConfigurationInvalid
                ? "agentCliReadScopeConfigInvalid" : "agentCliReadScopeDedicatedUnusable", profile.CliName);
        }

        if (scope.UsesWorkspace)
        {
            return Text.Format("agentCliReadScopeFolder", scope.EffectiveDirectory, profile.RecipientName, profile.CliName);
        }

        return scope.CandidateRejected
            ? Text.Format("agentCliReadScopeRejected", scope.CandidateDirectory, Reason(scope, profile), profile.RecipientName, profile.CliName)
            : Text.Format("agentCliReadScopeNone", profile.RecipientName, profile.CliName);
    }

    /// <summary>"contém ~/.ssh", "não existe", "é a raiz de um volume"…; unknown combinations use a generic reason.</summary>
    public static string Reason(AgentCliReadScope scope, AgentCliProviderProfile profile)
    {
        if (scope.Rejection != AgentCliReadScopeRejection.ProtectedArea)
        {
            var key = "agentCliReadScopeReason." + scope.Rejection;
            return Text.HasTranslation(key) ? Text.Resolve(key) : Text.Resolve("agentCliReadScopeReason.Unknown");
        }

        var area = scope.ProtectedArea switch
        {
            AgentCliProtectedArea.AppData => Text.Format("agentCliProtectedArea.AppData", Branding.ProductName),
            AgentCliProtectedArea.Database => Text.Format("agentCliProtectedArea.Database", Branding.ProductName),
            AgentCliProtectedArea.CliConfig => profile.ConfigLocation ?? Text.Format("agentCliProtectedArea.CliConfig", profile.CliName),
            AgentCliProtectedArea.SshKeys => "~/.ssh",
            _ => Text.Resolve("agentCliProtectedArea.Unknown"),
        };
        return Text.Format(scope.Relation == AgentCliProtectedRelation.Contains
            ? "agentCliReadScopeReason.Contains" : "agentCliReadScopeReason.Inside", area);
    }
}
