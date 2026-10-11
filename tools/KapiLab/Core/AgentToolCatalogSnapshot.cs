using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Core;

public static class AgentToolCatalogSnapshot
{
    private const string Schema = "kapilab-agent-catalog-v1";
    private const string ProviderMaximumScope = "provider-maximum";
    private static readonly string[] SchemaDirections = ["input", "output"];

    public static Snapshot Export(IAgentToolRegistry registry, string providerId, string surface,
        AgentOperationMode? mode = null, int maxResultBytes = 65_536)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        if (mode is { } requestedMode && !Enum.IsDefined(requestedMode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (maxResultBytes is < 1 or > 65_536) throw new ArgumentOutOfRangeException(nameof(maxResultBytes));

        var descriptors = surface switch
        {
            "in-process" => registry.GetInProcessDescriptors(providerId),
            "session-channel" => registry.GetSessionChannelDescriptors(providerId),
            _ => throw new ArgumentException("A superfície deve ser in-process ou session-channel.", nameof(surface))
        };

        // approve is tied to the active turn's confirmation plan, so it cannot be represented in a provider maximum.
        var permitted = mode is { } selectedMode ? MaximumPlan(providerId, selectedMode).ProductTools : null;
        var tools = descriptors
            .Where(static descriptor => descriptor.Name != AgentToolRegistry.ApproveToolName)
            .Where(descriptor => permitted is null || permitted.Contains(descriptor.Name, StringComparer.Ordinal))
            .OrderBy(static descriptor => descriptor.Name, StringComparer.Ordinal)
            .Select(descriptor =>
            {
                var input = surface == "in-process"
                    ? registry.GetInProcessInputSchemaJson(providerId, descriptor.Name)
                    : registry.GetSessionChannelInputSchemaJson(providerId, descriptor.Name);
                var output = surface == "session-channel"
                    ? registry.GetSessionChannelOutputSchemaJson(providerId, descriptor.Name)
                    : registry.GetInProcessOutputSchemaJson(providerId, descriptor.Name);
                return new Tool(descriptor.Name, descriptor.Version, descriptor.Risk.ToString(),
                    descriptor.RequiredPermissions.Select(static permission => permission.ToString()).ToArray(),
                    input, output, Hash(input), Hash(output), AgentToolDescriptions.Get(descriptor.Name),
                    AgentProductToolNames.CategoryOf(descriptor.Name).ToString(),
                    AgentToolOutputScopes.For(descriptor.Name)?.ToString(),
                    new(AgentToolInvocationQuota.MaximumPerSession, AgentToolInvocationQuota.MaximumPerConnection,
                        AgentToolInvocationQuota.MaximumGlobal, AgentProviderPermissions.DefaultMaximumToolCallsPerTurn),
                    Math.Clamp(maxResultBytes, 1, 65_536),
                    Enum.GetValues<AgentOperationMode>().Where(value => MaximumPlan(providerId, value).ProductTools.Contains(descriptor.Name))
                        .Select(value => value.ToString()).ToArray(), ErrorCodesFor(descriptor.Name));
            }).ToArray();

        var expectedNames = (permitted ?? MaximumPlan(providerId, AgentOperationMode.Agent).ProductTools)
            .Where(name => name != AgentToolRegistry.ApproveToolName).Order(StringComparer.Ordinal);
        var complete = providerId == LocalAgentProvider.Id && surface == "in-process" && tools.Length > 0 &&
            tools.Select(tool => tool.Name).SequenceEqual(expectedNames, StringComparer.Ordinal) &&
            tools.All(tool => tool.Description != tool.Name && tool.InputSchemaJson is not null && tool.OutputSchemaJson is not null);
        return new Snapshot(Schema, providerId, surface, ProviderMaximumScope, complete, tools, mode?.ToString());
    }

    private static AgentTurnPlan MaximumPlan(string providerId, AgentOperationMode mode) => AgentModePolicy.Plan(mode,
        AgentProviderPermissions.Default(providerId) with
        {
            EnabledReadTools = AgentProductToolNames.ReadTools,
            DataSending = new() { InferredSchema = true, MongoDocuments = true },
            NativeFileWrite = true,
        }, new(true, true, false), requireExternalDestinationConsent: false);

    // Registry failures are shared by ingress, authorization, audit and handler paths; not promises of success.
    private static readonly string[] CommonErrorCodes = ["UnknownTool", "PermissionDenied", "InvalidArguments",
        "ConfirmationRejected", "ConfirmationExpired", "ConfirmationUnavailable", "ResultTooLarge",
        "DeadlineExceeded", "Busy", "ToolCallLimitExceeded", "ExecutionFailed", "ToolTimeout", "ToolResultTooLarge"];
    private static readonly string[] ProposalErrorCodes = ["NoWorkspace", "InvalidPath",
        "OutsideWorkspace", "Excluded", "TargetNotPermitted", "ActiveFileNotAttached", "ActiveFileSnapshotMismatch",
        "NotFound", "FileTooLarge", "NotText", "EditNotApplicable", "RedactionMarkerIntroduced", "TargetUnavailable",
        "BaseChanged", "ProposalRejected"];
    private static readonly string[] CreationErrorCodes = ["InvalidPath", "OutsideWorkspace", "Excluded",
        "FileCreationFailed", "FileCreatedAuditIncomplete", "FileAlreadyExists", "ParentDirectoryNotFound"];
    private static string[] ErrorCodesFor(string name) => CommonErrorCodes.Concat(name switch
    {
        AgentToolRegistry.ProposeFileEditToolName => ProposalErrorCodes,
        AgentToolRegistry.CreateWorkspaceFileToolName => CreationErrorCodes,
        _ => Array.Empty<string>(),
    }).ToArray();

    public static CheckResult Check(Snapshot expected, Snapshot actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        var valid = Validate(expected) && Validate(actual);
        if (!valid) return new(false, ["schema_or_identity_invalid"]);
        var differences = new List<string>();
        if (expected.Tools.Length == 0 || actual.Tools.Length == 0) differences.Add("catalog_empty");
        if (expected.ProviderId != actual.ProviderId) differences.Add("provider_changed");
        if (expected.Surface != actual.Surface) differences.Add("surface_changed");
        if (expected.Scope != ProviderMaximumScope || actual.Scope != ProviderMaximumScope) differences.Add("scope_changed");
        if (expected.Complete != actual.Complete) differences.Add("completeness_changed");
        if (expected.Mode != actual.Mode) differences.Add("mode_changed");
        if (expected.DescriptionVersion != actual.DescriptionVersion) differences.Add("description_version_changed");
        var left = expected.Tools.ToDictionary(static tool => tool.Name, StringComparer.Ordinal);
        var right = actual.Tools.ToDictionary(static tool => tool.Name, StringComparer.Ordinal);
        if (!left.Keys.Order(StringComparer.Ordinal).SequenceEqual(right.Keys.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            differences.Add("tool_set_changed");
        foreach (var name in left.Keys.Intersect(right.Keys, StringComparer.Ordinal))
        {
            var a = left[name];
            var b = right[name];
            if (a.Version != b.Version || a.Risk != b.Risk || a.Description != b.Description ||
                !a.RequiredPermissions.SequenceEqual(b.RequiredPermissions, StringComparer.Ordinal) ||
                a.InputSchemaJson != b.InputSchemaJson || a.OutputSchemaJson != b.OutputSchemaJson ||
                a.ConfirmationCategory != b.ConfirmationCategory || a.OutputScope != b.OutputScope ||
                a.Quotas != b.Quotas || a.MaxResultBytes != b.MaxResultBytes ||
                !EqualsArrays(a.Modes, b.Modes) || !EqualsArrays(a.ErrorCodes, b.ErrorCodes))
                differences.Add("tool_contract_changed:" + name);
            if (a.InputSchemaJson is null || a.OutputSchemaJson is null || b.InputSchemaJson is null || b.OutputSchemaJson is null)
                differences.Add("schema_missing:" + name);
        }
        return new(differences.Count == 0, differences);
    }

    public static Snapshot? Parse(string json)
    {
        using (var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 }))
        {
            JsonContractValidation.RequireUniqueProperties(document.RootElement);
            var toolArray = Property(document.RootElement, "tools");
            if (toolArray is { ValueKind: JsonValueKind.Array })
                foreach (var tool in toolArray.Value.EnumerateArray())
                    foreach (var prefix in SchemaDirections)
                    {
                        var projection = Property(tool, prefix + "Schema");
                        var text = Property(tool, prefix + "SchemaJson");
                        if (projection is null) continue; // Legacy snapshots carried only the JSON strings.
                        if (text is null || text.Value.ValueKind == JsonValueKind.Null)
                        { if (projection.Value.ValueKind != JsonValueKind.Null) return null; }
                        else if (text.Value.ValueKind != JsonValueKind.String ||
                            !JsonElement.DeepEquals(projection.Value, JsonSerializer.Deserialize<JsonElement>(text.Value.GetString()!)))
                            return null;
                    }
        }
        var snapshot = JsonSerializer.Deserialize<Snapshot>(json, JsonOptions);
        return snapshot is not null && Validate(snapshot) ? snapshot : null;
    }

    public static string Serialize(Snapshot snapshot) => JsonSerializer.Serialize(snapshot, JsonOptions);

    private static JsonElement? Property(JsonElement element, string name) => element.ValueKind != JsonValueKind.Object ? null :
        element.EnumerateObject().Where(property => property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            .Select(property => (JsonElement?)property.Value).FirstOrDefault();

    private static bool Validate(Snapshot snapshot) =>
        snapshot.Schema == Schema && !string.IsNullOrWhiteSpace(snapshot.ProviderId) &&
        snapshot.Surface is "in-process" or "session-channel" && snapshot.Scope == ProviderMaximumScope &&
        (snapshot.Mode is null || Enum.TryParse<AgentOperationMode>(snapshot.Mode, out var parsedMode) && Enum.IsDefined(parsedMode)) &&
        snapshot.Tools is not null &&
        (!snapshot.Complete || snapshot.ProviderId == "local" && snapshot.Surface == "in-process" &&
            snapshot.Tools.Length > 0 && CompleteToolSet(snapshot) && snapshot.Tools.All(tool => tool is not null &&
                !string.IsNullOrWhiteSpace(tool.Description) && tool.Description != tool.Name &&
                tool.InputSchemaJson is not null && tool.OutputSchemaJson is not null && tool.Quotas is not null &&
                tool.OutputScope is not null && tool.Modes is { Length: > 0 } && tool.ErrorCodes is { Length: > 0 })) &&
        snapshot.Tools.All(static tool => tool is not null && !string.IsNullOrWhiteSpace(tool.Name) &&
            tool.Name != AgentToolRegistry.ApproveToolName && tool.Version > 0 &&
            !string.IsNullOrWhiteSpace(tool.Risk) && tool.RequiredPermissions is not null &&
            tool.RequiredPermissions.All(static permission => !string.IsNullOrWhiteSpace(permission)) &&
            (tool.InputSchemaJson is null ? tool.InputSchemaSha256 is null : tool.InputSchemaSha256 == Hash(tool.InputSchemaJson)) &&
            (tool.OutputSchemaJson is null ? tool.OutputSchemaSha256 is null : tool.OutputSchemaSha256 == Hash(tool.OutputSchemaJson))) &&
        snapshot.Tools.Select(static tool => tool.Name).Distinct(StringComparer.Ordinal).Count() == snapshot.Tools.Length;

    private static bool CompleteToolSet(Snapshot snapshot)
    {
        if (snapshot.Tools.Any(tool => tool is null)) return false;
        var mode = snapshot.Mode is null ? AgentOperationMode.Agent : Enum.Parse<AgentOperationMode>(snapshot.Mode);
        return snapshot.Tools.Select(tool => tool.Name).Order(StringComparer.Ordinal)
            .SequenceEqual(MaximumPlan(snapshot.ProviderId, mode).ProductTools.Order(StringComparer.Ordinal), StringComparer.Ordinal);
    }

    private static string? Hash(string? value) => value is null ? null : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static bool EqualsArrays(string[]? left, string[]? right) => left is null ? right is null :
        right is not null && left.SequenceEqual(right, StringComparer.Ordinal);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public sealed record Snapshot(string Schema, string ProviderId, string Surface, string Scope, bool Complete, Tool[] Tools,
        string? Mode = null, string ApproveOmissionReason = "approve pertence à confirmação do turno; não é uma tool do modelo local.",
        string DescriptionVersion = AgentToolDescriptions.Version);
    public sealed record Tool(string Name, int Version, string Risk, string[] RequiredPermissions,
        string? InputSchemaJson, string? OutputSchemaJson, string? InputSchemaSha256, string? OutputSchemaSha256,
        string? Description = null, string? ConfirmationCategory = null, string? OutputScope = null,
        InvocationQuotas? Quotas = null, int? MaxResultBytes = null, string[]? Modes = null, string[]? ErrorCodes = null)
    {
        public JsonElement? InputSchema => InputSchemaJson is null ? null : JsonSerializer.Deserialize<JsonElement>(InputSchemaJson);
        public JsonElement? OutputSchema => OutputSchemaJson is null ? null : JsonSerializer.Deserialize<JsonElement>(OutputSchemaJson);
    }
    public sealed record InvocationQuotas(int ConcurrentPerSession, int ConcurrentPerConnection, int ConcurrentGlobal,
        int DefaultCallsPerTurn);
    public sealed record CheckResult(bool Matches, IReadOnlyList<string> Differences);
}
