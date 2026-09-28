using EsilvaSoft.SlopStudio.Core.Agents;
using EsilvaSoft.SlopStudio.Desktop.Agents;

namespace EsilvaSoft.SlopStudio.Desktop.ViewModels;

/// <summary>Display wrapper of a provider descriptor. All labels derive from capabilities, never from a brand.</summary>
public sealed class AgentProviderOption(AgentProviderPresentation presentation)
{
    private static LocalizationViewModel Text => LocalizationViewModel.Current;

    public AgentProviderPresentation Presentation { get; } = presentation;

    public string ProviderId => Presentation.ProviderId;

    public bool IsExternal => Presentation.Destination == AgentDataDestinationKind.External;

    public string DestinationText => Text.Resolve(IsExternal ? "agentDestinationExternal" : "agentDestinationLocal");

    public string DestinationHint => Text.Resolve(IsExternal ? "agentDestinationExternalHint" : "agentDestinationLocalHint");

    /// <summary>The catalog has not checked this provider yet (listing never reads the vault or the network).</summary>
    public bool IsNotChecked => !Presentation.IsAvailable &&
        string.Equals(Presentation.UnavailableReason, AgentProviderStatus.NotReported.UnavailableCode, StringComparison.Ordinal);

    public string AvailabilityText => Text.Resolve(Presentation.IsAvailable ? "agentSettingsAvailable"
        : IsNotChecked ? "agentSettingsNotChecked" : "agentSettingsUnavailable");

    /// <summary>
    /// Readable reason for an unavailable provider. Generic status codes of the catalog are localized; any other safe
    /// code reported by the adapter is shown as-is (never a provider message or secret).
    /// </summary>
    public string UnavailableText => Presentation.UnavailableReason switch
    {
        null or "" => Text.Resolve("agentSettingsUnavailable"),
        "StatusNotReported" => Text.Resolve("agentUnavailableNotChecked"),
        "StatusTimedOut" => Text.Resolve("agentUnavailableTimedOut"),
        "StatusFailed" => Text.Resolve("agentUnavailableCheckFailed"),
        // Safe adapter codes (e.g. "ExecutableNotFound") are localized when the catalog knows them; otherwise the code
        // itself is shown. The lookup is by code, never by provider.
        var code when Text.HasTranslation(UnavailableCodePrefix + code) => Text.Resolve(UnavailableCodePrefix + code),
        var code => Text.Format("agentUnavailableUnknownCode", code),
    };

    internal const string UnavailableCodePrefix = "agentUnavailable.";

    /// <summary>
    /// <see cref="UnavailableText"/> as a standalone sentence (settings line): the reason is written to follow
    /// "X indisponível: …" in the chat, so it starts lowercase and is capitalized here.
    /// </summary>
    public string UnavailableSentence => UnavailableText is { Length: > 0 } reason && char.IsLower(reason[0])
        ? char.ToUpper(reason[0], System.Globalization.CultureInfo.CurrentCulture) + reason[1..]
        : UnavailableText;

    /// <summary>"Name · Externo" or "Name · Indisponível"; destination and availability are text, not color.</summary>
    public string Label => Text.Format("agentProviderItem", Presentation.DisplayName,
        Presentation.IsAvailable ? DestinationText : AvailabilityText);

    public bool RequiresApiKey => Presentation.AuthenticationMethods.Contains(AgentAuthenticationMethod.ApiKey);

    /// <summary>Authentication delegated to an official CLI: no key or vault is involved, so states read differently.</summary>
    public bool UsesOfficialCli => Presentation.AuthenticationMethods.Contains(AgentAuthenticationMethod.OfficialCliDelegated) ||
        Presentation.AuthenticationMethods.Contains(AgentAuthenticationMethod.OfficialAppServerDelegated);

    /// <summary>
    /// Textual mode chip ("Claude · assinatura" / "Claude · API"), derived from the authentication method (capability)
    /// and the family label of the composition root. Local providers have no mode chip: Local/Externo already says it.
    /// </summary>
    public string? ModeText => UsesOfficialCli
        ? Text.Format(Presentation.IsExperimental ? "agentModeChipExperimental" : "agentModeChip", FamilyName, Text.Resolve("agentModeSubscription"))
        : RequiresApiKey ? Text.Format("agentModeChip", FamilyName, Text.Resolve("agentModeApi")) : null;

    public bool HasModeText => ModeText is not null;

    private string FamilyName => string.IsNullOrWhiteSpace(Presentation.FamilyName) ? Presentation.DisplayName : Presentation.FamilyName;

    /// <summary>Account type for API-key providers; CLI-delegated accounts get it from an explicit check.</summary>
    public string ApiAccountTypeText => RequiresApiKey ? Text.Resolve("agentAccountTypeApi") : "";

    public string AuthStateText => Text.Resolve(Presentation.AuthState switch
    {
        AgentProviderAuthState.Configured when UsesOfficialCli => "agentAuthStateCliSignedIn",
        AgentProviderAuthState.NotConfigured when UsesOfficialCli => "agentAuthStateCliSignedOut",
        AgentProviderAuthState.Invalid when UsesOfficialCli => "agentAuthStateCliBlocked",
        AgentProviderAuthState.NotRequired => "agentAuthStateNotRequired",
        AgentProviderAuthState.NotConfigured => "agentAuthStateNotConfigured",
        AgentProviderAuthState.Configured => "agentAuthStateConfigured",
        AgentProviderAuthState.Invalid => "agentAuthStateInvalid",
        AgentProviderAuthState.Expired => "agentAuthStateExpired",
        AgentProviderAuthState.Unknown => "agentAuthStateUnknown",
        _ => "agentAuthStateVaultUnavailable",
    });

    public string AuthMethodsText => Presentation.AuthenticationMethods.Count == 0
        ? Text.Resolve("agentSettingsNoAuthMethods")
        : string.Join(" · ", Presentation.AuthenticationMethods.Distinct().Select(method =>
            Text.Resolve(method switch
            {
                AgentAuthenticationMethod.ApiKey => "agentAuthApiKey",
                AgentAuthenticationMethod.OfficialCliDelegated => "agentAuthOfficialCli",
                AgentAuthenticationMethod.OfficialAppServerDelegated => "agentAuthOfficialAppServer",
                _ => "agentAuthNone",
            })));

    public string CapabilitiesText => Text.Format("agentCapabilitiesValue",
        Text.Resolve(Presentation.SupportsStreaming ? "agentYes" : "agentNo"),
        Text.Resolve(Presentation.SupportsToolCalling ? "agentYes" : "agentNo"), Branding.ProductName);

    public string ModelsText => Presentation.Models.Count == 0
        ? Text.Resolve("agentSettingsNoModels")
        : string.Join(", ", Presentation.Models);

    public override string ToString() => Label;
}
