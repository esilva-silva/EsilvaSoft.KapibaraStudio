using System.Security.Cryptography;
using System.Text;
using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Core.Agents;

namespace EsilvaSoft.SlopStudio.UnitTests;

/// <summary>Attachment resolution against a real temporary folder: limits, exclusions, containment and redaction.</summary>
[TestFixture]
public sealed class AgentAttachmentResolverTests
{
    private const string Canary = "Canary7Secret";

    private string _root = null!;
    private string _workspace = null!;
    private string _outside = null!;

    private static AgentProviderPermissions Consented() =>
        AgentProviderPermissions.Default("claude-code") with { ExternalDestinationConsentAt = DateTimeOffset.UnixEpoch };

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "slop-attachments-" + Guid.NewGuid().ToString("N"));
        _workspace = Path.Combine(_root, "ws");
        _outside = Path.Combine(_root, "fora");
        Directory.CreateDirectory(Path.Combine(_workspace, "sub"));
        Directory.CreateDirectory(_outside);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
        {
            // Remove links/junctions first, without following them into their targets.
            foreach (var entry in new DirectoryInfo(_workspace).EnumerateFileSystemInfos("*", SearchOption.TopDirectoryOnly))
            {
                if (entry.LinkTarget is not null)
                {
                    entry.Delete();
                }
            }

            Directory.Delete(_root, recursive: true);
        }
    }

    private AgentWorkspaceContext Context(string? buffer = null, string? activePath = null) =>
        new(DateTimeOffset.UnixEpoch, _workspace, activePath, activePath is null ? null : Path.GetFileName(activePath),
            "tab-1", 1, buffer);

    private string Write(string relative, string content)
    {
        var path = Path.Combine(_workspace, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    private static Task<AgentAttachmentResolution> Resolve(
        AgentWorkspaceContext context, AgentProviderPermissions permissions, params AgentAttachmentRequest[] requests) =>
        AgentAttachmentResolver.ResolveAsync(requests, context, permissions, CancellationToken.None);

    [Test]
    public async Task WorkspaceFileIsReadWithRelativePathSizeAndHash()
    {
        Write(Path.Combine("sub", "q.js"), "db.orders.find({})");
        var resolution = await Resolve(Context(), Consented(),
            new AgentAttachmentRequest(AgentAttachmentKind.WorkspaceFile, Path.Combine("sub", "q.js")));
        var attachment = resolution.Attachments.Single();
        var expectedHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("db.orders.find({})")));
        Assert.Multiple(() =>
        {
            Assert.That(resolution.Succeeded, Is.True);
            Assert.That(attachment.PathOrName, Is.EqualTo("sub/q.js"));
            Assert.That(attachment.Content, Is.EqualTo("db.orders.find({})"));
            Assert.That(attachment.SizeBytes, Is.EqualTo(18));
            Assert.That(attachment.Sha256, Is.EqualTo(expectedHash));
            Assert.That(attachment.ToDescriptor(), Is.EqualTo(new AgentAttachmentDescriptor(
                AgentAttachmentKind.WorkspaceFile, "q.js", "sub/q.js", 18, expectedHash)));
            Assert.That(attachment.ToString(), Does.Not.Contain("orders"));
        });
    }

    [Test]
    public async Task PathsOutsideTheWorkspaceAreRefused()
    {
        var outsideFile = Path.Combine(_outside, "x.js");
        File.WriteAllText(outsideFile, "x");
        var resolution = await Resolve(Context(), Consented(),
            new AgentAttachmentRequest(AgentAttachmentKind.WorkspaceFile, Path.Combine("..", "fora", "x.js")),
            new AgentAttachmentRequest(AgentAttachmentKind.WorkspaceFile, outsideFile),
            new AgentAttachmentRequest(AgentAttachmentKind.WorkspaceFile, _workspace + "-irmao" + Path.DirectorySeparatorChar + "x.js"));
        Assert.Multiple(() =>
        {
            Assert.That(resolution.Attachments, Is.Empty);
            Assert.That(resolution.Failures.Select(static f => f.Error),
                Is.All.EqualTo(AgentAttachmentError.OutsideWorkspace));
            Assert.That(resolution.Failures.Select(static f => f.DisplayName), Has.None.Contains(_root));
        });
    }

    [TestCase(".env")]
    [TestCase("sub/.env")]
    [TestCase("certs/server.pem")]
    [TestCase("deploy/id.KEY")]
    [TestCase("app/secrets/db.json")]
    public async Task ExcludedFilesAreRefused(string relative)
    {
        Write(relative, "x");
        var resolution = await Resolve(Context(), Consented(),
            new AgentAttachmentRequest(AgentAttachmentKind.WorkspaceFile, relative));
        Assert.That(resolution.Failures.Single().Error, Is.EqualTo(AgentAttachmentError.Excluded));
    }

    [Test]
    public async Task ExclusionsAlsoApplyToTheActiveBuffer()
    {
        var resolution = await Resolve(Context("SECRET=1", Path.Combine(_workspace, ".env")), Consented(),
            new AgentAttachmentRequest(AgentAttachmentKind.ActiveFile));
        Assert.That(resolution.Failures.Single().Error, Is.EqualTo(AgentAttachmentError.Excluded));
    }

    [Test]
    public async Task InvalidExclusionFailsClosed()
    {
        Write("a.js", "x");
        var permissions = Consented() with { Workspace = new AgentWorkspacePermissions { Exclusions = ["(x)"] } };
        var resolution = await Resolve(Context(), permissions, new AgentAttachmentRequest(AgentAttachmentKind.WorkspaceFile, "a.js"));
        Assert.That(resolution.Failures.Single().Error, Is.EqualTo(AgentAttachmentError.InvalidExclusion));
    }

    [Test]
    public async Task PerFileAndPerMessageLimitsAreEnforced()
    {
        Write("grande.txt", new string('a', AgentAttachmentResolver.MaximumFileBytes + 1));
        for (var i = 0; i < 5; i++)
        {
            Write($"p{i}.txt", new string('b', 250 * 1024));
        }

        var big = await Resolve(Context(), Consented(), new AgentAttachmentRequest(AgentAttachmentKind.WorkspaceFile, "grande.txt"));
        var many = await Resolve(Context(), Consented(),
            [.. Enumerable.Range(0, 5).Select(static i => new AgentAttachmentRequest(AgentAttachmentKind.WorkspaceFile, $"p{i}.txt"))]);
        var bigBuffer = await Resolve(Context(new string('c', AgentAttachmentResolver.MaximumFileBytes + 1), Path.Combine(_workspace, "novo.js")),
            Consented(), new AgentAttachmentRequest(AgentAttachmentKind.ActiveFile));
        Assert.Multiple(() =>
        {
            Assert.That(big.Failures.Single().Error, Is.EqualTo(AgentAttachmentError.FileTooLarge));
            Assert.That(many.Attachments, Has.Count.EqualTo(4));
            Assert.That(many.TotalBytes, Is.LessThanOrEqualTo(AgentAttachmentResolver.MaximumMessageBytes));
            Assert.That(many.Failures.Single(), Is.EqualTo(new AgentAttachmentFailure(4, AgentAttachmentKind.WorkspaceFile,
                "p4.txt", AgentAttachmentError.MessageTooLarge)));
            Assert.That(bigBuffer.Failures.Single().Error, Is.EqualTo(AgentAttachmentError.FileTooLarge));
        });
    }

    [Test]
    public async Task ExternalFilesNeedPermissionAndKeepOnlyTheirName()
    {
        var external = Path.Combine(_outside, "notas.md");
        File.WriteAllText(external, "texto");
        var request = new AgentAttachmentRequest(AgentAttachmentKind.ExternalFile, external);
        var denied = await Resolve(Context(), Consented(), request);
        var allowed = await Resolve(Context(),
            Consented() with { DataSending = new AgentDataSendingPermissions { ExternalAttachments = true } }, request);
        Assert.Multiple(() =>
        {
            Assert.That(denied.Failures.Single().Error, Is.EqualTo(AgentAttachmentError.NotPermitted));
            Assert.That(allowed.Attachments.Single().PathOrName, Is.EqualTo("notas.md"));
            Assert.That(allowed.Attachments.Single().Content, Is.EqualTo("texto"));
        });
    }

    [Test]
    public async Task NothingResolvesWithoutConsent()
    {
        Write("a.js", "x");
        var resolution = await Resolve(Context("buffer", "a.js"), AgentProviderPermissions.Default("claude-code"),
            new AgentAttachmentRequest(AgentAttachmentKind.ActiveFile),
            new AgentAttachmentRequest(AgentAttachmentKind.WorkspaceFile, "a.js"));
        Assert.Multiple(() =>
        {
            Assert.That(resolution.Attachments, Is.Empty);
            Assert.That(resolution.Failures.Select(static f => f.Error), Is.All.EqualTo(AgentAttachmentError.ConsentMissing));
        });
    }

    [Test]
    public async Task SecretsAreRedactedFromFilesAndBuffer()
    {
        Write("conn.js", "const uri = \"mongodb://svc:" + Canary + "@db.internal/shop\";\ndb.orders.find({})");
        var resolution = await Resolve(Context("{ \"password\": \"" + Canary + "\" }", Path.Combine(_workspace, "q.json")),
            Consented(),
            new AgentAttachmentRequest(AgentAttachmentKind.WorkspaceFile, "conn.js"),
            new AgentAttachmentRequest(AgentAttachmentKind.ActiveFile));
        Assert.Multiple(() =>
        {
            Assert.That(resolution.Attachments, Has.Count.EqualTo(2));
            Assert.That(resolution.Attachments.Select(static a => a.Content), Has.None.Contains(Canary));
            Assert.That(resolution.Attachments[0].Content, Does.Contain("db.orders.find({})"));
        });
    }

    [Test]
    public async Task ActiveFileComesFromTheUnsavedBuffer()
    {
        var path = Write("q.js", "salvo");
        var resolution = await Resolve(Context("não salvo", path), Consented(), new AgentAttachmentRequest(AgentAttachmentKind.ActiveFile));
        var attachment = resolution.Attachments.Single();
        Assert.Multiple(() =>
        {
            Assert.That(attachment.Content, Is.EqualTo("não salvo"));
            Assert.That(attachment.PathOrName, Is.EqualTo("q.js"));
        });
    }

    [Test]
    public async Task TypedFailuresForMissingInputs()
    {
        File.WriteAllBytes(Path.Combine(_workspace, "bin.dat"), [1, 0, 2, 0]);
        var noWorkspace = new AgentWorkspaceContext(DateTimeOffset.UnixEpoch);
        var resolution = await Resolve(Context(), Consented(),
            new AgentAttachmentRequest(AgentAttachmentKind.WorkspaceFile, "inexistente.js"),
            new AgentAttachmentRequest(AgentAttachmentKind.WorkspaceFile, "bin.dat"),
            new AgentAttachmentRequest(AgentAttachmentKind.ActiveFile),
            new AgentAttachmentRequest(AgentAttachmentKind.TabMetadata));
        var withoutFolder = await Resolve(noWorkspace, Consented(),
            new AgentAttachmentRequest(AgentAttachmentKind.WorkspaceFile, "a.js"));
        Assert.Multiple(() =>
        {
            Assert.That(resolution.Failures.Select(static f => f.Error), Is.EqualTo([AgentAttachmentError.NotFound, AgentAttachmentError.NotText, AgentAttachmentError.NoActiveFile,
                AgentAttachmentError.NoTabMetadata,]));
            Assert.That(withoutFolder.Failures.Single().Error, Is.EqualTo(AgentAttachmentError.NoWorkspace));
        });
    }

    [Test]
    public async Task TabMetadataCarriesNamesOnly()
    {
        var id = Guid.NewGuid();
        var context = Context() with
        {
            ConnectionId = id.ToString(), ConnectionName = "developercluster", DatabaseName = "CakeShop", CollectionName = "orders",
        };
        var attachment = (await Resolve(context, Consented(), new AgentAttachmentRequest(AgentAttachmentKind.TabMetadata)))
            .Attachments.Single();
        Assert.Multiple(() =>
        {
            Assert.That(attachment.DisplayName, Is.EqualTo("developercluster › CakeShop › orders"));
            Assert.That(attachment.Content, Does.Contain(id.ToString("D")).And.Contain("Banco: CakeShop"));
            Assert.That(attachment.PathOrName, Is.Null);
        });
    }

    [Test]
    public async Task MalformedPermissionsResolveNothingAndNullExclusionsMeanDefaults()
    {
        Write("a.js", "x");
        Write(".env", "x");
        var request = new AgentAttachmentRequest(AgentAttachmentKind.WorkspaceFile, "a.js");
        var nullSending = await Resolve(Context(), Consented() with { DataSending = null! }, request);
        var nullWorkspace = await Resolve(Context(), Consented() with { Workspace = null! }, request);
        var future = await Resolve(Context(), Consented() with { FormatVersion = 99 }, request);
        var nullExclusions = await Resolve(Context(), Consented() with { Workspace = new AgentWorkspacePermissions { Exclusions = null! } },
            new AgentAttachmentRequest(AgentAttachmentKind.WorkspaceFile, ".env"));
        Assert.Multiple(() =>
        {
            Assert.That(nullSending.Failures.Single().Error, Is.EqualTo(AgentAttachmentError.InvalidPermissions));
            Assert.That(nullWorkspace.Failures.Single().Error, Is.EqualTo(AgentAttachmentError.InvalidPermissions));
            Assert.That(future.Failures.Single().Error, Is.EqualTo(AgentAttachmentError.InvalidPermissions));
            Assert.That(nullExclusions.Failures.Single().Error, Is.EqualTo(AgentAttachmentError.Excluded));
        });
    }

    [TestCase("a.js::$DATA")]
    [TestCase("a.js:stream")]
    [TestCase(@"SECRET~1\db.json")]
    [TestCase("PROGRA~1.TXT")]
    public async Task AliasSegmentsAreRefused(string relative)
    {
        var resolution = await Resolve(Context(), Consented(), new AgentAttachmentRequest(AgentAttachmentKind.WorkspaceFile, relative));
        Assert.That(resolution.Failures.Single().Error, Is.EqualTo(AgentAttachmentError.UnsafePath));
    }

    [Test]
    public async Task TrailingDotCannotBypassAnExclusion()
    {
        Write(".env", "SECRET=1");
        var resolution = await Resolve(Context(), Consented(), new AgentAttachmentRequest(AgentAttachmentKind.WorkspaceFile, ".env."));
        Assert.That(resolution.Failures.Single().Error,
            Is.EqualTo(AgentAttachmentError.UnsafePath).Or.EqualTo(AgentAttachmentError.Excluded));
    }

    [Test]
    public async Task JunctionOrDirectoryLinkInsideTheWorkspaceIsRefused()
    {
        File.WriteAllText(Path.Combine(_outside, "segredo.txt"), "fora");
        var link = Path.Combine(_workspace, "atalho");
        if (!TryCreateDirectoryLink(link, _outside))
        {
            Assert.Ignore("A plataforma não permitiu criar junction/link simbólico de diretório neste ambiente.");
        }

        var resolution = await Resolve(Context(), Consented(),
            new AgentAttachmentRequest(AgentAttachmentKind.WorkspaceFile, Path.Combine("atalho", "segredo.txt")));
        Assert.That(resolution.Failures.Single().Error, Is.EqualTo(AgentAttachmentError.OutsideWorkspace));
    }

    [Test]
    public async Task FileSymlinkInsideTheWorkspaceIsRefused()
    {
        var target = Path.Combine(_outside, "segredo.txt");
        File.WriteAllText(target, "fora");
        try
        {
            File.CreateSymbolicLink(Path.Combine(_workspace, "link.txt"), target);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Assert.Ignore("Criar link simbólico de arquivo exige privilégio/modo desenvolvedor neste ambiente.");
        }

        var resolution = await Resolve(Context(), Consented(), new AgentAttachmentRequest(AgentAttachmentKind.WorkspaceFile, "link.txt"));
        Assert.That(resolution.Failures.Single().Error, Is.EqualTo(AgentAttachmentError.OutsideWorkspace));
    }

    [Test]
    public async Task Utf16WithBomIsDecoded()
    {
        File.WriteAllText(Path.Combine(_workspace, "u16.js"), "db.clientes.find({ nome: \"João\" })", new UnicodeEncoding(false, true));
        var attachment = (await Resolve(Context(), Consented(), new AgentAttachmentRequest(AgentAttachmentKind.WorkspaceFile, "u16.js")))
            .Attachments.Single();
        Assert.That(attachment.Content, Is.EqualTo("db.clientes.find({ nome: \"João\" })"));
    }

    [Test]
    public async Task RedactionThatGrowsTheTextIsCheckedAgainstTheLimit()
    {
        var content = string.Concat(Enumerable.Repeat("pwd=x;\n", 36_000));
        Assert.That(Encoding.UTF8.GetByteCount(content), Is.LessThan(AgentAttachmentResolver.MaximumFileBytes));
        Write("muitos-segredos.txt", content);
        var resolution = await Resolve(Context(), Consented(), new AgentAttachmentRequest(AgentAttachmentKind.WorkspaceFile, "muitos-segredos.txt"));
        Assert.That(resolution.Failures.Single().Error, Is.EqualTo(AgentAttachmentError.FileTooLarge));
    }

    [Test]
    public void CancellationIsObserved()
    {
        Write("a.js", "x");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.That(() => AgentAttachmentResolver.ResolveAsync(
                [new AgentAttachmentRequest(AgentAttachmentKind.WorkspaceFile, "a.js")], Context(), Consented(), cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public async Task UserMessageCountsAgainstTheMessageLimit()
    {
        Write("a.txt", new string('a', 200 * 1024));
        var message = new string('m', 900 * 1024);
        var resolution = await AgentAttachmentResolver.ResolveAsync(
            [new AgentAttachmentRequest(AgentAttachmentKind.WorkspaceFile, "a.txt")], Context(), Consented(), message,
            CancellationToken.None);
        Assert.That(resolution.Failures.Single().Error, Is.EqualTo(AgentAttachmentError.MessageTooLarge));
    }

    [Test]
    public async Task ActiveFileIsMatchedByItsRealPathNotByTheTabTitle()
    {
        Directory.CreateDirectory(Path.Combine(_outside, "secrets"));
        var secretPath = Path.Combine(_outside, "secrets", "cfg.js");
        var ordinaryPath = Path.Combine(_outside, "notas.js");
        var excluded = await Resolve(Context("x", secretPath) with { ActiveFileName = "ok.js" }, Consented(),
            new AgentAttachmentRequest(AgentAttachmentKind.ActiveFile));
        var allowed = await Resolve(Context("y", ordinaryPath) with { ActiveFileName = ".env" }, Consented(),
            new AgentAttachmentRequest(AgentAttachmentKind.ActiveFile));
        Assert.Multiple(() =>
        {
            Assert.That(excluded.Failures.Single().Error, Is.EqualTo(AgentAttachmentError.Excluded));
            Assert.That(allowed.Attachments.Single().PathOrName, Is.EqualTo("notas.js"));
        });
    }

    [Test]
    public async Task ExternalFileInsideASecretsFolderIsExcluded()
    {
        Directory.CreateDirectory(Path.Combine(_outside, "secrets"));
        var path = Path.Combine(_outside, "secrets", "chaves.txt");
        File.WriteAllText(path, "x");
        var resolution = await Resolve(Context(),
            Consented() with { DataSending = new AgentDataSendingPermissions { ExternalAttachments = true } },
            new AgentAttachmentRequest(AgentAttachmentKind.ExternalFile, path));
        Assert.Multiple(() =>
        {
            Assert.That(resolution.Failures.Single().Error, Is.EqualTo(AgentAttachmentError.Excluded));
            Assert.That(resolution.Failures.Single().DisplayName, Is.EqualTo("chaves.txt"));
        });
    }

    [Test]
    public async Task ActiveAndExternalFilesCannotBypassExclusionsThroughSymbolicLinks()
    {
        var secretPath = Write(".env", Canary);
        var aliasPath = Path.Combine(_workspace, "alias.js");
        try
        {
            File.CreateSymbolicLink(aliasPath, secretPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException or NotSupportedException)
        {
            Assert.Ignore($"Symbolic links are unavailable in this environment: {exception.GetType().Name}");
            return;
        }

        var active = await Resolve(Context(Canary, aliasPath), Consented(),
            new AgentAttachmentRequest(AgentAttachmentKind.ActiveFile));
        var external = await Resolve(Context(),
            Consented() with { DataSending = new AgentDataSendingPermissions { ExternalAttachments = true } },
            new AgentAttachmentRequest(AgentAttachmentKind.ExternalFile, aliasPath));

        Assert.Multiple(() =>
        {
            Assert.That(active.Attachments, Is.Empty);
            Assert.That(active.Failures.Single().Error, Is.EqualTo(AgentAttachmentError.OutsideWorkspace));
            Assert.That(external.Attachments, Is.Empty);
            Assert.That(external.Failures.Single().Error, Is.EqualTo(AgentAttachmentError.OutsideWorkspace));
        });
    }

    [Test]
    public async Task LoneSurrogateIsNotText()
    {
        var resolution = await Resolve(Context("abc\uD800def", Path.Combine(_workspace, "q.js")), Consented(),
            new AgentAttachmentRequest(AgentAttachmentKind.ActiveFile));
        Assert.That(resolution.Failures.Single().Error, Is.EqualTo(AgentAttachmentError.NotText));
    }

    [Test]
    public void ContainmentHandlesDriveRootsAndSiblings()
    {
        var driveRoot = Path.GetPathRoot(_root)!;
        Assert.Multiple(() =>
        {
            Assert.That(AgentWorkspacePaths.IsStrictlyInside(Path.Combine(driveRoot, "x.js"), driveRoot), Is.True);
            Assert.That(AgentWorkspacePaths.IsStrictlyInside(driveRoot, driveRoot), Is.False);
            Assert.That(AgentWorkspacePaths.IsStrictlyInside(_workspace + "-irmao" + Path.DirectorySeparatorChar + "a", _workspace), Is.False);
            Assert.That(AgentWorkspacePaths.IsStrictlyInside(Path.Combine(_workspace, "a"), _workspace), Is.True);
        });
    }

    [Test]
    public void TryResolveInsideIsUsableByOtherTools()
    {
        Write(Path.Combine("sub", "q.js"), "x");
        var ok = AgentWorkspacePaths.TryResolveInside(_workspace, Path.Combine("sub", "q.js"), null, out var full, out var relative, out var error);
        var excluded = AgentWorkspacePaths.TryResolveInside(_workspace, ".env", null, out _, out _, out var excludedError);
        var outside = AgentWorkspacePaths.TryResolveInside(_workspace, Path.Combine("..", "fora", "x"), null, out _, out _, out var outsideError);
        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True);
            Assert.That(error, Is.EqualTo(AgentWorkspacePathError.None));
            Assert.That(relative, Is.EqualTo("sub/q.js"));
            Assert.That(full, Is.EqualTo(Path.Combine(_workspace, "sub", "q.js")));
            Assert.That(excluded, Is.False);
            Assert.That(excludedError, Is.EqualTo(AgentWorkspacePathError.Excluded));
            Assert.That(outside, Is.False);
            Assert.That(outsideError, Is.EqualTo(AgentWorkspacePathError.OutsideWorkspace));
        });
    }

    [Test]
    public void RequestToStringShowsOnlyTheFileName() =>
        Assert.That(new AgentAttachmentRequest(AgentAttachmentKind.ExternalFile, Path.Combine(_outside, "notas.md")).ToString(),
            Does.Contain("notas.md").And.Not.Contain(_outside));

    private static bool TryCreateDirectoryLink(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        // A junction needs no privilege on Windows.
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe")
        {
            ArgumentList = { "/c", "mklink", "/J", link, target },
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        });
        if (process is null)
        {
            return false;
        }

        process.WaitForExit(10_000);
        return process.HasExited && process.ExitCode == 0 && Directory.Exists(link);
    }

    [TestCase(".env", "a/.env", true)]
    [TestCase("*.pem", "x/y/server.PEM", true)]
    [TestCase("**/secrets/**", "secrets/a.txt", true)]
    [TestCase("**/secrets/**", "app/secrets/a/b.txt", true)]
    [TestCase("**/secrets/**", "app/mysecrets/a.txt", false)]
    [TestCase("docs/private", "docs/private/a.md", true)]
    [TestCase("docs/private", "x/docs/private/a.md", false)]
    [TestCase("*.key", "keys.txt", false)]
    public void ExclusionGlobSemantics(string pattern, string path, bool excluded) =>
        Assert.That(AgentWorkspaceExclusions.IsExcluded(path, [pattern]), Is.EqualTo(excluded));

    [Test]
    public void ExclusionsBecomeNativeReadRules() =>
        Assert.That(AgentWorkspaceExclusions.ToNativeReadRules([".env", "**/secrets/**", "/docs/private"]), Is.EqualTo(
        [
            "Read(**/.env)", "Read(**/.env/**)", "Read(**/secrets/**)", "Read(./docs/private)", "Read(./docs/private/**)",
        ]));
}
