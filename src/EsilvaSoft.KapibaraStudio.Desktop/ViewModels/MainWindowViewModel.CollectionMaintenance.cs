using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private static readonly string[] CollectionMaintenanceSources = ["hello", "listCollections", "collStats", "getCollectionValidation"];
    private static readonly JsonSerializerOptions CollectionMaintenanceJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };
    private CollectionMaintenancePlan? _collectionMaintenancePlan;

    [ObservableProperty]
    private string _collectionMaintenancePreviewText = string.Empty;

    public bool CanApplyCollectionIntegrityPlan => HasCurrentCollectionMaintenancePlan(CollectionMaintenanceKind.Validate)
        && SelectedProfile is { IsReadOnly: false }
        && string.Equals(SelectedCollection, CollectionIntegrityConfirmation.Trim(), StringComparison.Ordinal);

    public bool CanApplyCollectionCompactPlan => HasCurrentCollectionMaintenancePlan(CollectionMaintenanceKind.Compact)
        && SelectedProfile is { IsReadOnly: false }
        && string.Equals(SelectedCollection, CollectionCompactConfirmation.Trim(), StringComparison.Ordinal);

    public bool CanApplyCollectionValidationPlan => HasCurrentCollectionMaintenancePlan(CollectionMaintenanceKind.ConfigureValidation)
        && SelectedProfile is { IsReadOnly: false }
        && string.Equals(SelectedCollection, CollectionValidationConfirmation.Trim(), StringComparison.Ordinal);

    [RelayCommand]
    private Task PreviewCollectionIntegrityAsync() => PreviewCollectionMaintenanceAsync(CollectionMaintenanceKind.Validate);

    [RelayCommand]
    private Task PreviewCollectionCompactAsync() => PreviewCollectionMaintenanceAsync(CollectionMaintenanceKind.Compact);

    [RelayCommand]
    private Task PreviewCollectionValidationAsync() => PreviewCollectionMaintenanceAsync(CollectionMaintenanceKind.ConfigureValidation);

    [RelayCommand(CanExecute = nameof(CanApplyCollectionIntegrityPlan))]
    private Task ApplyCollectionIntegrityPlanAsync() => ApplyCollectionMaintenanceAsync(CollectionMaintenanceKind.Validate);

    [RelayCommand(CanExecute = nameof(CanApplyCollectionCompactPlan))]
    private Task ApplyCollectionCompactPlanAsync() => ApplyCollectionMaintenanceAsync(CollectionMaintenanceKind.Compact);

    [RelayCommand(CanExecute = nameof(CanApplyCollectionValidationPlan))]
    private Task ApplyCollectionValidationPlanAsync() => ApplyCollectionMaintenanceAsync(CollectionMaintenanceKind.ConfigureValidation);

    private async Task PreviewCollectionMaintenanceAsync(CollectionMaintenanceKind kind)
    {
        var profile = SelectedProfile;
        var database = SelectedDatabase;
        var collection = SelectedCollection;
        if (profile is null || string.IsNullOrWhiteSpace(database) || string.IsNullOrWhiteSpace(collection))
            return;

        ClearCollectionMaintenancePlan();
        var signature = CollectionMaintenanceSignature(profile, database, collection, kind);
        CollectionMaintenanceState? state = null;
        if (!await RunAsync(async cancellationToken =>
            {
                state = await ReadCollectionMaintenanceStateAsync(profile, database, collection, cancellationToken);
                if (!IsOriginalCollectionContext(profile, database, collection)
                    || !ReferenceEquals(profile, SelectedProfile)
                    || !string.Equals(signature, CurrentCollectionMaintenanceSignature(kind), StringComparison.Ordinal))
                    return;

                var topology = DescribeTopology(state.Preflight.TopologyJson);
                if (kind == CollectionMaintenanceKind.Compact && topology.IsRouter)
                    throw new InvalidOperationException(T("collectionMaintenanceCompactMongos"));
                if (kind == CollectionMaintenanceKind.Compact && topology.IsReplicaSetPrimary && !CollectionCompactForce)
                    throw new InvalidOperationException(T("collectionMaintenanceCompactPrimaryForce"));

                var risk = kind switch
                {
                    CollectionMaintenanceKind.Validate => T("collectionMaintenanceValidateRisk"),
                    CollectionMaintenanceKind.Compact => T("collectionMaintenanceCompactRisk"),
                    _ => T("collectionMaintenanceCollModRisk")
                };
                var capability = topology.IsRouter
                    ? T("collectionMaintenanceCapabilityRouter")
                    : topology.IsReplicaSetPrimary
                        ? T("collectionMaintenanceCapabilityPrimary")
                        : topology.IsReplicaSetMember
                            ? T("collectionMaintenanceCapabilityReplicaMember")
                            : T("collectionMaintenanceCapabilityUnknown");
                _collectionMaintenancePlan = new CollectionMaintenancePlan(
                    profile, database, collection, kind, signature, state, DateTimeOffset.UtcNow);
                CollectionMaintenancePreviewText = JsonSerializer.Serialize(new
                {
                    operation = kind.ToString(),
                    target = new { database, collection },
                    capability,
                    risk,
                    prerequisites = T("collectionMaintenancePrerequisites"),
                    resourceLimits = T("collectionMaintenanceResourceLimits"),
                    source = CollectionMaintenanceSources,
                    observedAt = _collectionMaintenancePlan.ObservedAt,
                    before = PresentCollectionMaintenanceState(state),
                    after = kind == CollectionMaintenanceKind.ConfigureValidation
                        ? new
                        {
                            validator = CollectionValidatorJson,
                            validationLevel = CollectionValidationLevel.ToString(),
                            validationAction = CollectionValidationAction.ToString()
                        }
                        : null
                }, CollectionMaintenanceJsonOptions);
                NotifyCollectionMaintenanceCommands();
            }))
        {
            ClearCollectionMaintenancePlan();
            return;
        }
    }

    private async Task ApplyCollectionMaintenanceAsync(CollectionMaintenanceKind kind)
    {
        var plan = _collectionMaintenancePlan;
        var explicitlyConfirmed = IsCollectionMaintenanceConfirmed(kind);
        if (plan is null || !explicitlyConfirmed || !HasCurrentCollectionMaintenancePlan(kind)
            || !IsOriginalCollectionContext(plan.Profile, plan.Database, plan.Collection))
            return;

        var commandAttempted = false;
        var staleState = false;
        var succeeded = await RunAsync(async cancellationToken =>
        {
            var current = await ReadCollectionMaintenanceStateAsync(plan.Profile, plan.Database, plan.Collection, cancellationToken);
            if (!IsOriginalCollectionContext(plan.Profile, plan.Database, plan.Collection)
                || !HasCurrentCollectionMaintenancePlan(kind)
                || !IsCollectionMaintenanceConfirmed(kind))
                throw new InvalidOperationException(T("collectionMaintenancePlanStale"));
            if (!plan.State.Preflight.MatchesObservedState(current.Preflight))
            {
                staleState = true;
                throw new InvalidOperationException(T("collectionMaintenancePlanStale"));
            }

            string commandResult;
            switch (kind)
            {
                case CollectionMaintenanceKind.Validate:
                    commandAttempted = true;
                    var integrityRequest = new CollectionIntegrityCheckRequest(plan.Database, plan.Collection, CollectionIntegrityConfirmation, plan.State.Preflight);
                    commandResult = await _workspace.ValidateCollectionIntegrityAsync(plan.Profile, integrityRequest, cancellationToken);
                    break;
                case CollectionMaintenanceKind.Compact:
                    commandAttempted = true;
                    var compactRequest = new CollectionCompactRequest(plan.Database, plan.Collection, CollectionCompactConfirmation, CollectionCompactForce, plan.State.Preflight);
                    commandResult = await _workspace.CompactCollectionAsync(plan.Profile, compactRequest, cancellationToken);
                    break;
                case CollectionMaintenanceKind.ConfigureValidation:
                    commandAttempted = true;
                    var validationRequest = new CollectionValidationRequest(
                        plan.Database,
                        plan.Collection,
                        CollectionValidatorJson,
                        CollectionValidationLevel,
                        CollectionValidationAction,
                        CollectionValidationConfirmation,
                        plan.State.Preflight);
                    await _workspace.ConfigureCollectionValidationAsync(plan.Profile, validationRequest, cancellationToken);
                    commandResult = T("collectionMaintenanceCollModAccepted");
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }

            var after = await ReadCollectionMaintenanceStateAsync(plan.Profile, plan.Database, plan.Collection, cancellationToken);
            if (kind == CollectionMaintenanceKind.ConfigureValidation
                && !CollectionValidationReadback.Matches(
                    new CollectionValidationRequest(plan.Database, plan.Collection, CollectionValidatorJson,
                        CollectionValidationLevel, CollectionValidationAction, CollectionValidationConfirmation, plan.State.Preflight),
                    after.Preflight.Validation))
                throw new InvalidOperationException(T("collectionMaintenanceReadbackDiffers"));

            if (IsOriginalCollectionContext(plan.Profile, plan.Database, plan.Collection))
            {
                CollectionMaintenancePreviewText = JsonSerializer.Serialize(new
                {
                    operation = kind.ToString(),
                    target = new { plan.Database, plan.Collection },
                    completedAt = DateTimeOffset.UtcNow,
                    commandResult,
                    before = PresentCollectionMaintenanceState(plan.State),
                    after = PresentCollectionMaintenanceState(after)
                }, CollectionMaintenanceJsonOptions);
                AdministrationResults = commandResult;
                StatusMessage = T("collectionMaintenanceVerified");
            }
        });

        if (!succeeded && commandAttempted)
        {
            if (IsOriginalCollectionContext(plan.Profile, plan.Database, plan.Collection))
            {
                CollectionMaintenancePreviewText += Environment.NewLine + T("collectionMaintenanceEffectUncertain");
                StatusMessage = T("collectionMaintenanceEffectUncertain");
            }
            _collectionMaintenancePlan = null;
            NotifyCollectionMaintenanceCommands();
        }
        else if (!succeeded && staleState)
        {
            _collectionMaintenancePlan = null;
            CollectionMaintenancePreviewText = T("collectionMaintenancePlanStale");
            NotifyCollectionMaintenanceCommands();
        }
        if (succeeded)
        {
            await RecordAuditAsync(
                kind switch
                {
                    CollectionMaintenanceKind.Validate => "collection.validate",
                    CollectionMaintenanceKind.Compact => "collection.compact",
                    _ => "collection.validation.configure"
                },
                plan.Profile,
                plan.Database,
                plan.Collection,
                T("collectionMaintenanceAudit"));
            CollectionIntegrityConfirmation = string.Empty;
            CollectionCompactConfirmation = string.Empty;
            CollectionValidationConfirmation = string.Empty;
            if (kind == CollectionMaintenanceKind.Compact)
                CollectionCompactForce = false;
            NotifyCollectionMaintenanceCommands();
            _collectionMaintenancePlan = null;
        }
    }

    private async Task<CollectionMaintenanceState> ReadCollectionMaintenanceStateAsync(
        ConnectionProfile profile,
        string database,
        string collection,
        CancellationToken cancellationToken)
    {
        var topology = await _workspace.GetTopologyAsync(profile, cancellationToken);
        var definition = await _workspace.GetCollectionDefinitionAsync(profile, database, collection, cancellationToken);
        using (var parsed = JsonDocument.Parse(definition))
        {
            if (!parsed.RootElement.TryGetProperty("type", out var type)
                || !string.Equals(type.GetString(), "collection", StringComparison.Ordinal))
                throw new InvalidOperationException(T("collectionMaintenanceCollectionRequired"));
        }

        var stats = await _workspace.GetCollectionStatsAsync(profile, database, collection, cancellationToken);
        var validation = await _workspace.GetCollectionValidationAsync(profile, database, collection, cancellationToken);
        var preflight = new CollectionMaintenancePreflight(
            database,
            collection,
            topology,
            definition,
            stats,
            validation,
            CollectionMaintenancePreflight.ExpectedSource,
            DateTimeOffset.UtcNow).Validate(database, collection);
        return new CollectionMaintenanceState(preflight);
    }

    private bool HasCurrentCollectionMaintenancePlan(CollectionMaintenanceKind kind) =>
        _collectionMaintenancePlan is { } plan
        && plan.Kind == kind
        && ReferenceEquals(plan.Profile, SelectedProfile)
        && string.Equals(plan.Signature, CurrentCollectionMaintenanceSignature(kind), StringComparison.Ordinal);

    private bool IsCollectionMaintenanceConfirmed(CollectionMaintenanceKind kind) => kind switch
    {
        CollectionMaintenanceKind.Validate => CanApplyCollectionIntegrityPlan,
        CollectionMaintenanceKind.Compact => CanApplyCollectionCompactPlan,
        _ => CanApplyCollectionValidationPlan
    };

    private string CurrentCollectionMaintenanceSignature(CollectionMaintenanceKind kind) =>
        CollectionMaintenanceSignature(SelectedProfile, SelectedDatabase, SelectedCollection, kind);

    private string CollectionMaintenanceSignature(ConnectionProfile? profile, string? database, string? collection, CollectionMaintenanceKind kind) =>
        string.Join("\u001f",
            profile?.Id.ToString("D") ?? string.Empty,
            profile?.SourceGenerationId?.ToString("D") ?? string.Empty,
            database ?? string.Empty,
            collection ?? string.Empty,
            kind,
            kind == CollectionMaintenanceKind.Compact ? CollectionCompactForce.ToString() : string.Empty,
            kind == CollectionMaintenanceKind.ConfigureValidation ? CollectionValidatorJson : string.Empty,
            kind == CollectionMaintenanceKind.ConfigureValidation ? CollectionValidationLevel.ToString() : string.Empty,
            kind == CollectionMaintenanceKind.ConfigureValidation ? CollectionValidationAction.ToString() : string.Empty);

    private void ClearCollectionMaintenancePlan()
    {
        _collectionMaintenancePlan = null;
        CollectionMaintenancePreviewText = string.Empty;
        NotifyCollectionMaintenanceCommands();
    }

    private void NotifyCollectionMaintenanceCommands()
    {
        OnPropertyChanged(nameof(CanApplyCollectionIntegrityPlan));
        OnPropertyChanged(nameof(CanApplyCollectionCompactPlan));
        OnPropertyChanged(nameof(CanApplyCollectionValidationPlan));
        PreviewCollectionIntegrityCommand.NotifyCanExecuteChanged();
        PreviewCollectionCompactCommand.NotifyCanExecuteChanged();
        PreviewCollectionValidationCommand.NotifyCanExecuteChanged();
        ApplyCollectionIntegrityPlanCommand.NotifyCanExecuteChanged();
        ApplyCollectionCompactPlanCommand.NotifyCanExecuteChanged();
        ApplyCollectionValidationPlanCommand.NotifyCanExecuteChanged();
    }

    private static CollectionMaintenanceTopology DescribeTopology(string json)
    {
        using var parsed = JsonDocument.Parse(json);
        var root = parsed.RootElement;
        var isRouter = root.TryGetProperty("msg", out var message) && message.GetString() == "isdbgrid";
        var replica = root.TryGetProperty("setName", out var setName) && setName.ValueKind == JsonValueKind.String;
        var primary = root.TryGetProperty("isWritablePrimary", out var writable) && writable.ValueKind == JsonValueKind.True;
        return new CollectionMaintenanceTopology(isRouter, replica, primary);
    }

    private static object PresentCollectionMaintenanceState(CollectionMaintenanceState state) => new
    {
        topology = ParseElement(state.Preflight.TopologyJson),
        definition = ParseElement(state.Preflight.DefinitionJson),
        statistics = ParseElement(state.Preflight.StatisticsJson),
        validation = new
        {
            validator = ParseElement(state.Preflight.Validation.ValidatorJson),
            validationLevel = state.Preflight.Validation.ValidationLevel.ToString(),
            validationAction = state.Preflight.Validation.ValidationAction.ToString()
        },
        source = state.Preflight.Source,
        observedAt = state.Preflight.ObservedAt,
        fingerprint = state.Preflight.Fingerprint
    };

    private static JsonElement ParseElement(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    partial void OnCollectionIntegrityConfirmationChanged(string value) => NotifyCollectionMaintenanceCommands();
    partial void OnCollectionCompactConfirmationChanged(string value) => NotifyCollectionMaintenanceCommands();
    partial void OnCollectionCompactForceChanged(bool value) => ClearCollectionMaintenancePlan();
    partial void OnCollectionValidationConfirmationChanged(string value) => NotifyCollectionMaintenanceCommands();
    partial void OnCollectionValidatorJsonChanged(string value) => ClearCollectionMaintenancePlan();
    partial void OnCollectionValidationLevelChanged(CollectionValidationLevel value) => ClearCollectionMaintenancePlan();
    partial void OnCollectionValidationActionChanged(CollectionValidationAction value) => ClearCollectionMaintenancePlan();

    private enum CollectionMaintenanceKind { Validate, Compact, ConfigureValidation }
    private sealed record CollectionMaintenancePlan(
        ConnectionProfile Profile,
        string Database,
        string Collection,
        CollectionMaintenanceKind Kind,
        string Signature,
        CollectionMaintenanceState State,
        DateTimeOffset ObservedAt);
    private sealed record CollectionMaintenanceState(CollectionMaintenancePreflight Preflight);
    private sealed record CollectionMaintenanceTopology(bool IsRouter, bool IsReplicaSetMember, bool IsReplicaSetPrimary);
}
