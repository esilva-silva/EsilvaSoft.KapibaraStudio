namespace EsilvaSoft.KapibaraStudio.Application;

/// <summary>
/// Disabled outside a published executable; ManualOnly when the installation folder is not writable or its
/// package marker is invalid; PackageManaged when a Linux package manager owns the files, so the application only
/// checks releases and explains how to update through that manager; Supported when it may replace itself.
/// </summary>
public enum AppUpdateAvailability { Disabled, ManualOnly, Supported, PackageManaged }
