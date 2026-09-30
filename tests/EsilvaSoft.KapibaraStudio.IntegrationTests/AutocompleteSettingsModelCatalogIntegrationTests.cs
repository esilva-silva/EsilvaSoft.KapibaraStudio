using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;
using EsilvaSoft.KapibaraStudio.Infrastructure;
using EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;
using static EsilvaSoft.KapibaraStudio.IntegrationTests.LocalModelDiskFixture;
using EsilvaSoft.KapibaraStudio.UnitTests;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

[TestFixture]
public sealed class AutocompleteSettingsModelCatalogIntegrationTests
{
    private static readonly string[] TwoModels = ["Coder-0.5B", "Coder 1.5B"];
    private static readonly string[] RefreshedModels = ["Coder 1.5B", "Coder-3B"];

    [Test]
    public async Task PreferencesStoreFolderNamesAndRefreshKeepsTheSelection()
    {
        using var models = new SyntheticDirectory();
        CreateQwenModel(models.Path, "Coder-0.5B");
        CreateQwenModel(models.Path, "Coder-1.5B", "{\"name\":\"Coder 1.5B\",\"recommendedContextTokens\":1024,\"recommendedCompletionTokens\":64}");
        AutocompleteSettings? saved = null;
        var preferences = new AutocompleteSettingsViewModel(new CompletionServiceFake(), new LocalModelCatalog(models.Path, fileAccess: new LocalModelFileAccess()), settings => { saved = settings; return Task.CompletedTask; });
        preferences.Load(new());
        await preferences.RefreshModelsCommand.ExecuteAsync(null);
        Assert.That(preferences.Models.Select(option => option.Display), Is.EqualTo(TwoModels));
        preferences.SelectedModelOption = preferences.Models.Single(option => option.Reference == "Coder-1.5B");
        Assert.That((preferences.ContextTokens, preferences.MaximumTokens), Is.EqualTo((1024, 64)));
        await preferences.ApplyCommand.ExecuteAsync(null);
        Assert.That((saved!.SelectedModel, saved.ModelPath, saved.ModelDirectory), Is.EqualTo(("Coder-1.5B", "", "")));

        Directory.Delete(Path.Combine(models.Path, "Coder-0.5B"), true);
        CreateQwenModel(models.Path, "Coder-3B");
        await preferences.RefreshModelsCommand.ExecuteAsync(null);
        Assert.That(preferences.Models.Select(option => option.Display), Is.EqualTo(RefreshedModels));
        Assert.That(preferences.SelectedModelOption!.Reference, Is.EqualTo("Coder-1.5B"));
        Assert.That(preferences.ContextTokens, Is.EqualTo(1024));

        await preferences.SelectExternalModelAsync(Path.Combine(models.Path, "Coder-3B"));
        await preferences.ApplyCommand.ExecuteAsync(null);
        Assert.That((saved.SelectedModel, saved.ModelPath), Is.EqualTo(("Coder-3B", "")), "A folder inside the directory is not stored as an absolute path.");
        preferences.Load(saved);
        Assert.That(preferences.SelectedModelOption!.Reference, Is.EqualTo("Coder-3B"));
    }

    [Test]
    public async Task TokenEditorAcceptsTheExactInclusiveModelBoundary()
    {
        using var models = new SyntheticDirectory();
        var path = CreateQwenModel(models.Path, "Boundary", "{\"generation\":{\"autocomplete\":{\"maxTokens\":32}}}");
        File.WriteAllText(Path.Combine(path, "genai_config.json"), "{\"model\":{\"type\":\"qwen2\",\"context_length\":32768,\"decoder\":{\"filename\":\"model.onnx\"}}}");

        var preferences = new AutocompleteSettingsViewModel(new CompletionServiceFake(), new LocalModelCatalog(models.Path, fileAccess: new LocalModelFileAccess()), settings => Task.CompletedTask);
        preferences.Load(new());
        await preferences.RefreshModelsCommand.ExecuteAsync(null);
        preferences.SelectedModelOption = preferences.Models.Single();
        preferences.MaximumTokensText = "32";

        Assert.That(preferences.MaximumTokens, Is.EqualTo(32));
        Assert.That(preferences.HasTokenBudgetError, Is.False);
        Assert.That(preferences.MaximumTokenSuggestions, Does.Contain("32"));
        preferences.ContextTokensText = "32733";
        Assert.That(preferences.HasTokenBudgetError, Is.False, "The inclusive boundary includes the three FIM overhead tokens.");
        preferences.ContextTokensText = "32734";
        Assert.That(preferences.HasTokenBudgetError, Is.True);
        preferences.ContextTokensText = "16384";
        Assert.That(preferences.Snapshot().MaximumCompletionTokens, Is.EqualTo(32));
    }

    private sealed class CompletionServiceFake : IAutocompleteService
    {
        public AutocompleteSettings Settings { get; private set; } = new();
        public LocalModelStatus Status { get; } = new(LocalModelState.Ready, "Pronto · cpu");
        public event EventHandler? SettingsChanged;

        public Task ConfigureAsync(AutocompleteSettings settings, CancellationToken cancellationToken = default)
        {
            Settings = settings;
            SettingsChanged?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public Task<AutocompleteResult?> GetCompletionAsync(AutocompleteRequest request, CancellationToken cancellationToken = default) => Task.FromResult<AutocompleteResult?>(null);
        public Task<LocalModelStatus> TestModelAsync(CancellationToken cancellationToken = default) => Task.FromResult(Status);
    }
}
