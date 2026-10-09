using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using EsilvaSoft.KapibaraStudio.Application;

namespace EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    [RelayCommand]
    private void LoadMetricHistory()
    {
        var profile = SelectedProfile;
        if (profile is null)
            return;

        var samples = _workspace.MetricHistory.GetSnapshot(profile.Id, profile.SourceGenerationId);
        if (samples.Count == 0)
        {
            AdministrationResults = T("metricHistoryEmpty");
            return;
        }

        AdministrationResults = T("metricHistoryIntroduction") + Environment.NewLine + Environment.NewLine
            + string.Join(Environment.NewLine, samples.Select(FormatMetricSample));
    }

    private static string FormatMetricSample(AdministrationMetricSample sample)
    {
        var source = sample.Source switch
        {
            AdministrationMetricSource.ServerStatus => "serverStatus",
            AdministrationMetricSource.Topology => "topology",
            AdministrationMetricSource.DatabaseStats => "databaseStats",
            AdministrationMetricSource.CollectionStats => "collectionStats",
            AdministrationMetricSource.ExactCount => "metricExactCount",
            AdministrationMetricSource.FilteredExactCount => "metricFilteredExactCount",
            AdministrationMetricSource.EstimatedCount => "metricEstimatedCount",
            _ => "metricHistory"
        };
        var namespaceLabel = sample.Database is null ? string.Empty
            : " — " + sample.Database + (sample.Collection is null ? string.Empty : "." + sample.Collection);
        var values = sample.Metrics.Count == 0
            ? T("metricNoNumericValues")
            : string.Join(", ", sample.Metrics.Select(metric => metric.Name + "="
                + metric.Value.ToString("G", CultureInfo.InvariantCulture)));
        return sample.ObservedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.CurrentCulture)
            + " | " + T(source) + namespaceLabel + " | " + values;
    }
}
