namespace EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

/// <summary>PackageManaged: a newer release exists, but a Linux package manager owns the installation.</summary>
public enum AppUpdateUiState { Hidden, Available, ManualOnly, Downloading, Ready, InstallFailed, PackageManaged }
