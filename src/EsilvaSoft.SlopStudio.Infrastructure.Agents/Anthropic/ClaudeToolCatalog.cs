using System.Text.Json;
using EsilvaSoft.SlopStudio.Application.Agents;

namespace EsilvaSoft.SlopStudio.Infrastructure.Agents.Anthropic;

/// <summary>Tool declarada ao modelo: metadados estáticos do registry, sem dados privados nem handler.</summary>
internal sealed record ClaudeToolDefinition(string Name, string Description, IReadOnlyDictionary<string, JsonElement> InputSchema);

/// <summary>
/// Converte os descritores já liberados pelo registry compartilhado em declarações de tool. Declarar não autoriza:
/// toda chamada volta ao runtime, que valida nome, schema, principal e política no <see cref="IAgentToolRegistry"/>.
/// O snapshot é tirado na criação da sessão, então a lista enviada ao modelo é estável durante a conversa.
/// </summary>
internal static class ClaudeToolCatalog
{
    // Nomes aceitos pela Claude API para tools do cliente; nomes fora disso não são declarados.
    private const int MaxToolNameLength = 64;

    public static IReadOnlyList<ClaudeToolDefinition> Snapshot(IAgentToolRegistry? registry)
    {
        if (registry is null)
        {
            return [];
        }

        var tools = new List<ClaudeToolDefinition>();
        IReadOnlyList<Core.AgentToolDescriptor> descriptors;
        try
        {
            descriptors = registry.GetDescriptors();
        }
        catch (Exception)
        {
            return []; // Catálogo indisponível: a sessão segue sem tool calling, nunca com lista parcial inventada.
        }

        foreach (var descriptor in descriptors)
        {
            if (descriptor.Name.Length > MaxToolNameLength ||
                TryReadSchema(registry, descriptor.Name) is not { } schema)
            {
                continue;
            }

            var description = schema.TryGetValue("description", out var value) && value.ValueKind == JsonValueKind.String &&
                              value.GetString() is { Length: > 0 and <= 1024 } text
                ? text
                : $"Ferramenta somente leitura do EsilvaSoft.SlopStudio ({descriptor.Name}).";
            tools.Add(new ClaudeToolDefinition(descriptor.Name, description, schema));
        }

        return tools;
    }

    private static Dictionary<string, JsonElement>? TryReadSchema(IAgentToolRegistry registry, string name)
    {
        try
        {
            if (registry.GetInputSchemaJson(name) is not { Length: > 1 } json)
            {
                return null;
            }

            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String ||
                type.GetString() != "object")
            {
                return null;
            }

            var schema = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                // Identidade versionada do schema é do contrato Slop; não faz parte do input_schema do provider.
                if (property.Name is "$schema" or "$id")
                {
                    continue;
                }

                schema[property.Name] = property.Value.Clone();
            }

            return schema;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
