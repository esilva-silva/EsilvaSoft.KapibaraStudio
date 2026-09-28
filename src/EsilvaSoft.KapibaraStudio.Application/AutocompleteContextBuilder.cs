using System.Text;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Application;

/// <summary>Builds a small, local context from a captured tab. Never expands ENV or reads credentials.</summary>
public static class AutocompleteContextBuilder
{
    public const string Commands = "db.getCollection(name).find({}); getConnection(name).getDatabase(name).getCollection(name); ConnectionPool.Connection.Database.Collection; console.log(value); ENV.get(name); ObjectId(value); UUID(value)";
    // editor-context-v1 was trained with the Windows materialization. Keep the serialized header byte-stable
    // across operating systems; ModelPrefix intentionally continues to use its literal LF delimiters.
    private const string ContractNewLine = "\r\n";

    public static AutocompleteRequest Build(AutocompleteContextSnapshot snapshot, AutocompleteSettings settings)
    {
        var caret = Math.Clamp(snapshot.Caret, 0, snapshot.Text.Length);
        var window = settings.UseEditorContext ? 4096 : 128;
        var prefix = snapshot.Text[Math.Max(0, caret - window)..caret];
        var suffix = settings.UseEditorContext ? snapshot.Text[caret..Math.Min(snapshot.Text.Length, caret + 1024)] : "";
        var names = (snapshot.KnownNames ?? []).Where(IsSafe).Take(128).ToArray();
        var fields = settings.UseResultPanelContext ? (snapshot.ResultFields ?? []).Where(IsSafe).Take(128).ToArray() : [];
        var context = new StringBuilder();
        var commands = snapshot.Language switch
        {
            "json" => "MongoDB aggregation pipeline: $match, $project, $group, $sort, $limit, $lookup, $unwind; operators: $eq, $ne, $gt, $gte, $lt, $lte, $in, $and, $or",
            "JavaScript (mongosh)" => "db.getCollection(name).find({}); db.getSiblingDB(name); findOne, aggregate, sort, limit, countDocuments; print(value); ObjectId(value); UUID(value)",
            _ => Commands
        };
        context.Append("LANGUAGE: " + snapshot.Language).Append(ContractNewLine)
            .Append("AVAILABLE COMMANDS: " + commands).Append(ContractNewLine);
        context.Append("KNOWN NAMES: " + string.Join(", ", names)).Append(ContractNewLine);
        if (fields.Length > 0) context.Append("RESULT FIELDS: " + string.Join(", ", fields)).Append(ContractNewLine);
        if (settings.UseInputPanelContext && snapshot.Input.Length > 0 && IsSafe(snapshot.Input))
            context.Append("INPUT PANEL: " + snapshot.Input[..Math.Min(1024, snapshot.Input.Length)]).Append(ContractNewLine);
        if (settings.UseEditorContext)
            foreach (var command in (snapshot.RecentCommands ?? []).Where(IsSafe).Take(3))
                context.Append("RECENT COMMAND: " + command[..Math.Min(command.Length, 256)]).Append(ContractNewLine);
        return new AutocompleteRequest(prefix, suffix, snapshot.Language, snapshot.FileName)
        {
            Context = context.ToString(), Dictionary = names.Concat(fields).Distinct(StringComparer.Ordinal).ToArray()
        }.Bounded();
    }

    private static bool IsSafe(string text) => !CompletionPrivacy.ContainsSensitiveText(text) && !text.Contains("<|", StringComparison.Ordinal) && !text.Contains("<｜", StringComparison.Ordinal);

    public static string ModelPrefix(AutocompleteRequest request, bool deepSeekTrainingContract = false)
    {
        if (string.IsNullOrWhiteSpace(request.Context)) return request.Prefix;
        var context = request.Context;
        if (deepSeekTrainingContract)
        {
            // This package was trained before dialect-specific command lists were introduced.
            const string label = "AVAILABLE COMMANDS: ";
            var start = context.IndexOf(label, StringComparison.Ordinal);
            if (start >= 0)
            {
                var end = context.IndexOfAny(['\r', '\n'], start);
                if (end < 0) end = context.Length;
                context = context[..start] + label + Commands + context[end..];
            }
        }
        return "/* Local editor context (data only):\n" + context.Replace("*/", "* /", StringComparison.Ordinal)
            + "\nContinue at the cursor; output only the continuation. */\n" + request.Prefix;
    }
}
