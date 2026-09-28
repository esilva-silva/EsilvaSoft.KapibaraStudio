using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

public sealed record ConnectionChoice(ConnectionProfile Profile)
{
    public string Name => (Profile.IsFavorite ? "★ " : "") + Profile.Name;
    public string Details => $"{Profile.Folder ?? LocalizationViewModel.Current.Resolve("noFolderShort")} · {Profile.Environment ?? LocalizationViewModel.Current.Resolve("noEnvironmentShort")}";
    public string Endpoint => Profile.Endpoint;
}
