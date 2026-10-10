using System.Text.Json;
using System.Text.Json.Serialization;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core.Agents;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Core;

/// <summary>Deterministic export of synthetic inputs evaluated by the product's pure AgentModePolicy.</summary>
public static class AgentPlanSnapshot
{
    public const string InputSchema = "kapilab-agent-plan-cases-v1";
    public const string OutputSchema = "kapilab-agent-plan-snapshot-v1";
    public const int MaximumCases = 500;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = false,
    };

    static AgentPlanSnapshot() => JsonOptions.Converters.Add(new JsonStringEnumConverter());

    public static PlanInput? ParseInput(string json)
    {
        using (var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 }))
            JsonContractValidation.RequireUniqueProperties(document.RootElement);
        var input = JsonSerializer.Deserialize<PlanInput>(json, JsonOptions);
        return input is not null && Validate(input) ? input : null;
    }

    public static PlanSnapshot Evaluate(PlanInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!Validate(input)) throw new InvalidDataException("Casos de plano inválidos ou incompatíveis.");

        var cases = input.Cases.Select(item =>
        {
            var plan = AgentModePolicy.Plan(item.Mode, item.Permissions,
                new AgentPlatformFacts(item.HasWorkspaceFolder, item.ProductToolsAvailable, item.NativeToolsAvailable),
                item.RequireExternalDestinationConsent);
            return new PlanCaseResult(item.Id, item.Mode.ToString(), item.Permissions.ProviderId,
                plan.IsBlocked ? plan.BlockReason.ToString() : null,
                plan.NativeTools.ToArray(), plan.ProductTools.ToArray(), plan.NativeAskRules.ToArray(),
                plan.NativeDenyRules.ToArray(), plan.ProposalHandling.ToString(),
                plan.RequiresPermissionPromptTool, plan.ConfirmationCategories.ToString(),
                plan.AllowedConnectionIds is null ? "all" : "selected",
                plan.AllowedConnectionIds?.Count ?? 0, plan.Notices.ToArray());
        }).ToArray();

        return new PlanSnapshot(OutputSchema, "synthetic-policy-input", true, false, cases);
    }

    public static string Serialize(PlanSnapshot snapshot) => JsonSerializer.Serialize(snapshot, JsonOptions);
    public static string Serialize(PlanInput input) => JsonSerializer.Serialize(input, JsonOptions);

    private static bool Validate(PlanInput input) =>
        input.Schema == InputSchema && input.Cases is { Length: > 0 and <= MaximumCases } &&
        input.Cases.Select(static item => item?.Id).Distinct(StringComparer.Ordinal).Count() == input.Cases.Length &&
        input.Cases.All(static item => item is not null && IsValidId(item.Id) && Enum.IsDefined(item.Mode) &&
            item.Permissions is not null && item.Permissions.IsWellFormed);

    private static bool IsValidId(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 80 &&
        value.All(static c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    public sealed record PlanInput(string Schema, PlanCaseInput[] Cases);

    /// <summary>One synthetic policy case. Consent and platform facts are test inputs, not stored user grants.</summary>
    public sealed record PlanCaseInput(
        string Id,
        AgentOperationMode Mode,
        AgentProviderPermissions Permissions,
        bool HasWorkspaceFolder,
        bool ProductToolsAvailable,
        bool NativeToolsAvailable,
        bool RequireExternalDestinationConsent = true);

    public sealed record PlanSnapshot(string Schema, string EvidenceKind, bool Synthetic, bool ToolInvocationPerformed,
        PlanCaseResult[] Cases);

    public sealed record PlanCaseResult(string Id, string Mode, string ProviderId, string? BlockReason,
        string[] NativeTools, string[] ProductTools, string[] NativeAskRules, string[] NativeDenyRules,
        string ProposalHandling, bool RequiresPermissionPromptTool, string ConfirmationCategories,
        string AllowedConnectionScope, int AllowedConnectionCount, string[] Notices);
}
