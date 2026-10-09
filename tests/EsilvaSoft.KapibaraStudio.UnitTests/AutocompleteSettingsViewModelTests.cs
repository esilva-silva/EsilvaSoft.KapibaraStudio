using EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;
using EsilvaSoft.KapibaraStudio.Infrastructure;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;
using EsilvaSoft.KapibaraStudio.Testing;
using System.Text.Json;

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

    [Test]
    public void LocalAgentModelSelectionIsIndependentAndEmptyFollowsAutocomplete()
    {
        var preferences = new AutocompleteSettingsViewModel(new CompletionServiceFake(), catalog: null, save: _ => Task.CompletedTask);
        preferences.Load(new() { SelectedModel = "autocomplete-small", ChatModel = "agent-qwen" });

        Assert.That(preferences.ChatModel, Is.EqualTo("agent-qwen"));
        Assert.That(preferences.Snapshot().ChatModel, Is.EqualTo("agent-qwen"));

        preferences.UseAutocompleteModelForAgentCommand.Execute(null);
        Assert.That(preferences.ChatModel, Is.Empty);
        Assert.That(preferences.Snapshot().ChatModel, Is.Empty);

        preferences.ChatModel = "agent-qwen";
        preferences.Load(preferences.Snapshot());
        Assert.That(preferences.ChatModel, Is.EqualTo("agent-qwen"));
    }

    [Test]
    public async Task RefreshKeepsAnUnavailableSavedAgentModelVisible()
    {
        var preferences = new AutocompleteSettingsViewModel(new CompletionServiceFake(), new CompletionCatalogFake(),
            save: _ => Task.CompletedTask);
        preferences.Load(new() { ChatModel = "agent-qwen" });

        await preferences.RefreshModelsCommand.ExecuteAsync(null);

        var savedModel = preferences.AgentModels.Single(option => option.Reference == "agent-qwen");
        Assert.That(preferences.ChatModel, Is.EqualTo("agent-qwen"));
        Assert.That(savedModel.Validation?.Status.State, Is.EqualTo(LocalModelState.NotInstalled));
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
