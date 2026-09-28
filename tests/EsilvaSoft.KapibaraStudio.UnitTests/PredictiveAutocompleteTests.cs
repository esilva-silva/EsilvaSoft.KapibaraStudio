using EsilvaSoft.KapibaraStudio.LocalAi.Core;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class PredictiveAutocompleteTests
{
    [Test]
    public async Task ResultFieldsAndInputStayInsideTheirOriginatingTab()
    {
        using var context = new WorkspaceTestContext();
        var profile = ConnectionProfile.Create("Dev", "mongodb://host");
        context.Mongo.Handler = (_, _) => Task.FromResult(new QueryPage(["{\"_id\":1,\"AccountId\":\"private-value\",\"Active\":true}"], TimeSpan.Zero, false));
        var tab = new WorkspaceTabViewModel(context.Workspace) { Profile = profile, Database = "loja", Text = "db.Customers.find({})", InputJson = "{\"Name\":\"Eduardo\"}", IsConnected = true };
        await tab.ExecuteCommand.ExecuteAsync(null);
        var request = tab.CaptureAutocompleteRequest("Acc", 3);
        Assert.That(request.Dictionary, Does.Contain("AccountId"));
        Assert.That(request.Context, Does.Contain("Eduardo").And.Not.Contain("private-value"));
        var other = new WorkspaceTabViewModel(context.Workspace).CaptureAutocompleteRequest("Acc", 3);
        Assert.That(other.Dictionary, Does.Not.Contain("AccountId"));
        Assert.That(other.Context, Does.Not.Contain("Eduardo"));
        tab.Database = "other";
        Assert.That(tab.CaptureAutocompleteRequest("Acc", 3).Dictionary, Does.Not.Contain("AccountId"));
    }

    [Test]
    public async Task SuffixEchoIsNotInsertedTwice()
    {
        var runtime = new CompletionRuntimeFake { Handler = (_, _) => Task.FromResult(new ModelGenerationResult("a + b;\n}\nfunction extra() {}", 12, TimeSpan.Zero, "cpu")) };
        await using var ai = new AiAutocompleteProvider(new CompletionCatalogFake(), () => runtime);
        var result = await new AutocompleteService(ai).GetCompletionAsync(new("return ", ";\n}"));
        Assert.That(result?.Text, Is.EqualTo("a + b"));
    }
    [Test]
    public void ContextIsBoundedKeepsCursorAndHonorsIndependentOptions()
    {
        var text = new string('x', 90000) + "db.Customers.find({";
        var snapshot = new AutocompleteContextSnapshot(text, text.Length, "javascript", "{\"Name\":\"Eduardo\"}",
            ["Name", "AccountId", "Active"], ["Production", "Customers"], ["db.Customers.findOne()"]);
        var request = AutocompleteContextBuilder.Build(snapshot, new());
        Assert.That(request.Prefix, Has.Length.EqualTo(4096).And.EndsWith("db.Customers.find({"));
        Assert.That(request.Context, Does.Contain("Eduardo").And.Contain("AccountId").And.Contain("findOne").And.Contain("ConnectionPool"));
        Assert.That(request.Dictionary, Does.Contain("Active"));
        var disabled = AutocompleteContextBuilder.Build(snapshot, new() { UseInputPanelContext = false, UseResultPanelContext = false, UseEditorContext = false });
        Assert.That(disabled.Context, Does.Not.Contain("Eduardo").And.Not.Contain("AccountId").And.Not.Contain("findOne"));
        Assert.That(disabled.Prefix, Has.Length.EqualTo(128));
        Assert.That(disabled.Dictionary, Does.Not.Contain("Active"));
    }

    [Test]
    public void SensitiveAuxiliaryContentIsExcluded()
    {
        var request = AutocompleteContextBuilder.Build(new("db.", 3, "javascript", "{\"password\":\"hidden\"}",
            [], ["Production"], ["const api_key = 'hidden'"]), new());
        Assert.That(request.Context, Does.Not.Contain("hidden"));
    }

    [TestCase("json", "$match")]
    [TestCase("JavaScript (mongosh)", "getSiblingDB")]
    public void CommandContextRespectsTheEditorLanguage(string language, string supported)
    {
        var request = AutocompleteContextBuilder.Build(new("", 0, language), new());
        Assert.That(request.Context, Does.Contain(supported).And.Not.Contain("ConnectionPool"));
    }

    [TestCase("ConnectionPool.Production.Customers.find({", "ConnectionPool.")]
    [TestCase("Production.Customers", "Production.")]
    [TestCase("Customers.find({", "Customers")]
    [TestCase(".find({\n", ".find({")]
    [TestCase("\"Name\": \"Eduardo\",\n", "\"Name\": \"Eduardo\",")]
    [TestCase("\r\nnext", "\r\n")]
    [TestCase(" + b", " + b")]
    public void TabAcceptsSemanticPartsWithoutDroppingCharacters(string suggestion, string expected)
    {
        Assert.That(suggestion[..IncrementalCompletion.NextLength(suggestion)], Is.EqualTo(expected));
        var remainder = suggestion; var accepted = "";
        while (remainder.Length > 0)
        {
            var count = IncrementalCompletion.NextLength(remainder);
            Assert.That(count, Is.InRange(1, remainder.Length));
            accepted += remainder[..count]; remainder = remainder[count..];
        }
        Assert.That(accepted, Is.EqualTo(suggestion));
    }

    [Test]
    public async Task DictionaryPrecedesModelAndDoesNotWaitForDebounce()
    {
        var runtime = new CompletionRuntimeFake();
        await using var ai = new AiAutocompleteProvider(new CompletionCatalogFake(), () => runtime);
        var service = new AutocompleteService(ai);
        await service.ConfigureAsync(new() { DelayMilliseconds = 2000 });
        using var session = new CompletionSession();
        var result = session.RequestAsync(service, new("Acc", "") { Dictionary = ["AccountId"] });
        Assert.That(result.IsCompletedSuccessfully, Is.True);
        Assert.That((await result)?.Text, Is.EqualTo("ountId"));
        Assert.That(runtime.Initializations, Is.Zero);
    }

    [Test]
    public async Task CacheIncludesAuxiliaryContextAndDictionaryCanBeDisabled()
    {
        var runtime = new CompletionRuntimeFake();
        await using var ai = new AiAutocompleteProvider(new CompletionCatalogFake(), () => runtime);
        var service = new AutocompleteService(ai);
        await service.ConfigureAsync(new() { UseDictionary = false });
        var request = new AutocompleteRequest("Conn", "") { Context = "Production" };
        Assert.That(service.GetImmediateCompletion(request), Is.Null);
        await service.GetCompletionAsync(request); await service.GetCompletionAsync(request);
        await service.GetCompletionAsync(request with { Context = "Development" });
        Assert.That(runtime.Generations, Is.EqualTo(2));
    }
}
