using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.AiContext;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.KapiLab.Cli;
using EsilvaSoft.KapibaraStudio.KapiLab.Core;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Tests;

/// <summary>
/// Ajustes do laboratório para os alunos v4 (editor-context-v2) e para o <c>agent run</c> com modelo real. Sem pesos nem MongoDB:
/// a execução real fica nos comandos (<c>model run autocomplete</c> com pacote v2 e <c>agent run</c>), documentados no README.
/// </summary>
[TestFixture]
public sealed class CursorInCallV4LabTests
{
    // Registro no formato dos dados v4 do repositório de modelos: editor.target como objeto e target_fields tipados.
    private const string V4Line = """
        {"id":"v4-1","type":"fim","language":"Mongo Console JavaScript","prefix":"db.orders.find({ status: { $in","suffix":" })","editor":{"known_names":["orders"],"result_fields":[],"recent_commands":[],"input_panel":"","target":{"connection":"Dev","database":"shop","collection":"orders"},"target_fields":[{"path":"_id","type":"objectid"},{"path":"status","type":"string"},{"path":"items","type":"array","item":"object"},{"path":"items.qty","type":"int32"}]}}
        """;

    private static readonly string[] CoveredTools = ["list_connections", "list_databases", "list_collections", "get_cached_schema",
        "get_indexes", "get_workspace_context", "propose_file_edit", "create_workspace_file"];

    private static string RepositoryRoot
    {
        get
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) return directory.FullName;
            throw new DirectoryNotFoundException("Checkout não encontrado.");
        }
    }

    [Test]
    public void V2RenderAddsTargetFieldsFromTheV4RecordAndV1StaysFrozen()
    {
        var v2 = ContractRenderCommand.RenderLine(V4Line, 1, noEditorContext: false, contract: EditorContextV2Contract.Instance);
        var v1 = ContractRenderCommand.RenderLine(V4Line, 1, noEditorContext: false);
        Assert.Multiple(() =>
        {
            Assert.That(v2.Contract, Is.EqualTo("editor-context-v2"));
            Assert.That(v2.ModelPrefix, Does.Contain("TARGET FIELDS: orders: status:string, items:array<object>, items.qty:int32\r\n"));
            Assert.That(v2.ModelPrefix, Does.Not.Contain("_id:objectid"), "_id fica fora, como em target_fields_line");
            Assert.That(v1.Contract, Is.EqualTo("editor-context-v1"));
            Assert.That(v1.ModelPrefix, Does.Not.Contain("TARGET FIELDS"));
            Assert.That(v2.ModelPrefix.Replace("TARGET FIELDS: orders: status:string, items:array<object>, items.qty:int32\r\n", ""),
                Is.EqualTo(v1.ModelPrefix), "v2 = v1 + a linha TARGET FIELDS");
        });
    }

    [Test]
    public void ParsedRecordKeepsTheFullDocumentForTheInnerCloserRule()
    {
        var parsed = ContractRenderCommand.ParseInputLine(V4Line, 1, new AutocompleteSettings(), EditorContextV2Contract.Instance);
        Assert.Multiple(() =>
        {
            Assert.That(parsed.Prefix, Is.EqualTo("db.orders.find({ status: { $in"));
            Assert.That(parsed.Suffix, Is.EqualTo(" })"));
            Assert.That(CompletionOutputProcessor.CloseInnerDelimiters(parsed.Prefix, ": [\"A\"]", parsed.Suffix), Is.EqualTo(": [\"A\"] }"));
        });
    }

    [Test]
    public void TargetFieldsMustBeTypedObjects()
    {
        var bad = V4Line.Replace("{\"path\":\"status\",\"type\":\"string\"}", "\"status\"", StringComparison.Ordinal);
        Assert.That(() => ContractRenderCommand.ParseInputLine(bad, 7, new AutocompleteSettings(), EditorContextV2Contract.Instance),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains("target_fields"));
    }

    [Test]
    public void AgentTaskFixtureIsValidAndCoversTheProductTools()
    {
        var root = RepositoryRoot;
        var input = AgentModelRunCommand.ReadInput(root, Path.Combine(root, "tools/KapiLab/Fixtures/kapi-sim/cases/agent-tasks.json"));
        var expected = input.Tasks.SelectMany(task => task.ExpectTools ?? []).ToHashSet(StringComparer.Ordinal);
        Assert.Multiple(() =>
        {
            Assert.That(input.Tasks, Has.Length.EqualTo(10));
            Assert.That(expected, Is.SupersetOf(CoveredTools));
            Assert.That(input.Tasks.Any(task => task.ExpectTools is null or { Length: 0 }), Is.True, "há tarefa sem tool (resposta direta)");
        });
    }

    [TestCase("schema", "\"kapilab-agent-task-v0\"")]
    [TestCase("tool", "\"drop_collection\"")]
    [TestCase("database", "\"admin\"")]
    [TestCase("duplicate", "")]
    public void AgentTaskInputOutsideTheContractIsRefusedBeforeMongo(string defect, string value)
    {
        var root = RepositoryRoot;
        var path = Path.Combine(root, "tmp", "agent-task-" + Guid.NewGuid().ToString("N") + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(root, "tools/KapiLab/Fixtures/kapi-sim/cases/agent-tasks.json")))!;
        var first = node["tasks"]![0]!;
        switch (defect)
        {
            case "schema": node["schema"] = JsonSerializer.Deserialize<string>(value); break;
            case "tool": first["expectTools"] = new System.Text.Json.Nodes.JsonArray(JsonSerializer.Deserialize<string>(value)); break;
            case "database": first["database"] = JsonSerializer.Deserialize<string>(value); break;
            case "duplicate": node["tasks"]![1]!["id"] = first["id"]!.GetValue<string>(); break;
        }
        File.WriteAllText(path, node.ToJsonString());
        try
        {
            Assert.That(() => AgentModelRunCommand.ReadInput(root, path), Throws.InstanceOf<InvalidDataException>());
        }
        finally { File.Delete(path); }
    }
}
