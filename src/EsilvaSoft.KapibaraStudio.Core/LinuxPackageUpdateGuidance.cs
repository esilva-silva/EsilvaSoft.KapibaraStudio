namespace EsilvaSoft.KapibaraStudio.Core;

/// <summary>
/// Reads the <c>package-manager</c> marker installed next to the executable by the Linux packages and builds the
/// command that updates the installation through its own package manager. The application never replaces files
/// owned by a package manager.
/// </summary>
public static class LinuxPackageUpdateGuidance
{
    public const string MarkerFileName = "package-manager";

    /// <summary>
    /// Accepts exactly <c>apt</c>, <c>dnf</c> or <c>pacman</c> in lowercase, surrounded only by whitespace
    /// (a trailing newline is expected). Any other content, including different casing, is invalid.
    /// </summary>
    public static bool TryParseMarker(string? text, out LinuxPackageManager manager)
    {
        switch (text?.Trim())
        {
            case "apt": manager = LinuxPackageManager.Apt; return true;
            case "dnf": manager = LinuxPackageManager.Dnf; return true;
            case "pacman": manager = LinuxPackageManager.Pacman; return true;
            default: manager = default; return false;
        }
    }

    /// <summary>Name shown to the user; pacman installations come from the AUR.</summary>
    public static string GetDisplayName(LinuxPackageManager manager) => manager switch
    {
        LinuxPackageManager.Apt => "APT",
        LinuxPackageManager.Dnf => "DNF",
        LinuxPackageManager.Pacman => "pacman/AUR",
        _ => throw new ArgumentOutOfRangeException(nameof(manager))
    };

    /// <summary>
    /// Command for the package file published by the release of <paramref name="version"/>. File names keep the
    /// SemVer text (GitHub renames assets with <c>~</c>); the tilde that orders a pre-release below the final version
    /// only lives in the package's Version field. Until the project owns the AUR name, pacman installations rebuild
    /// the PKGBUILD published with the release instead of trusting a helper to resolve <c>kapibarastudio-bin</c>.
    /// </summary>
    public static string GetCommand(LinuxPackageManager manager, AppVersion version, string rid)
    {
        ArgumentNullException.ThrowIfNull(version);
        var (debArchitecture, rpmArchitecture) = rid switch
        {
            "linux-x64" => ("amd64", "x86_64"),
            "linux-arm64" => ("arm64", "aarch64"),
            _ => throw new ArgumentException($"RID sem pacote Linux: '{rid}'.", nameof(rid))
        };
        // Same names as New-KapibaraDistroPackages/New-KapibaraAurPackage (eng/LinuxDistroPackaging.ps1).
        // AppVersion.ToString already drops build metadata (+sha).
        var fileVersion = version.ToString();
        return manager switch
        {
            LinuxPackageManager.Apt => $"sudo apt install ./kapibarastudio_{fileVersion}_{debArchitecture}.deb",
            LinuxPackageManager.Dnf => $"sudo dnf upgrade ./kapibarastudio-{fileVersion}-1.{rpmArchitecture}.rpm",
            LinuxPackageManager.Pacman =>
                $"tar -xzf kapibarastudio-bin-{fileVersion}-aur.tar.gz && cd kapibarastudio-bin && makepkg -si",
            _ => throw new ArgumentOutOfRangeException(nameof(manager))
        };
    }
}
