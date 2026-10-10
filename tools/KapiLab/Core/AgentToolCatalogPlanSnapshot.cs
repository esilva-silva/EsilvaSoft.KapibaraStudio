using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core.Agents;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Core;

/// <summary>Catalog limited to a single explicit synthetic policy input; it never represents a live grant.</summary>
public static class AgentToolCatalogPlanSnapshot
{
    public const string InputSchema = "kapilab-agent-catalog-plan-input-v1";
    public const string OutputSchema = "kapilab-agent-catalog-plan-v1";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = false,
    };

    static AgentToolCatalogPlanSnapshot() => JsonOptions.Converters.Add(new JsonStringEnumConverter());

    public static PlanInput? ParseInput(string json)
    {
        using (var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 }))
            JsonContractValidation.RequireUniqueProperties(document.RootElement);
        try
        {
            var input = JsonSerializer.Deserialize<PlanInput>(json, JsonOptions);
            return input is not null && Validate(input) ? input : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static Snapshot Export(IAgentToolRegistry registry, string providerId, PlanInput input)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ArgumentNullException.ThrowIfNull(input);
        if (!Validate(input) || !string.Equals(input.Case.Permissions.ProviderId, providerId, StringComparison.Ordinal))
            throw new InvalidDataException("Plano sintético inválido ou provider divergente.");

        var item = input.Case;
        var plan = AgentModePolicy.Plan(item.Mode, item.Permissions,
            new AgentPlatformFacts(item.HasWorkspaceFolder!.Value, item.ProductToolsAvailable!.Value, item.NativeToolsAvailable!.Value),
            item.RequireExternalDestinationConsent);
        var permitted = plan.ProductTools.ToHashSet(StringComparer.Ordinal);
        // Registry discovery remains the authority for provider exposure. The plan can only narrow this set.
        var tools = registry.GetInProcessDescriptors(providerId)
            .Where(descriptor => permitted.Contains(descriptor.Name))
            .OrderBy(static descriptor => descriptor.Name, StringComparer.Ordinal)
            .Select(descriptor =>
            {
                var inputSchema = registry.GetInProcessInputSchemaJson(providerId, descriptor.Name);
                var outputSchema = registry.GetInProcessOutputSchemaJson(providerId, descriptor.Name);
                return new Tool(descriptor.Name, descriptor.Version, descriptor.Risk.ToString(),
                    descriptor.RequiredPermissions.Select(static permission => permission.ToString()).ToArray(),
                    AgentToolExposure.StageOf(descriptor.Name)?.ToString(), AgentToolRegistry.IsSessionTool(descriptor.Name),
                    AgentProductToolNames.CategoryOf(descriptor.Name).ToString(),
                    AgentToolOutputScopes.For(descriptor.Name)?.ToString(), inputSchema, outputSchema,
                    Hash(inputSchema), Hash(outputSchema));
            }).ToArray();

        var body = new SnapshotContent(OutputSchema, "synthetic-policy-input", true, false, false, false,
            providerId, item.Id, item.Mode.ToString(), plan.IsBlocked ? plan.BlockReason.ToString() : null,
            plan.ProductTools.ToArray(), plan.AllowedConnectionIds is null ? "all" : "selected",
            plan.AllowedConnectionIds?.Count ?? 0, tools);
        return new Snapshot(body.Schema, body.EvidenceKind, body.Synthetic, body.Complete, body.ToolInvocationPerformed,
            body.GrantsVerified, body.ProviderId, body.PlanId, body.Mode, body.BlockReason, body.PlannedProductTools,
            body.AllowedConnectionScope, body.AllowedConnectionCount, body.Tools,
            Hash(JsonSerializer.Serialize(body, JsonOptions))!);
    }

    public static string Serialize(Snapshot snapshot) => JsonSerializer.Serialize(snapshot, JsonOptions);
    public static string Serialize(PlanInput input) => JsonSerializer.Serialize(input, JsonOptions);

    private static bool Validate(PlanInput input) => input.Schema == InputSchema && input.Case is not null &&
        IsValidId(input.Case.Id) && Enum.IsDefined(input.Case.Mode) && input.Case.Permissions is not null &&
        input.Case.Permissions.IsWellFormed && input.Case.HasWorkspaceFolder is not null &&
        input.Case.ProductToolsAvailable is not null && input.Case.NativeToolsAvailable is not null;

    private static bool IsValidId(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 80 &&
        value.All(static c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    private static string? Hash(string? value) => value is null ? null : Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    public sealed record PlanInput(string Schema, PlanCaseInput Case);
    public sealed record PlanCaseInput(string Id, AgentOperationMode Mode, AgentProviderPermissions Permissions,
        bool? HasWorkspaceFolder, bool? ProductToolsAvailable, bool? NativeToolsAvailable,
        bool RequireExternalDestinationConsent = true);
    public sealed record Snapshot(string Schema, string EvidenceKind, bool Synthetic, bool Complete, bool ToolInvocationPerformed,
        bool GrantsVerified, string ProviderId, string PlanId, string Mode, string? BlockReason,
        string[] PlannedProductTools, string AllowedConnectionScope, int AllowedConnectionCount, Tool[] Tools,
        string SnapshotSha256);
    private sealed record SnapshotContent(string Schema, string EvidenceKind, bool Synthetic, bool Complete, bool ToolInvocationPerformed,
        bool GrantsVerified, string ProviderId, string PlanId, string Mode, string? BlockReason,
        string[] PlannedProductTools, string AllowedConnectionScope, int AllowedConnectionCount, Tool[] Tools);
    public sealed record Tool(string Name, int Version, string Risk, string[] RequiredPermissions,
        string? Stage, bool IsSessionTool, string ConfirmationCategory, string? OutputScope,
        string? InputSchemaJson, string? OutputSchemaJson, string? InputSchemaSha256, string? OutputSchemaSha256);
}
