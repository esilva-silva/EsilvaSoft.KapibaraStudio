using EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;
using EsilvaSoft.KapibaraStudio.Infrastructure;
using System.Text.Json;
using static EsilvaSoft.KapibaraStudio.UnitTests.LocalModelFolderFixture;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class AutocompleteSettingsViewModelTests
{
    [Test]
    public void LocalAiContextConsentIsOptInAndInputJsonRequiresSeparateOptIn()
    {
        var preferences = new AutocompleteSettingsViewModel(new CompletionServiceFake(), catalog: null, save: _ => Task.CompletedTask);
        var defaults = new AutocompleteSettings();
        preferences.Load(defaults);

        Assert.That(defaults.LocalAiContextEnabled, Is.False);
        Assert.That(defaults.IncludeInputJsonInLocalAiContext, Is.False);
        Assert.That(preferences.Snapshot().LocalAiContextEnabled, Is.False);
        Assert.That(preferences.Snapshot().IncludeInputJsonInLocalAiContext, Is.False);

        preferences.LocalAiContextEnabled = true;
        preferences.IncludeInputJsonInLocalAiContext = true;
        var optedIn = preferences.Snapshot();
        Assert.That(optedIn.LocalAiContextEnabled, Is.True);
        Assert.That(optedIn.IncludeInputJsonInLocalAiContext, Is.True);
        var restored = JsonSerializer.Deserialize<AutocompleteSettings>(JsonSerializer.Serialize(optedIn));
        Assert.That(restored, Is.EqualTo(optedIn));
    }
    private static readonly string[] TwoModels = ["Coder-0.5B", "Coder 1.5B"];
    private static readonly string[] RefreshedModels = ["Coder 1.5B", "Coder-3B"];

    [Test]
    public async Task PreferencesStoreFolderNamesAndRefreshKeepsTheSelection()
    {
        using var models = new TemporaryDirectory();
        CreateQwenModel(models.Path, "Coder-0.5B");
        CreateQwenModel(models.Path, "Coder-1.5B", "{\"name\":\"Coder 1.5B\",\"recommendedContextTokens\":1024,\"recommendedCompletionTokens\":64}");
        AutocompleteSettings? saved = null;
        var preferences = new AutocompleteSettingsViewModel(new CompletionServiceFake(), new LocalModelCatalog(models.Path), settings => { saved = settings; return Task.CompletedTask; });
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
    public async Task UntouchedInlineFlagsStayAbsentAndFollowTheirLiveDefaults()
    {
        AutocompleteSettings? saved = null;
        var preferences = new AutocompleteSettingsViewModel(new CompletionServiceFake(), catalog: null, settings => { saved = settings; return Task.CompletedTask; });
        preferences.Load(new());

        // Nothing was touched: the effective values mirror the defaults, and no override is materialized.
        Assert.That((preferences.InlineEnabledEffective, preferences.InlineUseTraditionalEffective, preferences.InlineUseAiEffective), Is.EqualTo((true, true, false)));
        Assert.That((preferences.InlineEnabledIsOverridden, preferences.InlineUseTraditionalIsOverridden, preferences.InlineUseAiIsOverridden), Is.EqualTo((false, false, false)));

        // InlineUseTraditional keeps tracking UseDictionary live while it has no explicit override.
        preferences.UseDictionary = false;
        Assert.That(preferences.InlineUseTraditionalEffective, Is.False);
        Assert.That(preferences.InlineUseTraditionalIsOverridden, Is.False);

        await preferences.ApplyCommand.ExecuteAsync(null);
        Assert.That(saved!.InlineEnabledValue, Is.Null, "Never touched by the user: the field stays absent in the persisted document.");
        Assert.That(saved.InlineUseTraditionalValue, Is.Null);
        Assert.That(saved.InlineUseAiValue, Is.Null);
        // The effective (derived) values must still be correct for a fresh load elsewhere in the app.
        Assert.That((saved.InlineEnabled, saved.InlineUseTraditional, saved.InlineUseAi), Is.EqualTo((true, false, false)));
    }

    [Test]
    public async Task TogglingAnInlineFlagMaterializesAnExplicitValueAndResetClearsIt()
    {
        AutocompleteSettings? saved = null;
        var preferences = new AutocompleteSettingsViewModel(new CompletionServiceFake(), catalog: null, settings => { saved = settings; return Task.CompletedTask; });
        preferences.Load(new());

        preferences.InlineUseAiEffective = true;
        Assert.That(preferences.InlineUseAiIsOverridden, Is.True);
        await preferences.ApplyCommand.ExecuteAsync(null);
        Assert.That(saved!.InlineUseAiValue, Is.True, "A user toggle must persist as an explicit value, not just the effective default.");

        preferences.ResetInlineUseAiCommand.Execute(null);
        Assert.That(preferences.InlineUseAiIsOverridden, Is.False);
        Assert.That(preferences.InlineUseAiEffective, Is.False);
        await preferences.ApplyCommand.ExecuteAsync(null);
        Assert.That(saved.InlineUseAiValue, Is.Null, "Usar padrão must restore the absent state, not just flip the checkbox back to false.");

        // An explicit false must be distinguished from absence when reloaded (round-trip through Load/Snapshot).
        preferences.InlineEnabledEffective = false;
        var snapshot = preferences.Snapshot();
        Assert.That(snapshot.InlineEnabledValue, Is.False);
        preferences.Load(snapshot);
        Assert.That((preferences.InlineEnabledEffective, preferences.InlineEnabledIsOverridden), Is.EqualTo((false, true)));
    }

    [Test]
    public async Task CompletionListPreferencesRoundTrip()
    {
        AutocompleteSettings? saved = null;
        var preferences = new AutocompleteSettingsViewModel(new CompletionServiceFake(), catalog: null, settings => { saved = settings; return Task.CompletedTask; });
        preferences.Load(new() { CompletionAutoOpenOnTrigger = true, CompletionEnterAccepts = false });
        Assert.That((preferences.CompletionAutoOpenOnTrigger, preferences.CompletionEnterAccepts), Is.EqualTo((true, false)));
        await preferences.ApplyCommand.ExecuteAsync(null);
        Assert.That((saved!.CompletionAutoOpenOnTrigger, saved.CompletionEnterAccepts), Is.EqualTo((true, false)));
    }

    [Test]
    public void ManualHardwareProfileDrivesSuggestionsAndPersistsAsAdditiveJson()
    {
        var preferences = new AutocompleteSettingsViewModel(new CompletionServiceFake(), catalog: null, settings => Task.CompletedTask);
        preferences.Load(new());
        preferences.HardwareProfileVendor = "AMD";
        preferences.HardwareProfileName = "Radeon RX 7800 XT";
        preferences.HardwareProfileMemoryGiB = "16";
        preferences.AddHardwareProfileCommand.Execute(null);

        Assert.That(preferences.SelectedHardwareProfile!.TierLabel, Is.EqualTo("Alto"));
        Assert.That(preferences.ContextTokenSuggestions, Does.Contain("4096"));
        Assert.That(preferences.ContextTokenSuggestions, Has.None.Contains("."));
        var snapshot = preferences.Snapshot();
        Assert.That(snapshot.HardwareProfilesJson, Does.Contain("Radeon RX 7800 XT"));

        preferences.Load(snapshot);
        Assert.That(preferences.SelectedHardwareProfile!.Name, Is.EqualTo("Radeon RX 7800 XT"));
    }

    [Test]
    public void FreeTypedBudgetsRejectAnInvalidCombinedWindow()
    {
        var preferences = new AutocompleteSettingsViewModel(new CompletionServiceFake(), catalog: null, settings => Task.CompletedTask);
        preferences.Load(new());
        preferences.ContextTokensText = "8192";
        preferences.MaximumTokensText = "256";

        Assert.That(preferences.HasTokenBudgetError, Is.True);
        Assert.Throws<ArgumentException>(() => preferences.Snapshot());
    }

    [TestCase("8.192")]
    [TestCase("8,192")]
    public void TokenEditorsRejectCultureSpecificGrouping(string text)
    {
        var preferences = new AutocompleteSettingsViewModel(new CompletionServiceFake(), catalog: null, settings => Task.CompletedTask);
        preferences.Load(new());
        preferences.ContextTokensText = text;

        Assert.That(preferences.HasTokenBudgetError, Is.True);
        Assert.That(preferences.TokenBudgetWarning, Does.Contain("somente dígitos"));
        Assert.Throws<ArgumentException>(() => preferences.Snapshot());
    }

    [Test]
    public async Task TokenEditorAcceptsTheExactInclusiveModelBoundary()
    {
        using var models = new TemporaryDirectory();
        var path = CreateQwenModel(models.Path, "Boundary", "{\"generation\":{\"autocomplete\":{\"maxTokens\":32}}}");
        File.WriteAllText(Path.Combine(path, "genai_config.json"), "{\"model\":{\"type\":\"qwen2\",\"context_length\":32768,\"decoder\":{\"filename\":\"model.onnx\"}}}");

        var preferences = new AutocompleteSettingsViewModel(new CompletionServiceFake(), new LocalModelCatalog(models.Path), settings => Task.CompletedTask);
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

    [Test]
    public void EditableBudgetTextIsCommittedToTheSavedSettings()
    {
        var preferences = new AutocompleteSettingsViewModel(new CompletionServiceFake(), catalog: null, settings => Task.CompletedTask);
        preferences.Load(new());
        preferences.ContextTokensText = "4096";
        preferences.MaximumTokensText = "64";

        var snapshot = preferences.Snapshot();

        Assert.That((snapshot.ContextTokens, snapshot.MaximumCompletionTokens), Is.EqualTo((4096, 64)));
        Assert.That(preferences.HasTokenBudgetError, Is.False);
    }

    [Test]
    public async Task TestCommandShowsTokenValidationInsteadOfHidingIt()
    {
        var preferences = new AutocompleteSettingsViewModel(new CompletionServiceFake(), catalog: null, settings => Task.CompletedTask);
        preferences.Load(new());
        preferences.MaximumTokensText = "";

        await preferences.TestCommand.ExecuteAsync(null);

        Assert.That(preferences.OperationStatus, Does.Contain("Tokens gerados"));
    }

    [Test]
    public void TokenBudgetsSurviveSettingsJsonRoundTrip()
    {
        var original = new AutocompleteSettings { ContextTokens = 3072, MaximumCompletionTokens = 77 };
        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<AutocompleteSettings>(json);

        Assert.That(restored, Is.Not.Null);
        Assert.That((restored!.ContextTokens, restored.MaximumCompletionTokens), Is.EqualTo((3072, 77)));
    }
}
