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

    /// <summary>Teto de campos da linha <c>TARGET FIELDS</c> (v2), na ordem do schema.</summary>
    public const int MaximumTargetFields = 96;
    /// <summary>Teto de caracteres da lista da linha <c>TARGET FIELDS</c> (v2), contando os separadores.</summary>
    public const int MaximumTargetFieldsCharacters = 2048;

    /// <summary>Cabeçalho <c>editor-context-v1</c>, congelado.</summary>
    public static AutocompleteRequest Build(AutocompleteContextSnapshot snapshot, AutocompleteSettings settings)
        => Build(snapshot, settings, includeTargetFields: false);

    /// <summary>
    /// Cabeçalho do contrato: <paramref name="includeTargetFields"/> verdadeiro é o <c>editor-context-v2</c>, que
    /// acrescenta <c>TARGET FIELDS</c> logo depois de <c>KNOWN NAMES</c> quando há campos alvo; sem eles, a saída é a
    /// do v1 byte a byte.
    /// </summary>
    public static AutocompleteRequest Build(AutocompleteContextSnapshot snapshot, AutocompleteSettings settings, bool includeTargetFields)
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
        if (includeTargetFields && snapshot.TargetFields is { Count: > 0 } targetFields && TargetFieldsLine(targetFields) is { Length: > 0 } line)
        {
            var label = snapshot.TargetCollection is { Length: > 0 } target && IsSafe(target) ? target + ": " : "";
            context.Append("TARGET FIELDS: " + label + line).Append(ContractNewLine);
        }
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

    /// <summary>
    /// Lista <c>caminho:tipo, ...</c> do v2 (espelho de <c>target_fields_line</c>): arrays como
    /// <c>array&lt;item&gt;</c>, <c>_id</c> e caminhos inseguros descartados, até <see cref="MaximumTargetFields"/>
    /// campos e <see cref="MaximumTargetFieldsCharacters"/> caracteres.
    /// </summary>
    public static string TargetFieldsLine(IEnumerable<AutocompleteTargetField> targetFields)
    {
        ArgumentNullException.ThrowIfNull(targetFields);
        var pieces = new List<string>();
        var size = 0;
        foreach (var field in targetFields)
        {
            if (string.IsNullOrEmpty(field.Path) || !IsSafe(field.Path) || field.Path == "_id") continue;
            var piece = field.Path + ":" + field.Type + (field.Type == "array" && !string.IsNullOrEmpty(field.Item) ? "<" + field.Item + ">" : "");
            if (pieces.Count >= MaximumTargetFields || size + piece.Length + 2 > MaximumTargetFieldsCharacters) break;
            pieces.Add(piece);
            size += piece.Length + 2;
        }
        return string.Join(", ", pieces);
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
