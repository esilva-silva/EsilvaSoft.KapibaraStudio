using System.Text.Json;

namespace EsilvaSoft.KapibaraStudio.Core;

public static class ExplorerScripts
{
    /// <param name="operation">Script to generate.</param>
    /// <param name="database">Selected database; scripts address it through <c>db</c>.</param>
    /// <param name="collection">Selected collection, if any.</param>
    /// <param name="index">Selected index, for index scripts.</param>
    /// <param name="identifiers">Identifier mode and UUID representation of the connection; the placeholder <c>_id</c> follows them.</param>
    public static string Create(ExplorerScriptOperation operation, string database, string? collection = null, IndexInfo? index = null, IdentifierDisplayOptions? identifiers = null)
    {
        var target = "db";
        var coll = $"{target}.getCollection({JsonSerializer.Serialize(collection ?? "colecao")})";
        var id = IdentifierRepresentationService.ScriptIdentifierPlaceholder(identifiers ?? IdentifierDisplayOptions.Default);
        var command = operation switch
        {
            ExplorerScriptOperation.Find => $"{coll}.find({{}}).limit(100);",
            ExplorerScriptOperation.Insert => $"{coll}.insertOne({{ /* documento */ }});",
            ExplorerScriptOperation.Update => $"{coll}.updateMany({{ _id: {id} }}, {{ $set: {{ /* campos */ }} }});",
            ExplorerScriptOperation.Delete => $"{coll}.deleteMany({{ _id: {id} }});",
            ExplorerScriptOperation.CreateIndex when index is not null => $"{coll}.createIndex(EJSON.parse({JsonSerializer.Serialize(index.Keys)}), EJSON.parse({JsonSerializer.Serialize(index.Options)}));",
            ExplorerScriptOperation.CreateIndex => $"{coll}.createIndex({{ campo: 1 }}, {{ name: \"campo_1\", unique: false }});",
            ExplorerScriptOperation.DropIndex => $"{coll}.dropIndex({JsonSerializer.Serialize(index?.Name ?? "nome_do_indice")});",
            ExplorerScriptOperation.CreateCollection => $"{target}.createCollection(\"nova_colecao\");",
            ExplorerScriptOperation.DropCollection => $"{coll}.drop();",
            ExplorerScriptOperation.DropDatabase => $"{target}.dropDatabase();",
            ExplorerScriptOperation.Statistics => collection is null ? $"{target}.stats();" : $"{coll}.stats();",
            _ => "// db representa o banco selecionado no destino desta aba.\ndb.getName();"
        };
        var destructive = operation is ExplorerScriptOperation.Delete or ExplorerScriptOperation.Update or ExplorerScriptOperation.DropIndex or ExplorerScriptOperation.DropCollection or ExplorerScriptOperation.DropDatabase;
        return "// Gerado para revisão. Nada foi executado. Confira o destino da aba.\n"
            + (destructive ? "// ATENÇÃO: esta operação altera ou remove dados. Revise o alvo antes de executar.\n" : "") + command + "\n";
    }
}
