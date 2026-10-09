using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture, NonParallelizable]
public sealed class MainWindowViewModelCollectionMaintenanceTests
{
    private const string BeforeStats = "{\"count\":1,\"size\":40,\"storageSize\":80,\"freeStorageSize\":40}";
    private const string AfterStats = "{\"count\":1,\"size\":40,\"storageSize\":72,\"freeStorageSize\":32}";
    private const string CollectionDefinition = "{\"type\":\"collection\",\"options\":{}}";
    private string _previousLanguage = "pt-BR";

    [SetUp]
    public void SetUp()
    {
        _previousLanguage = LocalizationViewModel.Current.Language;
        LocalizationViewModel.Current.Language = "pt-BR";
    }

    [TearDown]
    public void TearDown() => LocalizationViewModel.Current.Language = _previousLanguage;

    [Test]
    public async Task ViewMaterializationAuditsSuccessfulWriteAfterReadback()
    {
        using var context = new WorkspaceTestContext();
        const string viewDefinition = "{\"type\":\"view\",\"options\":{\"viewOn\":\"widgets\",\"pipeline\":[]}}";
        const string destinationDefinition = "{\"type\":\"collection\",\"options\":{}}";
        context.Mongo.Handler = (method, arguments) => method switch
        {
            "GetCollectionDefinitionAsync" => Task.FromResult((string)arguments[2]! == "active_widgets"
                ? viewDefinition
                : "{}"),
            "MaterializeViewAsync" => Task.FromResult(new ViewMaterializationResult(false, destinationDefinition)),
            _ => throw new InvalidOperationException(method)
        };
        var viewModel = CreateViewModel(context.Workspace);
        viewModel.SelectedCollection = "active_widgets";
        viewModel.MaterializationDestination = "widgets_snapshot";

        await viewModel.PreviewViewMaterializationCommand.ExecuteAsync(null);
        viewModel.MaterializationConfirmation = "widgets_snapshot";
        await viewModel.MaterializeViewCommand.ExecuteAsync(null);
        var audit = (await context.Repository.GetRecentAuditAsync()).Single();

        Assert.Multiple(() =>
        {
            Assert.That(audit.Action, Is.EqualTo("view.materialization"));
            Assert.That(audit.Database, Is.EqualTo("sample_mflix"));
            Assert.That(audit.Collection, Is.EqualTo("widgets_snapshot"));
            Assert.That(viewModel.StatusMessage, Does.Contain("criado"));
        });
    }

    [Test]
    public async Task ViewMaterializationAuditsUncertainEffectWhenServerCallFails()
    {
        using var context = new WorkspaceTestContext();
        const string viewDefinition = "{\"type\":\"view\",\"options\":{\"viewOn\":\"widgets\",\"pipeline\":[]}}";
        context.Mongo.Handler = (method, arguments) => method switch
        {
            "GetCollectionDefinitionAsync" => Task.FromResult((string)arguments[2]! == "active_widgets"
                ? viewDefinition
                : "{}"),
            "MaterializeViewAsync" => throw new InvalidOperationException("synthetic uncertain $out"),
            _ => throw new InvalidOperationException(method)
        };
        var viewModel = CreateViewModel(context.Workspace);
        viewModel.SelectedCollection = "active_widgets";
        viewModel.MaterializationDestination = "widgets_snapshot";

        await viewModel.PreviewViewMaterializationCommand.ExecuteAsync(null);
        viewModel.MaterializationConfirmation = "widgets_snapshot";
        await viewModel.MaterializeViewCommand.ExecuteAsync(null);
        var audit = (await context.Repository.GetRecentAuditAsync()).Single();

        Assert.Multiple(() =>
        {
            Assert.That(audit.Action, Is.EqualTo("view.materialization.uncertain"));
            Assert.That(audit.Database, Is.EqualTo("sample_mflix"));
            Assert.That(audit.Collection, Is.EqualTo("widgets_snapshot"));
            Assert.That(viewModel.StatusMessage, Does.Contain("Inspecione-o"));
            Assert.That(audit.Summary, Does.Not.Contain("synthetic uncertain"));
        });
    }

    [Test]
    public async Task CompactPreviewRefusesMongosBeforeDispatch()
    {
        using var context = new WorkspaceTestContext();
        var calls = new List<string>();
        context.Mongo.Handler = (method, _) =>
        {
            calls.Add(method);
            return method switch
            {
                "GetTopologyAsync" => Task.FromResult("{\"msg\":\"isdbgrid\"}"),
                "GetCollectionDefinitionAsync" => Task.FromResult(CollectionDefinition),
                "GetCollectionStatsAsync" => Task.FromResult(BeforeStats),
                "GetCollectionValidationAsync" => Task.FromResult(new CollectionValidationInfo("{}", CollectionValidationLevel.Strict, CollectionValidationAction.Error)),
                "CompactCollectionAsync" => throw new AssertionException("compact must be refused during preview"),
                _ => throw new InvalidOperationException(method)
            };
        };
        var viewModel = CreateViewModel(context.Workspace);

        await viewModel.PreviewCollectionCompactCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(calls, Does.Contain("GetTopologyAsync"));
            Assert.That(calls, Does.Not.Contain("CompactCollectionAsync"));
            Assert.That(viewModel.StatusMessage, Does.Contain("mongos"));
            Assert.That(viewModel.CanApplyCollectionCompactPlan, Is.False);
        });
    }

    [Test]
    public async Task StaleBeforeStateRefusesValidationWithoutDispatch()
    {
        using var context = new WorkspaceTestContext();
        var statsReads = 0;
        var validationCalls = 0;
        context.Mongo.Handler = (method, _) => method switch
        {
            "GetTopologyAsync" => Task.FromResult("{\"setName\":\"rs0\",\"isWritablePrimary\":false}"),
            "GetCollectionDefinitionAsync" => Task.FromResult(CollectionDefinition),
            "GetCollectionStatsAsync" => Task.FromResult(Interlocked.Increment(ref statsReads) == 1 ? BeforeStats : AfterStats),
            "GetCollectionValidationAsync" => Task.FromResult(new CollectionValidationInfo("{}", CollectionValidationLevel.Strict, CollectionValidationAction.Error)),
            "ValidateCollectionIntegrityAsync" => Interlocked.Increment(ref validationCalls) == 1
                ? Task.FromResult("{\"valid\":true}")
                : Task.FromResult("{\"valid\":true}"),
            _ => throw new InvalidOperationException(method)
        };
        var viewModel = CreateViewModel(context.Workspace);

        await viewModel.PreviewCollectionIntegrityCommand.ExecuteAsync(null);
        viewModel.CollectionIntegrityConfirmation = "widgets";
        await viewModel.ApplyCollectionIntegrityPlanCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(statsReads, Is.EqualTo(2));
            Assert.That(validationCalls, Is.Zero);
            Assert.That(viewModel.StatusMessage, Does.Contain("mudou desde a prévia"));
        });
    }

    [Test]
    public async Task CompactReportContainsCapturedBeforeAndObservedAfterState()
    {
        using var context = new WorkspaceTestContext();
        var statsReads = 0;
        context.Mongo.Handler = (method, _) => method switch
        {
            "GetTopologyAsync" => Task.FromResult("{}"),
            "GetCollectionDefinitionAsync" => Task.FromResult(CollectionDefinition),
            "GetCollectionStatsAsync" => Task.FromResult(Interlocked.Increment(ref statsReads) < 3 ? BeforeStats : AfterStats),
            "GetCollectionValidationAsync" => Task.FromResult(new CollectionValidationInfo("{}", CollectionValidationLevel.Strict, CollectionValidationAction.Error)),
            "CompactCollectionAsync" => Task.FromResult("{\"bytesFreed\":8}"),
            _ => throw new InvalidOperationException(method)
        };
        var viewModel = CreateViewModel(context.Workspace);

        await viewModel.PreviewCollectionCompactCommand.ExecuteAsync(null);
        viewModel.CollectionCompactConfirmation = "widgets";
        await viewModel.ApplyCollectionCompactPlanCommand.ExecuteAsync(null);
        using var report = System.Text.Json.JsonDocument.Parse(viewModel.CollectionMaintenancePreviewText);

        Assert.Multiple(() =>
        {
            Assert.That(report.RootElement.GetProperty("before").GetProperty("statistics").GetProperty("storageSize").GetInt32(), Is.EqualTo(80));
            Assert.That(report.RootElement.GetProperty("after").GetProperty("statistics").GetProperty("storageSize").GetInt32(), Is.EqualTo(72));
            Assert.That(viewModel.CollectionMaintenancePreviewText, Does.Contain("bytesFreed"));
            Assert.That(viewModel.StatusMessage, Does.Contain("estado posterior relido"));
        });
    }

    [Test]
    public async Task CollModReportShowsValidatorBeforeAndReadBackAfter()
    {
        using var context = new WorkspaceTestContext();
        var current = new CollectionValidationInfo("{}", CollectionValidationLevel.Strict, CollectionValidationAction.Error);
        context.Mongo.Handler = (method, arguments) => method switch
        {
            "GetTopologyAsync" => Task.FromResult("{}"),
            "GetCollectionDefinitionAsync" => Task.FromResult(CollectionDefinition),
            "GetCollectionStatsAsync" => Task.FromResult(BeforeStats),
            "GetCollectionValidationAsync" => Task.FromResult(current),
            "ConfigureCollectionValidationAsync" => ApplyValidation(arguments),
            _ => throw new InvalidOperationException(method)
        };
        var viewModel = CreateViewModel(context.Workspace);
        viewModel.CollectionValidatorJson = "{\"status\":\"active\"}";
        viewModel.CollectionValidationLevel = CollectionValidationLevel.Moderate;

        await viewModel.PreviewCollectionValidationCommand.ExecuteAsync(null);
        viewModel.CollectionValidationConfirmation = "widgets";
        await viewModel.ApplyCollectionValidationPlanCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.CollectionMaintenancePreviewText, Does.Contain("Strict"));
            Assert.That(viewModel.CollectionMaintenancePreviewText, Does.Contain("Moderate"));
            Assert.That(viewModel.CollectionMaintenancePreviewText, Does.Contain("active"));
            Assert.That(viewModel.StatusMessage, Does.Contain("estado posterior relido"));
        });

        Task ApplyValidation(object?[] arguments)
        {
            var request = (CollectionValidationRequest)arguments[1]!;
            current = new CollectionValidationInfo(request.ValidatorJson, request.ValidationLevel, request.ValidationAction);
            return Task.CompletedTask;
        }
    }

    [Test]
    public async Task CommandFailureAfterDispatchDisplaysPossibleEffect()
    {
        using var context = new WorkspaceTestContext();
        var validateStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Mongo.Handler = (method, _) => method switch
        {
            "GetTopologyAsync" => Task.FromResult("{\"isWritablePrimary\":true}"),
            "GetCollectionDefinitionAsync" => Task.FromResult(CollectionDefinition),
            "GetCollectionStatsAsync" => Task.FromResult(BeforeStats),
            "GetCollectionValidationAsync" => Task.FromResult(new CollectionValidationInfo("{}", CollectionValidationLevel.Strict, CollectionValidationAction.Error)),
            "ValidateCollectionIntegrityAsync" => FailAfterDispatch(validateStarted),
            _ => throw new InvalidOperationException(method)
        };
        var viewModel = CreateViewModel(context.Workspace);
        await viewModel.PreviewCollectionIntegrityCommand.ExecuteAsync(null);
        viewModel.CollectionIntegrityConfirmation = "widgets";

        await viewModel.ApplyCollectionIntegrityPlanCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(validateStarted.Task.IsCompleted, Is.True);
            Assert.That(viewModel.CollectionMaintenancePreviewText, Does.Contain("Não presuma rollback"));
            Assert.That(viewModel.StatusMessage, Does.Contain("Não presuma rollback"));
        });
    }

    [Test]
    public async Task WorkspaceRejectsDirectMaintenanceCallsWithoutPreviewBeforeMongoDispatch()
    {
        using var context = new WorkspaceTestContext();
        var calls = 0;
        context.Mongo.Handler = (_, _) =>
        {
            calls++;
            throw new AssertionException("Mongo service must not be called without a maintenance preview.");
        };
        var profile = ConnectionProfile.Create("maintenance-test", "mongodb://localhost");

        Assert.Multiple(() =>
        {
            Assert.That(() => context.Workspace.ValidateCollectionIntegrityAsync(
                profile, new CollectionIntegrityCheckRequest("sample_mflix", "widgets", "widgets")),
                Throws.ArgumentException.With.Message.Contain("prévia obrigatória"));
            Assert.That(() => context.Workspace.CompactCollectionAsync(
                profile, new CollectionCompactRequest("sample_mflix", "widgets", "widgets")),
                Throws.ArgumentException.With.Message.Contain("prévia obrigatória"));
            Assert.That(calls, Is.Zero);
        });
        Assert.ThrowsAsync<ArgumentException>(async () => await context.Workspace.ConfigureCollectionValidationAsync(
            profile, new CollectionValidationRequest("sample_mflix", "widgets", "{}",
                CollectionValidationLevel.Strict, CollectionValidationAction.Error, "widgets")));
        Assert.That(calls, Is.Zero);
    }

    [Test]
    public async Task CancelAfterDispatchDisplaysPossibleEffect()
    {
        using var context = new WorkspaceTestContext();
        var validateStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Mongo.Handler = (method, arguments) => method switch
        {
            "GetTopologyAsync" => Task.FromResult("{\"isWritablePrimary\":true}"),
            "GetCollectionDefinitionAsync" => Task.FromResult(CollectionDefinition),
            "GetCollectionStatsAsync" => Task.FromResult(BeforeStats),
            "GetCollectionValidationAsync" => Task.FromResult(new CollectionValidationInfo("{}", CollectionValidationLevel.Strict, CollectionValidationAction.Error)),
            "ValidateCollectionIntegrityAsync" => WaitUntilCancelled(validateStarted, (CancellationToken)arguments[2]!),
            _ => throw new InvalidOperationException(method)
        };
        var viewModel = CreateViewModel(context.Workspace);
        await viewModel.PreviewCollectionIntegrityCommand.ExecuteAsync(null);
        viewModel.CollectionIntegrityConfirmation = "widgets";

        var apply = viewModel.ApplyCollectionIntegrityPlanCommand.ExecuteAsync(null);
        await validateStarted.Task;
        viewModel.CancelOperationCommand.Execute(null);
        await apply;

        Assert.That(viewModel.CollectionMaintenancePreviewText, Does.Contain("Não presuma rollback"));
    }

    private static MainWindowViewModel CreateViewModel(EsilvaSoft.KapibaraStudio.Application.WorkspaceService workspace) =>
        new(workspace, autoLoadCollections: false)
        {
            SelectedProfile = ConnectionProfile.Create("maintenance-test", "mongodb://localhost"),
            SelectedDatabase = "sample_mflix",
            SelectedCollection = "widgets"
        };

    private static Task<string> FailAfterDispatch(TaskCompletionSource entered)
    {
        entered.TrySetResult();
        return Task.FromException<string>(new InvalidOperationException("synthetic command failure"));
    }

    private static async Task<string> WaitUntilCancelled(TaskCompletionSource entered, CancellationToken cancellationToken)
    {
        entered.TrySetResult();
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return "{}";
    }
}
