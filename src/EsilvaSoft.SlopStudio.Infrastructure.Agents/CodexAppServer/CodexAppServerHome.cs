using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Runtime.Versioning;

namespace EsilvaSoft.SlopStudio.Infrastructure.Agents.CodexAppServer;

/// <summary>Fail-closed storage root for Codex-managed credentials; this application never opens auth.json.</summary>
internal static class CodexAppServerHome
{
    private const string LegacyConfig = "cli_auth_credentials_store = \"keyring\"\n";
    private const string Config = """
        cli_auth_credentials_store = "keyring"
        approval_policy = "on-request"
        sandbox_mode = "read-only"
        web_search = "disabled"

        [features]
        shell_tool = false
        unified_exec = false
        shell_snapshot = false
        multi_agent = false
        apps = false
        plugins = false
        hooks = false
        browser_use = false
        computer_use = false
        code_mode_host = false
        code_mode_only = false
        view_image = false

        [features.code_mode]
        enabled = false
        """ + "\n";
    private static readonly UTF8Encoding Utf8 = new(false);

    public static void Prepare(string path)
    {
        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException("Codex home must be absolute.", nameof(path));
        }

        var fullPath = Path.GetFullPath(path);
        RejectLinksOnExistingAncestors(fullPath);
        if (OperatingSystem.IsWindows())
        {
            PrepareWindows(fullPath);
        }
        else if (OperatingSystem.IsLinux())
        {
            Directory.CreateDirectory(fullPath, UnixFileMode.UserRead | UnixFileMode.UserWrite |
                UnixFileMode.UserExecute);
            if (File.GetUnixFileMode(fullPath) != (UnixFileMode.UserRead | UnixFileMode.UserWrite |
                UnixFileMode.UserExecute))
            {
                throw new UnauthorizedAccessException("Codex home permissions are not private.");
            }
        }
        else
        {
            throw new PlatformNotSupportedException("Codex home permissions cannot be verified here.");
        }

        RejectLinksOnExistingAncestors(fullPath);
        var configPath = Path.Combine(fullPath, "config.toml");
        if (File.Exists(configPath))
        {
            var existingConfig = File.ReadAllText(configPath, Utf8);
            if ((File.GetAttributes(configPath) & FileAttributes.ReparsePoint) != 0 ||
                !string.Equals(existingConfig, Config, StringComparison.Ordinal) &&
                !string.Equals(existingConfig, LegacyConfig, StringComparison.Ordinal))
            {
                throw new UnauthorizedAccessException("Codex home configuration differs from the managed policy.");
            }

            if (string.Equals(existingConfig, LegacyConfig, StringComparison.Ordinal))
            {
                var migrationPath = Path.Combine(fullPath, $"config.toml.{Guid.NewGuid():N}.tmp");
                try
                {
                    using (var stream = new FileStream(migrationPath, FileMode.CreateNew, FileAccess.Write,
                               FileShare.None, bufferSize: 4096, FileOptions.WriteThrough))
                    {
                        var bytes = Utf8.GetBytes(Config);
                        stream.Write(bytes);
                        stream.Flush(flushToDisk: true);
                    }

                    if (OperatingSystem.IsLinux())
                        File.SetUnixFileMode(migrationPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                    File.Move(migrationPath, configPath, overwrite: true);
                }
                finally
                {
                    if (File.Exists(migrationPath)) File.Delete(migrationPath);
                }
            }
        }
        else
        {
            using var stream = new FileStream(configPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 4096, FileOptions.WriteThrough);
            var bytes = Utf8.GetBytes(Config);
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }

        if (OperatingSystem.IsLinux())
        {
            File.SetUnixFileMode(configPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private static void RejectLinksOnExistingAncestors(string path)
    {
        for (var current = new DirectoryInfo(path); current is not null; current = current.Parent)
        {
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new UnauthorizedAccessException("Codex home uses a linked directory.");
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static void PrepareWindows(string path)
    {
        var sid = WindowsIdentity.GetCurrent().User ??
            throw new UnauthorizedAccessException("Current Windows identity is unavailable.");
        if (!Directory.Exists(path))
        {
            var security = new DirectorySecurity();
            security.SetOwner(sid);
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None,
                AccessControlType.Allow));
            var directory = Directory.CreateDirectory(path);
            directory.SetAccessControl(security);
        }

        var currentSecurity = new DirectoryInfo(path).GetAccessControl(AccessControlSections.Owner |
            AccessControlSections.Access);
        if (!sid.Equals(currentSecurity.GetOwner(typeof(SecurityIdentifier))) ||
            !currentSecurity.AreAccessRulesProtected)
        {
            throw new UnauthorizedAccessException("Codex home ACL is not private.");
        }

        var hasOwnerGrant = false;
        foreach (FileSystemAccessRule rule in currentSecurity.GetAccessRules(includeExplicit: true,
                     includeInherited: true, typeof(SecurityIdentifier)))
        {
            if (!sid.Equals(rule.IdentityReference) && rule.AccessControlType == AccessControlType.Allow)
            {
                throw new UnauthorizedAccessException("Codex home ACL grants access to another identity.");
            }

            hasOwnerGrant |= sid.Equals(rule.IdentityReference) && rule.AccessControlType == AccessControlType.Allow &&
                (rule.FileSystemRights & FileSystemRights.FullControl) == FileSystemRights.FullControl;
        }

        if (!hasOwnerGrant)
        {
            throw new UnauthorizedAccessException("Codex home ACL lacks owner access.");
        }
    }
}
