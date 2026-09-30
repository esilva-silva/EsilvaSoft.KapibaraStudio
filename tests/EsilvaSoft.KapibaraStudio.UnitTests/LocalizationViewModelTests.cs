using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;
using EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class LocalizationViewModelTests
{
    private static readonly string[] AgentPanelKeys =
    [
        "agentAvailabilityRetry", "agentAvailabilityAvailable", "agentAvailabilityChecking", "agentAvailabilityCliMissing",
        "agentAvailabilityFailed", "agentAvailabilityNotConnected", "agentAvailabilityTimedOut", "agentChipActive",
        "agentChipQualified", "agentChipRemove", "agentChipSize", "agentChipUntitled", "agentChipKindActive",
        "agentChipKindWorkspace", "agentChipKindExternal", "agentChipKindTab", "agentConversationUntitled",
        "agentMessageAttachments", "agentSummaryUnreadable", "agentSummaryMongoRead", "agentSummaryWorkspaceFiles",
        "agentSummaryExternalFiles", "agentSummaryToolsUnavailable", "agentSummaryNoHistory", "agentHistoryEmpty",
        "agentHistoryLimit", "agentHistoryListFailed", "agentHistoryMeta", "agentHistoryNewerVersion", "agentHistoryNoDate",
        "agentHistoryNoStore", "agentHistoryOpen", "agentHistoryOpenFailed", "agentHistoryOpenMissing",
        "agentHistoryOpenNewerVersion", "agentHistoryOpenUnreadable", "agentHistoryPermissionsUnknown", "agentHistoryRename",
        "agentHistoryRenameConflict", "agentHistoryRenameFailed", "agentHistoryToday", "agentHistoryUnreadable",
        "agentHistoryYesterday", "agentHistoryDeleteBusy", "agentHistoryDeleteConflict", "agentHistoryDeleteFailed",
        "agentHistoryDeleted", "agentHistoryDisabled", "agentPersistenceConflict", "agentPersistenceConflictMerged",
        "agentPersistenceFailed", "agentPersistenceLimit", "agentPersistenceStoredUnreadable", "agentPickerEmpty",
        "agentPickerFailed", "agentPickerInvalidExclusion", "agentPickerLoading", "agentPickerNotPermitted",
        "agentPickerNoWorkspace", "agentPickerTruncated", "agentStateAttachmentsRefused", "agentBlockConsent",
        "agentBlockInvalid", "agentBlockLoading", "agentBlockUnreadable", "agentBlockConfigure", "agentBlockOpenPermissions",
    ];

    [TestCase("pt-BR", "Conexões")]
    [TestCase("en", "Connections")]
    [TestCase("es", "Conexiones")]
    [TestCase("zh-CN", "连接")]
    public void ResolvesToolbarTextForEachSupportedLanguage(string language, string expected)
    {
        var localization = new LocalizationViewModel { Language = language };

        Assert.That(localization.Connections, Is.EqualTo(expected));
    }

    [Test]
    public void MissingKeyFallsBackToEnglishAndThenToVisibleMarker()
    {
        var localization = new LocalizationViewModel { Language = "zh-CN" };

        Assert.Multiple(() =>
        {
            Assert.That(localization.Resolve("connections"), Is.EqualTo("连接"));
            Assert.That(localization.Resolve("newScriptShortcut"), Is.EqualTo("新建脚本 (Ctrl+T)"));
            Assert.That(localization.Resolve("missing.key"), Is.EqualTo("[[missing.key]]"));
        });
    }

    [Test]
    public void InvalidLanguageUsesEnglishCatalog()
    {
        var localization = new LocalizationViewModel { Language = "fr" };

        Assert.That(localization.Language, Is.EqualTo(ApplicationLanguages.FallbackCode));
        Assert.That(localization.Connections, Is.EqualTo("Connections"));
    }

    [Test]
    public void EveryCatalogKeyHasAnExplicitTranslationInEverySupportedLanguage()
    {
        var localization = new LocalizationViewModel();
        foreach (var language in ApplicationLanguages.All)
        {
            localization.Language = language.Code;
            foreach (var key in LocalizationViewModel.TranslationKeys)
                Assert.That(localization.HasTranslation(key), Is.True, $"{language.Code}/{key}");
        }
    }

    [Test]
    public void AgentPanelRuntimeKeysResolveInEverySupportedLanguage()
    {
        var localization = new LocalizationViewModel();
        foreach (var language in ApplicationLanguages.All)
        {
            localization.Language = language.Code;
            foreach (var key in AgentPanelKeys)
            {
                Assert.That(localization.Resolve(key), Does.Not.Contain("[["), $"{language.Code}/{key}");
            }
        }
    }

    [Test]
    public void DynamicMessagesUseTheSelectedLanguageAndKeepPlaceholders()
    {
        var localization = new LocalizationViewModel();
        foreach (var language in ApplicationLanguages.All)
        {
            localization.Language = language.Code;
            var text = localization.Format("queryReturned", 3, 12);
            Assert.That(text, Does.Not.Contain("[["), language.Code);
            Assert.That(text, Does.Contain("3"), language.Code);
            Assert.That(text, Does.Contain("12"), language.Code);
        }
    }

    [TestCase("pt-BR", "Tempo limite excedido")]
    [TestCase("en", "Timed out")]
    [TestCase("es", "Tiempo agotado")]
    [TestCase("zh-CN", "已超时")]
    public void OperationDiagnosticsUseTheSelectedLanguage(string language, string expected)
    {
        var localization = new LocalizationViewModel { Language = language };

        Assert.That(OperationErrorMessages.Describe(new TimeoutException(), localize: localization.Resolve),
            Does.StartWith(expected));
    }

    [TestCase("pt-BR", "Modelo selecionado")]
    [TestCase("en", "Selected model")]
    [TestCase("es", "Modelo seleccionado")]
    [TestCase("zh-CN", "已选模型")]
    public void LocalAiStatusUsesTheSelectedLanguageForItsLabels(string language, string expected)
    {
        var localization = new LocalizationViewModel { Language = language };
        var text = LocalAiStatusFormatter.Format(
            new LocalModelStatus(LocalModelState.NotLoaded, "model is not loaded"),
            "sample-model", AiAccelerationMode.Auto, [], localization.Resolve);

        Assert.That(text, Does.Contain(expected));
        if (language != "pt-BR") Assert.That(text, Does.Not.Contain("Modelo selecionado:"), language);
    }

    [TestCase("en", "ONNX Runtime was not loaded in this process.")]
    [TestCase("es", "ONNX Runtime no se cargó en este proceso.")]
    [TestCase("zh-CN", "此进程未加载 ONNX Runtime。")]
    public void HardwareAvailabilityReasonsUseTheSelectedLanguage(string language, string expected)
    {
        var localization = new LocalizationViewModel { Language = language };
        var device = new AiHardwareDevice(AiAccelerationMode.Cpu, "CPU", "CPU", false)
        {
            Reason = "ONNX Runtime não foi carregado neste processo."
        };

        Assert.That(LocalAiStatusFormatter.DeviceLine(device, localization.Resolve), Does.Contain(expected));
    }

    [TestCase("pt-BR", "Modelo ainda não carregado; será validado sob demanda.")]
    [TestCase("en", "Model is not loaded yet; it will be validated on demand.")]
    [TestCase("es", "El modelo aún no está cargado; se validará bajo demanda.")]
    [TestCase("zh-CN", "模型尚未加载；将按需验证。")]
    public async Task LocalAiModelOperationsUseTheSelectedLanguage(string language, string expected)
    {
        var localization = new LocalizationViewModel { Language = language };
        await using var service = new LocalAiModelService(new CompletionCatalogFake(), () => new CompletionRuntimeFake());
        service.SetLocalization(localization.Resolve);

        await service.UnloadModelAsync();

        Assert.That(service.Status.Message, Is.EqualTo(expected));
    }

    [Test]
    public async Task MetadataOperationsUseTheSelectedLanguage()
    {
        var localization = new LocalizationViewModel { Language = "en" };
        var operations = new ApplicationOperationService();
        using var cache = new MetadataCache(new FakeMetadataSource(), operations);
        cache.SetLocalization(localization.Resolve);

        var profile = ConnectionProfile.Create("sample", "mongodb://localhost");
        cache.Connect(profile);
        cache.SetSchemaSamplingAllowed(profile.Id, true);
        await cache.SampleSchemaAsync(profile, "db", "items");

        Assert.That(operations.LastCompleted?.Description, Does.Contain("Schema sampled"));
    }

    [Test]
    public void ConsoleHistoryAndResultPresentationUseTheSelectedLanguage()
    {
        var previousLanguage = LocalizationViewModel.Current.Language;
        LocalizationViewModel.Current.Language = "en";
        var history = new LocalizedConsoleHistoryItem(new ConsoleHistoryEntry(1, Guid.NewGuid(), DateTimeOffset.UtcNow,
            Guid.NewGuid(), "Dev", "shop", "Development", "db.orders.find({})", 1, "Concluído", [])
        { Mode = "Agregação", Collection = "orders" });
        var result = new LocalizedConsoleResultItem(new ConsoleResultSet(1, "[]", Database: "shop", Collection: "orders", Documents: [], IsTruncated: true));

        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(history.DisplayText, Does.Contain("Aggregation").And.Contain("Completed"));
                Assert.That(result.DisplayText, Does.Contain("document(s)").And.Contain("limited"));
            });

            LocalizationViewModel.Current.Language = "zh-CN";
            history.RefreshLanguage();
            result.RefreshLanguage();
            Assert.That(history.DisplayText, Does.Contain("聚合").And.Contain("已完成"));
            Assert.That(result.DisplayText, Does.Contain("个文档").And.Contain("已限制"));
        }
        finally { LocalizationViewModel.Current.Language = previousLanguage; }
    }

    [TestCase("pt-BR", "Padrão em CPU: menor e mais rápido")]
    [TestCase("en", "Default on CPU: smaller and faster")]
    [TestCase("es", "Predeterminado en CPU: más pequeño y rápido")]
    [TestCase("zh-CN", "CPU 默认：更小、更快")]
    public void BuiltInRemoteModelHintsUseTheSelectedLanguage(string language, string expected)
    {
        var previousLanguage = LocalizationViewModel.Current.Language;
        LocalizationViewModel.Current.Language = language;
        try
        {
            var variant = new RemoteModelVariant("esilva/sample", new string('a', 40), "int4", "sample-int4", 1,
                null, new Uri("https://huggingface.co/esilva/sample/tree/main/int4"), [])
            {
                Family = "Sample", Hint = "padrão em CPU: menor e mais rápido", HintKey = "remoteHintCpuCompact"
            };

            Assert.That(new RemoteModelOption(variant, false).Subtitle, Does.Contain(expected));
        }
        finally { LocalizationViewModel.Current.Language = previousLanguage; }
    }
}