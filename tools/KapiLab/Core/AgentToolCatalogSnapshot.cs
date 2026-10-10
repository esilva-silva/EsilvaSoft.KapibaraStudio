using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Core;

public static class AgentToolCatalogSnapshot
{
    private const string Schema = "kapilab-agent-catalog-v1";
    private const string ProviderMaximumScope = "provider-maximum";

    public static Snapshot Export(IAgentToolRegistry registry, string providerId, string surface)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);

        var descriptors = surface switch
        {
            "in-process" => registry.GetInProcessDescriptors(providerId),
            "session-channel" => registry.GetSessionChannelDescriptors(providerId),
            _ => throw new ArgumentException("A superfície deve ser in-process ou session-channel.", nameof(surface))
        };

        // approve is tied to the active turn's confirmation plan, so it cannot be represented in a provider maximum.
        var tools = descriptors
            .Where(static descriptor => descriptor.Name != AgentToolRegistry.ApproveToolName)
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
                    input, output, Hash(input), Hash(output));
            }).ToArray();

        return new Snapshot(Schema, providerId, surface, ProviderMaximumScope, false, tools);
    }

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
        if (expected.Complete || actual.Complete) differences.Add("unexpected_complete_claim");
        var left = expected.Tools.ToDictionary(static tool => tool.Name, StringComparer.Ordinal);
        var right = actual.Tools.ToDictionary(static tool => tool.Name, StringComparer.Ordinal);
        if (!left.Keys.Order(StringComparer.Ordinal).SequenceEqual(right.Keys.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            differences.Add("tool_set_changed");
        foreach (var name in left.Keys.Intersect(right.Keys, StringComparer.Ordinal))
        {
            var a = left[name];
            var b = right[name];
            if (a.Version != b.Version || a.Risk != b.Risk ||
                !a.RequiredPermissions.SequenceEqual(b.RequiredPermissions, StringComparer.Ordinal) ||
                a.InputSchemaJson != b.InputSchemaJson || a.OutputSchemaJson != b.OutputSchemaJson)
                differences.Add("tool_contract_changed:" + name);
            if (a.InputSchemaJson is null || a.OutputSchemaJson is null || b.InputSchemaJson is null || b.OutputSchemaJson is null)
                differences.Add("schema_missing:" + name);
        }
        return new(differences.Count == 0, differences);
    }

    public static Snapshot? Parse(string json)
    {
        using (var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 }))
            JsonContractValidation.RequireUniqueProperties(document.RootElement);
        var snapshot = JsonSerializer.Deserialize<Snapshot>(json, JsonOptions);
        return snapshot is not null && Validate(snapshot) ? snapshot : null;
    }

    public static string Serialize(Snapshot snapshot) => JsonSerializer.Serialize(snapshot, JsonOptions);

    private static bool Validate(Snapshot snapshot) =>
        snapshot.Schema == Schema && !string.IsNullOrWhiteSpace(snapshot.ProviderId) &&
        snapshot.Surface is "in-process" or "session-channel" && snapshot.Scope == ProviderMaximumScope &&
        !snapshot.Complete && snapshot.Tools is not null &&
        snapshot.Tools.All(static tool => tool is not null && !string.IsNullOrWhiteSpace(tool.Name) &&
            tool.Name != AgentToolRegistry.ApproveToolName && tool.Version > 0 &&
            !string.IsNullOrWhiteSpace(tool.Risk) && tool.RequiredPermissions is not null &&
            tool.RequiredPermissions.All(static permission => !string.IsNullOrWhiteSpace(permission)) &&
            (tool.InputSchemaJson is null ? tool.InputSchemaSha256 is null : tool.InputSchemaSha256 == Hash(tool.InputSchemaJson)) &&
            (tool.OutputSchemaJson is null ? tool.OutputSchemaSha256 is null : tool.OutputSchemaSha256 == Hash(tool.OutputSchemaJson))) &&
        snapshot.Tools.Select(static tool => tool.Name).Distinct(StringComparer.Ordinal).Count() == snapshot.Tools.Length;

    private static string? Hash(string? value) => value is null ? null : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public sealed record Snapshot(string Schema, string ProviderId, string Surface, string Scope, bool Complete, Tool[] Tools);
    public sealed record Tool(string Name, int Version, string Risk, string[] RequiredPermissions,
        string? InputSchemaJson, string? OutputSchemaJson, string? InputSchemaSha256, string? OutputSchemaSha256);
    public sealed record CheckResult(bool Matches, IReadOnlyList<string> Differences);
}
