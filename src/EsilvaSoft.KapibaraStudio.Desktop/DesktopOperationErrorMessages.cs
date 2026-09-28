using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

namespace EsilvaSoft.KapibaraStudio.Desktop;

internal static class DesktopOperationErrorMessages
{
    public static string Describe(Exception exception, bool export = false) =>
        OperationErrorMessages.Describe(exception, export, LocalizationViewModel.Current.Resolve);
}
