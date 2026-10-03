using System.Globalization;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Desktop.Agents;

namespace EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

public sealed partial class AgentChatViewModel
{
    private long _contextMeasurementGeneration;
    private AgentContextMeasurement? _contextMeasurement;
    public Task ContextMeasurementCompletion { get; private set; } = Task.CompletedTask;
    public AgentContextMeasurement? ContextMeasurement => _contextMeasurement;
    public string ContextMetricsSummary => _contextMeasurement is { } measured
        ? Text.Format("agentContextSizeSummary", Chips.Count, measured.MeasuredBytes / 1024d) +
            (measured.IsComplete ? "" : " · " + Text.Resolve("agentMetricPending"))
        : Text.Resolve("agentMetricPending");
    public string ContextMetricsDetails => AgentMetricText.Context(_contextMeasurement, sent: false);
    public string UsageDetails => AgentMetricText.Usage(ActiveConversation,
        Providers.FirstOrDefault(provider => string.Equals(provider.ProviderId, ActiveConversation.ProviderId, StringComparison.Ordinal))
            ?.Presentation.DisplayName ?? ActiveConversation.ProviderId);

    private void ScheduleContextMeasurement()
    {
        if (_disposed) return;
        var generation = ++_contextMeasurementGeneration;
        _contextMeasurement = null;
        NotifyMetricProperties();
        // Capture on the owning UI thread before asynchronous work. No file contents are read for a preview.
        try
        {
            var context = _host.CaptureWorkspace();
            var permissions = CurrentPermissions;
            var message = ComposerText.Trim();
            var conversationId = ActiveConversation.Id;
            var mode = SelectedMode.Mode;
            var external = IsExternalDestination;
            var supportsPlan = SupportsTurnPlan;
            var facts = CapturePlatformFacts(permissions?.Workspace.UseFilesFolder == true ? context.WorkspaceFolder : null);
            var chipRequests = Chips.Select(static chip => chip.ToRequest()).ToArray();
            var pending = Chips.Count(static chip => !chip.IsAutomatic && chip.Error is null);
            var refused = Chips.Count(static chip => chip.Error is not null);
            var memoryRequests = chipRequests.Where(static request => request.Kind is AgentAttachmentKind.ActiveFile or AgentAttachmentKind.TabMetadata).ToArray();
            ContextMeasurementCompletion = MeasurePreviewAsync();

            async Task MeasurePreviewAsync()
            {
                try
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(250), _services.Clock, _lifetime.Token).ConfigureAwait(false);
                    if (generation != Volatile.Read(ref _contextMeasurementGeneration)) return;
                    AgentContextMeasurement measured;
                    if (supportsPlan && permissions is { IsWellFormed: true })
                    {
                        if (!permissions.Workspace.UseFilesFolder) context = context with { WorkspaceFolder = null };
                        var resolution = await AgentAttachmentResolver.ResolveAsync(memoryRequests, context, permissions, message,
                            _lifetime.Token, pathProbe: _services.PathProbe).ConfigureAwait(false);
                        var plan = AgentModePolicy.Plan(mode, permissions, facts,
                            requireExternalDestinationConsent: external);
                        var prompt = AgentSystemPromptBuilder.Build(new AgentSystemPromptContext(plan, context.WorkspaceFolder,
                            permissions.DataSending.ActiveFile ? context.ActiveFileName : null));
                        measured = AgentContextMeasurement.Capture(new AgentTurnRequest(AgentTurnId.New(), message, context.TabId ?? "", context.DocumentVersion ?? 0)
                            { SystemPrompt = prompt, Attachments = resolution.Attachments }) with
                            { PendingItems = pending, FailedItems = refused + resolution.Failures.Count };
                    }
                    else measured = AgentContextMeasurement.Capture(new AgentTurnRequest(AgentTurnId.New(), message, "", 0));
                    await AgentUiDispatch.RunOnUiAsync(() =>
                    {
                        if (_disposed || generation != _contextMeasurementGeneration || conversationId != ActiveConversation.Id)
                            return Task.FromResult(false);
                        _contextMeasurement = measured;
                        NotifyMetricProperties();
                        return Task.FromResult(true);
                    }).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
                catch (Exception)
                {
                    AgentUiDispatch.Post(() =>
                    {
                        if (_disposed || generation != _contextMeasurementGeneration) return;
                        _contextMeasurement = new AgentContextMeasurement(0, 0, 0, [], FailedItems: 1);
                        NotifyMetricProperties();
                    });
                }
            }
        }
        catch (Exception)
        {
            _contextMeasurement = new AgentContextMeasurement(0, 0, 0, [], FailedItems: 1);
            NotifyMetricProperties();
        }
    }

    private void NotifyMetricProperties()
    {
        OnPropertyChanged(nameof(ContextMeasurement));
        OnPropertyChanged(nameof(ContextMetricsSummary));
        OnPropertyChanged(nameof(ContextMetricsDetails));
        OnPropertyChanged(nameof(UsageDetails));
    }
}

internal static class AgentMetricText
{
    private static LocalizationViewModel Text => LocalizationViewModel.Current;
    public static string Context(AgentContextMeasurement? value, bool sent)
    {
        if (value is null) return Text.Resolve("agentMetricPending");
        var lines = new List<string> { Text.Resolve(sent ? "agentContextSentSnapshot" : "agentContextLocalPreview"),
            Text.Format("agentMetricMeasuredBytes", value.MeasuredBytes),
            Text.Format("agentMetricBytes", Text.Resolve("agentMetricMessage"), value.MessageBytes),
            Text.Format("agentMetricBytes", Text.Resolve("agentMetricInstructions"), value.InstructionsBytes) };
        if (value.AuthorizedContextBytes != 0) lines.Add(Text.Format("agentMetricBytes", Text.Resolve("agentMetricAuthorizedContext"), value.AuthorizedContextBytes));
        foreach (var group in value.Items.GroupBy(static item => item.Kind))
            lines.Add(Text.Format("agentMetricBytes", Text.Resolve(group.Key switch
            { AgentAttachmentKind.ActiveFile => "agentChipActive", AgentAttachmentKind.TabMetadata => "agentMetricMetadata", _ => "agentMetricAttachments" }), group.Sum(static item => item.Bytes)));
        if (value.PendingItems > 0) lines.Add(Text.Format("agentMetricPendingCount", value.PendingItems));
        if (value.FailedItems > 0) lines.Add(Text.Format("agentMetricFailedCount", value.FailedItems));
        lines.Add(Text.Resolve("agentMetricContextLimits"));
        return string.Join('\n', lines);
    }

    public static string Usage(AgentChatConversation conversation, string providerName)
    {
        var lines = new List<string> { Text.Format("agentUsageProvider", providerName), Text.Resolve("agentUsageObservedExecution") };
        var latest = conversation.UsageTurns.LastOrDefault();
        if (latest.Value?.Total is { } total)
        {
            lines.Add(Text.Format("agentUsageTurn", latest.Key.Value));
            lines.Add(Text.Format("agentUsageModel", total.Model ?? Text.Resolve("agentMetricUnavailable")));
            lines.Add(Text.Format("agentUsageInputOutput", Number(total.InputTokens), Number(total.OutputTokens)));
            lines.Add(Text.Format("agentUsageCache", Number(total.CacheReadTokens), Number(total.CacheWriteTokens)));
            if (total.Cost is { } cost) lines.Add(Text.Format("agentUsageCost", cost, total.Currency));
            lines.Add(Text.Format("agentUsageSource", total.Source, latest.Value.UpdatedAtUtc));
            lines.Add(Text.Resolve(latest.Value.IsPartial ? "agentUsagePartial" : "agentUsageTurnComplete"));
        }
        else lines.Add(Text.Resolve("agentMetricUnavailable"));
        try
        {
            lines.Add(Text.Format("agentUsageAccumulated", Number(AgentUsageAccumulator.SumKnown(conversation.UsageTurns.Values.Select(static value => value.Total?.InputTokens))),
                Number(AgentUsageAccumulator.SumKnown(conversation.UsageTurns.Values.Select(static value => value.Total?.OutputTokens)))));
        }
        catch (OverflowException) { lines.Add(Text.Resolve("agentUsageOverflow")); }
        lines.Add(Text.Resolve("agentUsageWindowQuotaUnavailable"));
        return string.Join('\n', lines);
    }
    private static string Number(long? value) => value?.ToString("N0", CultureInfo.CurrentCulture) ?? Text.Resolve("agentMetricUnavailable");
}
