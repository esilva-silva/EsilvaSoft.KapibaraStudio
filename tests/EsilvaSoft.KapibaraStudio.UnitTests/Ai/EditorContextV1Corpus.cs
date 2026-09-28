using System.Globalization;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests.Ai;

/// <summary>
/// One case of the frozen editor-context-v1 corpus. <paramref name="Resolve"/> either replays a captured tab
/// through <see cref="AutocompleteContextBuilder.Build"/> or hand-builds an <see cref="AutocompleteRequest"/>
/// directly, to reach branches that <c>Build</c> itself can never produce (an unterminated header, a blank
/// context). <paramref name="Tags"/> feed <c>EditorContextV1GoldenTests.CorpusCoversEveryBranch</c>; every entry in
/// <c>EditorContextV1GoldenTests.RequiredTags</c> must be claimed by at least one case.
/// </summary>
public sealed record EditorContextCase(string Id, IReadOnlyList<string> Tags, Func<AutocompleteRequest> Resolve)
{
    public override string ToString() => Id;
}

/// <summary>
/// Corpus for the training contract consumed by SlopCoder packages. Mirrors the fixtures already exercised by
/// <c>PredictiveAutocompleteTests</c> and <c>LocalAutocompleteTests</c> (acceptance criterion 1 of phase 3) plus the
/// production shape used by <c>WorkspaceTabViewModel.CaptureAutocompleteRequest</c>.
/// </summary>
public static class EditorContextV1Corpus
{
    private static readonly string LongTail = new string('x', 90000) + "db.Customers.find({";

    public static readonly IReadOnlyList<EditorContextCase> Cases =
    [
        new("lang-json-baseline",
            ["lang:json", "opt:editorContext:on", "opt:resultPanel:on", "opt:inputPanel:on", "names:empty",
                "fields:empty", "input:empty", "recent:0", "fixture:predictive", "modelprefix:nontraining", "modelprefix:training"],
            () => AutocompleteContextBuilder.Build(new("", 0, "json"), new())),

        new("lang-mongosh-with-result-fields",
            ["lang:mongosh", "fields:nonempty", "fixture:predictive"],
            () => AutocompleteContextBuilder.Build(new("db.customers.find({na", 22, "JavaScript (mongosh)",
                ResultFields: ["Name", "AccountId"]), new())),

        new("lang-console-production-shape",
            ["lang:console", "fixture:local", "recent:3"],
            () => AutocompleteContextBuilder.Build(new("db.Customers.find({})", 22, "Mongo Console JavaScript",
                KnownNames: ["Production", "Customers", "Dev"],
                RecentCommands: ["db.Customers.findOne()", "db.Customers.count()", "db.Customers.drop()"]), new())),

        new("lang-null-defaults-to-shared-commands",
            ["lang:null"],
            () => AutocompleteContextBuilder.Build(new("", 0, null!), new())),

        new("names-overflow-takes-first-128",
            ["names:over-limit"],
            () => AutocompleteContextBuilder.Build(new("", 0, "json",
                KnownNames: Enumerable.Range(1, 129).Select(i => "Field" + i.ToString("000", CultureInfo.InvariantCulture)).ToArray()), new())),

        new("names-filters-sensitive-and-reserved-tokens",
            ["names:sensitive", "names:pipe-token", "names:reserved-token"],
            () => AutocompleteContextBuilder.Build(new("", 0, "json",
                KnownNames: ["Safe1", "password: hidden", "Weird<|Field", "Weird<｜Field", "Safe2"]), new())),

        new("result-panel-disabled-hides-fields",
            ["opt:resultPanel:off"],
            () => AutocompleteContextBuilder.Build(new("", 0, "json", ResultFields: ["Name", "AccountId"]),
                new() { UseResultPanelContext = false })),

        new("input-panel-disabled-hides-safe-input",
            ["opt:inputPanel:off"],
            () => AutocompleteContextBuilder.Build(new("", 0, "json", "{\"Name\":\"Eduardo\"}"),
                new() { UseInputPanelContext = false })),

        new("input-overlong-cuts-at-1024",
            ["input:overlong", "input:safe"],
            () => AutocompleteContextBuilder.Build(new("", 0, "json", new string('i', 2000)), new())),

        new("input-sensitive-excluded",
            ["input:sensitive", "fixture:predictive"],
            () => AutocompleteContextBuilder.Build(new("db.", 3, "javascript", "{\"password\":\"hidden\"}",
                ResultFields: [], KnownNames: ["Production"], RecentCommands: ["const api_key = 'hidden'"]), new())),

        new("recent-five-truncates-to-three-and-cuts-256",
            ["recent:5", "recent:overlong"],
            () => AutocompleteContextBuilder.Build(new("", 0, "json",
                RecentCommands: [new string('r', 300), "c2", "c3", "c4", "c5"]), new())),

        new("predictive-bounded-enabled",
            ["fixture:predictive", "window:text-larger-than-window", "window:caret-end"],
            () => AutocompleteContextBuilder.Build(new(LongTail, LongTail.Length, "javascript",
                "{\"Name\":\"Eduardo\"}", ["Name", "AccountId", "Active"], ["Production", "Customers"],
                ["db.Customers.findOne()"]), new())),

        new("predictive-bounded-disabled",
            ["fixture:predictive", "opt:editorContext:off", "opt:resultPanel:off", "opt:inputPanel:off"],
            () => AutocompleteContextBuilder.Build(new(LongTail, LongTail.Length, "javascript",
                    "{\"Name\":\"Eduardo\"}", ["Name", "AccountId", "Active"], ["Production", "Customers"],
                    ["db.Customers.findOne()"]),
                new() { UseInputPanelContext = false, UseResultPanelContext = false, UseEditorContext = false })),

        new("window-caret-zero",
            ["window:caret-zero"],
            () => AutocompleteContextBuilder.Build(new("return value;", 0, "json"), new())),

        new("window-mixed-eol-preserves-raw-bytes",
            ["window:mixed-eol"],
            () =>
            {
                const string text = "line1\r\nline2\nline3\r\nCURSOR";
                return AutocompleteContextBuilder.Build(new(text, text.IndexOf("CURSOR", StringComparison.Ordinal), "json"), new());
            }),

        new("window-surrogate-pair-split-by-char-index",
            ["window:surrogate-split"],
            () =>
            {
                var text = "abc" + '\uD83D' + '\uDE00' + "def";
                return AutocompleteContextBuilder.Build(new(text, 4, "json"), new());
            }),

        new("context-exceeds-8192-gets-bounded",
            ["context:over-8192"],
            () => AutocompleteContextBuilder.Build(new("", 0, "json",
                KnownNames: Enumerable.Range(0, 128).Select(i => "Name_" + i.ToString("0000", CultureInfo.InvariantCulture) + new string('a', 70)).ToArray()), new())),

        new("modelprefix-neutralizes-star-slash",
            ["modelprefix:contains-star-slash"],
            () => AutocompleteContextBuilder.Build(new("", 0, "JavaScript (mongosh)", "check */ neutralized but safe value"), new())),

        new("modelprefix-raw-commands-last-line-has-no-terminator",
            ["modelprefix:commands-last-line-no-break"],
            () => new AutocompleteRequest("db.", "", "javascript") { Context = "AVAILABLE COMMANDS: " + AutocompleteContextBuilder.Commands }),

        new("modelprefix-raw-blank-context-short-circuits",
            ["modelprefix:empty-context"],
            () => new AutocompleteRequest("db.", "", "javascript") { Context = "   " }),
    ];
}
