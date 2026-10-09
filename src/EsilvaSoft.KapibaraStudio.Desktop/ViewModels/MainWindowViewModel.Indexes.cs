using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    [ObservableProperty]
    private string _indexKeys = "{ \"campo\": 1 }";

    [ObservableProperty]
    private string _indexName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDropIndex))]
    [NotifyCanExecuteChangedFor(nameof(DropIndexCommand))]
    private string _indexNameToDrop = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSetIndexVisibility))]
    [NotifyCanExecuteChangedFor(nameof(SetIndexVisibilityCommand))]
    private string _indexNameForVisibility = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSetIndexVisibility))]
    [NotifyCanExecuteChangedFor(nameof(SetIndexVisibilityCommand))]
    private string _indexVisibilityConfirmation = string.Empty;

    [ObservableProperty]
    private bool _indexVisibilityHidden;

    [ObservableProperty]
    private bool _indexIsUnique;

    [ObservableProperty]
    private bool _indexIsSparse;

    [ObservableProperty]
    private bool _indexIsHidden;

    [ObservableProperty]
    private decimal? _indexExpireAfterSeconds;

    [ObservableProperty]
    private string _indexPartialFilter = string.Empty;

    [ObservableProperty]
    private string _indexCollation = string.Empty;

    [ObservableProperty]
    private string _indexWildcardProjection = string.Empty;

    [ObservableProperty]
    private string _indexResults = string.Empty;

    partial void OnIndexResultsChanged(string value) { }

    public bool CanDropIndex => CanExecuteQuery && !string.IsNullOrWhiteSpace(IndexNameToDrop);

    public bool CanSetIndexVisibility => CanExecuteQuery
        && !string.IsNullOrWhiteSpace(IndexNameForVisibility)
        && string.Equals(IndexNameForVisibility.Trim(), IndexVisibilityConfirmation.Trim(), StringComparison.Ordinal);

    [RelayCommand(CanExecute = nameof(CanExecuteQuery))]
    private async Task LoadIndexesAsync()
    {
        if (!TryGetCollectionContext(out var profile, out var database, out var collection))
        {
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            var indexes = await _workspace.GetIndexesAsync(profile, database, collection, cancellationToken);
            if (IsCurrentIndexTarget(profile, database, collection))
            {
                IndexResults = indexes.Count == 0 ? T("noIndexes") : string.Join(Environment.NewLine + Environment.NewLine, indexes);
                StatusMessage = F("indexesLoaded", indexes.Count);
            }
        });
    }

    [RelayCommand(CanExecute = nameof(CanExecuteQuery))]
    private async Task LoadIndexDiagnosticsAsync()
    {
        if (!TryGetCollectionContext(out var profile, out var database, out var collection))
        {
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            IReadOnlyList<string>? indexes = null;
            IReadOnlyList<string>? usage = null;
            string? collectionStats = null;
            string? indexError = null;
            string? usageError = null;
            string? sizeError = null;
            string? currentOperations = null;
            string? buildError = null;
            DateTimeOffset? indexCapturedAt = null;
            DateTimeOffset? usageCapturedAt = null;
            DateTimeOffset? sizeCapturedAt = null;
            DateTimeOffset? buildCapturedAt = null;

            try
            {
                indexes = await _workspace.GetIndexesAsync(profile, database, collection, cancellationToken);
                indexCapturedAt = DateTimeOffset.UtcNow;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                indexError = DesktopOperationErrorMessages.Describe(exception);
            }

            try
            {
                usage = await _workspace.GetIndexUsageStatsAsync(profile, database, collection, cancellationToken);
                usageCapturedAt = DateTimeOffset.UtcNow;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                usageError = DesktopOperationErrorMessages.Describe(exception);
            }

            try
            {
                collectionStats = await _workspace.GetCollectionStatsAsync(profile, database, collection, cancellationToken);
                sizeCapturedAt = DateTimeOffset.UtcNow;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                sizeError = DesktopOperationErrorMessages.Describe(exception);
            }

            try
            {
                currentOperations = await _workspace.GetCurrentOperationsAsync(profile, cancellationToken);
                buildCapturedAt = DateTimeOffset.UtcNow;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                buildError = DesktopOperationErrorMessages.Describe(exception);
            }

            if (IsCurrentIndexTarget(profile, database, collection))
            {
                IndexResults = FormatIndexDiagnostics(database, collection, indexes, indexCapturedAt, indexError,
                    usage, usageCapturedAt, usageError, collectionStats, sizeCapturedAt, sizeError,
                    currentOperations, buildCapturedAt, buildError);
                var available = (indexes is null ? 0 : 1) + (usage is null ? 0 : 1) + (collectionStats is null ? 0 : 1) + (currentOperations is null ? 0 : 1);
                StatusMessage = F("indexDiagnosticsLoaded", database, collection, available, 4);
            }
        });
    }

    private string FormatIndexDiagnostics(
        string database,
        string collection,
        IReadOnlyList<string>? indexes,
        DateTimeOffset? indexCapturedAt,
        string? indexError,
        IReadOnlyList<string>? usage,
        DateTimeOffset? usageCapturedAt,
        string? usageError,
        string? collectionStats,
        DateTimeOffset? sizeCapturedAt,
        string? sizeError,
        string? currentOperations,
        DateTimeOffset? buildCapturedAt,
        string? buildError)
    {
        var lines = new List<string>
        {
            F("indexDiagnosticsTarget", database, collection),
            string.Empty,
            T("indexDiagnosticsUsageHeader"),
            F("indexDiagnosticsSourceTime", "$indexStats", FormatTimestamp(usageCapturedAt))
        };
        if (usageError is not null) lines.Add(F("indexDiagnosticsUnavailable", usageError));
        else if (usage is null || usage.Count == 0) lines.Add(T("noIndexStats"));
        else lines.AddRange(usage.Select(FormatIndexUsageRow));

        lines.Add(string.Empty);
        lines.Add(T("indexDiagnosticsSizeHeader"));
        lines.Add(F("indexDiagnosticsSourceTime", "collStats.indexSizes (bytes)", FormatTimestamp(sizeCapturedAt)));
        if (sizeError is not null) lines.Add(F("indexDiagnosticsUnavailable", sizeError));
        else if (collectionStats is null) lines.Add(T("indexDiagnosticsNoSize"));
        else lines.AddRange(FormatIndexSizes(collectionStats));

        lines.Add(string.Empty);
        lines.Add(T("indexDiagnosticsBuildHeader"));
        lines.Add(F("indexDiagnosticsSourceTime", "currentOp", FormatTimestamp(buildCapturedAt)));
        if (buildError is not null) lines.Add(F("indexDiagnosticsUnavailable", buildError));
        else if (currentOperations is null) lines.Add(T("indexDiagnosticsNoBuildData"));
        else
        {
            try
            {
                var builds = IndexBuildProgressParser.Parse(currentOperations, database, collection);
                lines.Add(builds.Count == 0
                    ? T("indexDiagnosticsNoBuildObserved")
                    : string.Join(Environment.NewLine, builds.Select(FormatIndexBuildProgress)));
            }
            catch (JsonException)
            {
                lines.Add(T("indexDiagnosticsInvalidBuildData"));
            }
        }

        lines.Add(string.Empty);
        lines.Add(T("indexDiagnosticsRedundancyHeader"));
        lines.Add(F("indexDiagnosticsSourceTime", T("indexesOnServer"), FormatTimestamp(indexCapturedAt)));
        if (indexError is not null) lines.Add(F("indexDiagnosticsUnavailable", indexError));
        else if (indexes is null) lines.Add(T("indexDiagnosticsNoDefinitions"));
        else
        {
            var findings = IndexRedundancyAnalyzer.Analyze(indexes);
            lines.Add(findings.Count == 0
                ? T("indexDiagnosticsNoCandidates")
                : string.Join(Environment.NewLine, findings.Select(finding => F("indexDiagnosticsCandidate", finding.CandidateIndex, finding.CoveringIndex, T("indexDiagnosticsCandidateEvidence")))));
        }

        lines.Add(string.Empty);
        lines.Add(T("indexDiagnosticsLimits"));
        return string.Join(Environment.NewLine, lines);
    }

    private string FormatIndexBuildProgress(IndexBuildProgress build)
    {
        var name = build.IndexNames.Count == 0 ? T("unknown") : string.Join(", ", build.IndexNames);
        var progress = build.Done is not null && build.Total is not null
            ? F("indexDiagnosticsBuildProgress", build.Done, build.Total)
            : T("indexDiagnosticsBuildProgressUnknown");
        var quorum = build.CommitQuorumWasSpecified
            ? F("indexDiagnosticsBuildQuorum", build.ObservedCommitQuorum ?? T("unknown"))
            : T("indexDiagnosticsBuildQuorumUnobserved");
        var phase = string.IsNullOrWhiteSpace(build.Message) ? T("unknown") : build.Message;
        return F("indexDiagnosticsBuildRow", name, phase, progress, quorum);
    }

    private string FormatIndexUsageRow(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return T("indexDiagnosticsMalformedRow");
            var name = ReadString(root, "name") ?? T("unknown");
            var host = ReadString(root, "host") ?? T("unknown");
            var accesses = root.TryGetProperty("accesses", out var accessesValue) ? accessesValue : default;
            var operations = accesses.ValueKind == JsonValueKind.Object && accesses.TryGetProperty("ops", out var ops) ? FormatExtendedScalar(ops) : T("unknown");
            var since = accesses.ValueKind == JsonValueKind.Object && accesses.TryGetProperty("since", out var sinceValue) ? FormatExtendedScalar(sinceValue) : T("unknown");
            return F("indexDiagnosticsUsageRow", name, operations, since, host);
        }
        catch (JsonException)
        {
            return T("indexDiagnosticsMalformedRow");
        }
    }

    private static string[] FormatIndexSizes(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("indexSizes", out var sizes) || sizes.ValueKind != JsonValueKind.Object)
                return [T("indexDiagnosticsNoSize")];
            var rows = sizes.EnumerateObject()
                .Select(property => F("indexDiagnosticsSizeRow", property.Name, FormatExtendedScalar(property.Value)))
                .ToArray();
            return rows.Length == 0 ? [T("indexDiagnosticsNoSize")] : rows;
        }
        catch (JsonException)
        {
            return [T("indexDiagnosticsInvalidStats")];
        }
    }

    private static string? ReadString(JsonElement root, string property) =>
        root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string FormatExtendedScalar(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var wrapper in new[] { "$numberLong", "$numberInt", "$numberDouble", "$numberDecimal" })
            {
                if (value.TryGetProperty(wrapper, out var wrapped)) return wrapped.ToString();
            }
            if (value.TryGetProperty("$date", out var date))
            {
                if (date.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(date.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
                    return parsed.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
                if (date.ValueKind == JsonValueKind.Object && date.TryGetProperty("$numberLong", out var milliseconds)
                    && long.TryParse(milliseconds.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var unixMilliseconds))
                {
                    try { return DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds).ToString("O", CultureInfo.InvariantCulture); }
                    catch (ArgumentOutOfRangeException) { }
                }
            }
        }
        return value.ToString();
    }

    private static string FormatTimestamp(DateTimeOffset? timestamp) =>
        timestamp?.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture) ?? "—";

    [RelayCommand(CanExecute = nameof(CanExecuteQuery))]
    private async Task CreateIndexAsync()
    {
        if (!TryGetCollectionContext(out var profile, out var database, out var collection))
        {
            return;
        }

        var request = new IndexCreateRequest(
            database,
            collection,
            IndexKeys,
            IndexName,
            IndexIsUnique,
            IndexIsSparse,
            IndexExpireAfterSeconds is null ? null : decimal.ToInt32(IndexExpireAfterSeconds.Value),
            string.IsNullOrWhiteSpace(IndexPartialFilter) ? null : IndexPartialFilter,
            string.IsNullOrWhiteSpace(IndexCollation) ? null : IndexCollation,
            IndexIsHidden,
            string.IsNullOrWhiteSpace(IndexWildcardProjection) ? null : IndexWildcardProjection);
        request.Validate();

        await RunAsync(async cancellationToken =>
        {
            var before = await _workspace.GetIndexesAsync(profile, database, collection, cancellationToken);
            var name = await _workspace.CreateIndexAsync(profile, request, cancellationToken);
            IReadOnlyList<string>? after = null;
            string? refreshError = null;
            try { after = await _workspace.GetIndexesAsync(profile, database, collection, cancellationToken); }
            catch (Exception exception) { refreshError = DesktopOperationErrorMessages.Describe(exception); }
            var auditRecorded = await RecordIndexAuditAsync("index.create", profile, database, collection, name, before, after);
            if (IsCurrentIndexTarget(profile, database, collection))
            {
                if (after is null) IndexResults = T("indexRefreshFailed");
                else IndexResults = after.Count == 0 ? T("noIndexes") : string.Join(Environment.NewLine + Environment.NewLine, after);
                if (refreshError is not null) StatusMessage = auditRecorded
                    ? F("indexMutationRefreshFailed", name, refreshError)
                    : F("indexRefreshAndAuditFailed", name, refreshError);
                else if (!auditRecorded) StatusMessage = F("auditNotRecorded", T("indexAudit"));
                else StatusMessage = F("indexCreated", name);
            }
            else if (refreshError is not null) StatusMessage = auditRecorded
                ? F("indexMutationRefreshFailed", name, refreshError)
                : F("indexRefreshAndAuditFailed", name, refreshError);
            else if (!auditRecorded) StatusMessage = F("auditNotRecorded", T("indexAudit"));
        });
    }

    [RelayCommand(CanExecute = nameof(CanDropIndex))]
    private async Task DropIndexAsync()
    {
        if (!TryGetCollectionContext(out var profile, out var database, out var collection))
        {
            return;
        }

        var name = IndexNameToDrop.Trim();
        if (!await RunAsync(async cancellationToken =>
        {
            var before = await _workspace.GetIndexesAsync(profile, database, collection, cancellationToken);
            await _workspace.DropIndexAsync(profile, new IndexDropRequest(database, collection, name), cancellationToken);
            IReadOnlyList<string>? after = null;
            string? refreshError = null;
            try { after = await _workspace.GetIndexesAsync(profile, database, collection, cancellationToken); }
            catch (Exception exception) { refreshError = DesktopOperationErrorMessages.Describe(exception); }
            var auditRecorded = await RecordIndexAuditAsync("index.drop", profile, database, collection, name, before, after);
            if (IsCurrentIndexTarget(profile, database, collection))
            {
                if (after is null) IndexResults = T("indexRefreshFailed");
                else IndexResults = after.Count == 0 ? T("noIndexes") : string.Join(Environment.NewLine + Environment.NewLine, after);
                if (refreshError is not null) StatusMessage = auditRecorded
                    ? F("indexMutationRefreshFailed", name, refreshError)
                    : F("indexRefreshAndAuditFailed", name, refreshError);
                else if (!auditRecorded) StatusMessage = F("auditNotRecorded", T("indexAudit"));
                else StatusMessage = F("indexDropped", name);
            }
            else if (refreshError is not null) StatusMessage = auditRecorded
                ? F("indexMutationRefreshFailed", name, refreshError)
                : F("indexRefreshAndAuditFailed", name, refreshError);
            else if (!auditRecorded) StatusMessage = F("auditNotRecorded", T("indexAudit"));
        }))
        {
            return;
        }

        if (IsCurrentIndexTarget(profile, database, collection) && string.Equals(IndexNameToDrop.Trim(), name, StringComparison.Ordinal))
            IndexNameToDrop = string.Empty;
    }

    [RelayCommand(CanExecute = nameof(CanSetIndexVisibility))]
    private async Task SetIndexVisibilityAsync()
    {
        if (!TryGetCollectionContext(out var profile, out var database, out var collection))
        {
            return;
        }

        var request = new IndexVisibilityRequest(database, collection, IndexNameForVisibility, IndexVisibilityConfirmation, IndexVisibilityHidden);
        request.Validate();
        if (!await RunAsync(async cancellationToken =>
        {
            var before = await _workspace.GetIndexesAsync(profile, database, collection, cancellationToken);
            await _workspace.SetIndexVisibilityAsync(profile, request, cancellationToken);
            IReadOnlyList<string>? after = null;
            string? refreshError = null;
            try { after = await _workspace.GetIndexesAsync(profile, database, collection, cancellationToken); }
            catch (Exception exception) { refreshError = DesktopOperationErrorMessages.Describe(exception); }
            var auditRecorded = await RecordIndexAuditAsync("index.visibility", profile, database, collection, request.Name.Trim(), before, after);
            if (IsCurrentIndexTarget(profile, database, collection))
            {
                if (after is null) IndexResults = T("indexRefreshFailed");
                else IndexResults = after.Count == 0 ? T("noIndexes") : string.Join(Environment.NewLine + Environment.NewLine, after);
                if (refreshError is not null) StatusMessage = auditRecorded
                    ? F("indexMutationRefreshFailed", request.Name.Trim(), refreshError)
                    : F("indexRefreshAndAuditFailed", request.Name.Trim(), refreshError);
                else if (!auditRecorded) StatusMessage = F("auditNotRecorded", T("indexAudit"));
                else StatusMessage = F("indexVisibilityChanged", request.Name.Trim());
            }
            else if (refreshError is not null) StatusMessage = auditRecorded
                ? F("indexMutationRefreshFailed", request.Name.Trim(), refreshError)
                : F("indexRefreshAndAuditFailed", request.Name.Trim(), refreshError);
            else if (!auditRecorded) StatusMessage = F("auditNotRecorded", T("indexAudit"));
        }))
        {
            return;
        }

        if (IsCurrentIndexTarget(profile, database, collection)
            && string.Equals(IndexNameForVisibility.Trim(), request.Name.Trim(), StringComparison.Ordinal)
            && string.Equals(IndexVisibilityConfirmation.Trim(), request.ConfirmationName.Trim(), StringComparison.Ordinal))
        {
            IndexNameForVisibility = string.Empty;
            IndexVisibilityConfirmation = string.Empty;
        }
    }

    private bool IsCurrentIndexTarget(ConnectionProfile profile, string database, string collection) =>
        SelectedProfile == profile
        && string.Equals(SelectedDatabase, database, StringComparison.Ordinal)
        && string.Equals(SelectedCollection, collection, StringComparison.Ordinal);

    private async Task<bool> RecordIndexAuditAsync(
        string action,
        ConnectionProfile profile,
        string database,
        string collection,
        string indexName,
        IReadOnlyList<string> before,
        IReadOnlyList<string>? after)
    {
        try
        {
            // Only the name and non-sensitive options are persisted; never store key paths, filters, collation, or connection details.
            await _workspace.SaveAuditAsync(AuditEntry.Create(action, profile.Id, database, collection,
                F("indexAuditSummary", indexName, GetIndexAuditState(before, indexName),
                    after is null ? T("unknown") : GetIndexAuditState(after, indexName))));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string GetIndexAuditState(IEnumerable<string> indexes, string name)
    {
        foreach (var json in indexes)
        {
            try
            {
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.ValueKind == JsonValueKind.Object
                    && document.RootElement.TryGetProperty("name", out var indexName)
                    && string.Equals(indexName.GetString(), name, StringComparison.Ordinal))
                {
                    var root = document.RootElement;
                    var hidden = ReadAuditBoolean(root, "hidden");
                    var unique = ReadAuditBoolean(root, "unique");
                    var sparse = ReadAuditBoolean(root, "sparse");
                    var ttl = root.TryGetProperty("expireAfterSeconds", out var ttlValue) ? ttlValue.GetRawText() : "none";
                    return $"present=true;hidden={hidden};unique={unique};sparse={sparse};ttl={ttl}";
                }
            }
            catch (JsonException)
            {
                // A malformed server definition cannot be treated as proof that an index exists.
            }
        }

        return "present=false";
    }

    private static string ReadAuditBoolean(JsonElement document, string property) =>
        document.TryGetProperty(property, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean().ToString(CultureInfo.InvariantCulture).ToLowerInvariant()
            : "unknown";
}
