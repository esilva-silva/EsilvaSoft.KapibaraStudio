using EsilvaSoft.KapibaraStudio.SystemAdapters.Copilot;
using System.Globalization;
using EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Copilot;
using GitHub.Copilot;
using GitHub.Copilot.Rpc;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Tests.Copilot;

#pragma warning disable GHCP001 // Exercita diretamente a API experimental SessionFs do SDK pinado.
[TestFixture]
[Category("Integration")]
internal sealed class CopilotVolatileSessionFsTests
{
    private static readonly string[] RootEntries = ["workspace"];
    private static readonly string[] WorkspaceEntries = ["notes.txt"];
    private static readonly string[] WindowsSubEntries = ["Sub"];
    private static readonly string[] ValueColumn = ["value"];
    private static CopilotVolatileSessionFsStore.SessionState NewState() => new(StringComparer.Ordinal);

    [Test]
    public async Task FileSystemSupportsNestedCrudAndTypedDirectoryEntries()
    {
        await using var state = NewState();

        await state.WriteFileAsync("/workspace/notes.txt", "hello", CancellationToken.None);
        await state.AppendFileAsync("workspace/notes.txt", " world", CancellationToken.None);

        Assert.That(await state.ReadFileAsync("workspace\\notes.txt", CancellationToken.None), Is.EqualTo("hello world"));
        Assert.That((await state.StatAsync("/workspace/notes.txt", CancellationToken.None)).Size, Is.EqualTo(11));
        Assert.That(await state.ReadDirectoryAsync("/", CancellationToken.None), Is.EqualTo(RootEntries));
        Assert.That(await state.ReadDirectoryAsync("/workspace", CancellationToken.None), Is.EqualTo(WorkspaceEntries));

        var typed = await state.ReadDirectoryWithTypesAsync("/", CancellationToken.None);
        Assert.That(typed, Has.Count.EqualTo(1));
        Assert.That(typed[0].Name, Is.EqualTo("workspace"));
        Assert.That(typed[0].Type, Is.EqualTo(SessionFsReaddirWithTypesEntryType.Directory));
    }

    [Test]
    public async Task ConcurrentAppendsDoNotLoseSessionFileUpdates()
    {
        await using var state = NewState();
        const int writers = 64;

        await Task.WhenAll(Enumerable.Range(0, writers).Select(_ =>
            state.AppendFileAsync("/shared/transcript.txt", "x", CancellationToken.None)));

        var transcript = await state.ReadFileAsync("/shared/transcript.txt", CancellationToken.None);
        Assert.That(transcript, Has.Length.EqualTo(writers));
        Assert.That(transcript.Count(static character => character == 'x'), Is.EqualTo(writers));
    }

    [TestCase("../outside.txt")]
    [TestCase("/safe/../../outside.txt")]
    [TestCase("safe/./file.txt")]
    [TestCase("safe/..\\outside.txt")]
    public async Task FileSystemRejectsTraversalAndDotSegments(string path)
    {
        await using var state = NewState();

        Assert.Throws<IOException>(() => state.WriteFileAsync(path, "blocked", CancellationToken.None));
        Assert.Throws<IOException>(() => state.ExistsAsync(path, CancellationToken.None));
    }

    [Test]
    public async Task FileSystemRenamesFilesAndDirectoriesAndRemovesRecursively()
    {
        await using var state = NewState();
        await state.WriteFileAsync("/a/one.txt", "one", CancellationToken.None);
        await state.WriteFileAsync("/a/sub/two.txt", "two", CancellationToken.None);

        await state.RenameAsync("/a/one.txt", "/a/renamed.txt", CancellationToken.None);
        await state.RenameAsync("/a/sub", "/a/child", CancellationToken.None);
        Assert.That(await state.ReadFileAsync("/a/renamed.txt", CancellationToken.None), Is.EqualTo("one"));
        Assert.That(await state.ReadFileAsync("/a/child/two.txt", CancellationToken.None), Is.EqualTo("two"));
        Assert.ThrowsAsync<IOException>(() => state.RemoveAsync("/a", recursive: false, force: false, CancellationToken.None));

        await state.RemoveAsync("/a", recursive: true, force: false, CancellationToken.None);
        Assert.That(await state.ExistsAsync("/a", CancellationToken.None), Is.False);
    }

    [Test]
    public async Task WindowsPathSemanticsHandleCasingForDeleteAndRename()
    {
        await using var state = new CopilotVolatileSessionFsStore.SessionState(StringComparer.OrdinalIgnoreCase);
        await state.WriteFileAsync("/Workspace/Sub/note.txt", "x", CancellationToken.None);
        Assert.That(await state.ReadDirectoryAsync("/workspace", CancellationToken.None), Is.EqualTo(WindowsSubEntries));
        await state.RenameAsync("/workspace", "/Moved", CancellationToken.None);
        Assert.That(await state.ReadFileAsync("/moved/sub/note.txt", CancellationToken.None), Is.EqualTo("x"));
        await state.RemoveAsync("/MOVED", recursive: true, force: false, CancellationToken.None);
        Assert.That(await state.ExistsAsync("/moved/sub/note.txt", CancellationToken.None), Is.False);
    }

    [Test]
    public async Task FileSystemRejectsFileAsAncestorForWritesAndRecursiveDirectoryCreation()
    {
        await using var state = NewState();
        await state.WriteFileAsync("/a", "file", CancellationToken.None);
        Assert.Throws<IOException>(() => state.WriteFileAsync("/a/b", "blocked", CancellationToken.None));
        Assert.Throws<IOException>(() => state.MakeDirectoryAsync("/a/b", recursive: true, CancellationToken.None));
        Assert.That(await state.ReadFileAsync("/a", CancellationToken.None), Is.EqualTo("file"));
        Assert.That(await state.ExistsAsync("/a/b", CancellationToken.None), Is.False);
    }

    [Test]
    public async Task FileMetadataKeepsBirthtimeAndAdvancesMtimeOnWrite()
    {
        await using var state = NewState();
        await state.WriteFileAsync("/note.txt", "a", CancellationToken.None);
        var first = await state.StatAsync("/note.txt", CancellationToken.None);
        var repeated = await state.StatAsync("/note.txt", CancellationToken.None);
        Assert.That(repeated.Birthtime, Is.EqualTo(first.Birthtime));
        Assert.That(repeated.Mtime, Is.EqualTo(first.Mtime));

        await Task.Delay(5);
        await state.AppendFileAsync("/note.txt", "b", CancellationToken.None);
        var changed = await state.StatAsync("/note.txt", CancellationToken.None);
        Assert.That(changed.Birthtime, Is.EqualTo(first.Birthtime));
        Assert.That(changed.Mtime, Is.GreaterThan(first.Mtime));
    }

    [Test]
    public async Task FileSystemEnforcesPerFileAndPerSessionByteLimits()
    {
        await using var state = NewState();
        var maxFile = new string('x', CopilotVolatileSessionFsStore.MaxFileBytes);
        await state.WriteFileAsync("/one", maxFile, CancellationToken.None);
        Assert.Throws<IOException>(() => state.WriteFileAsync("/too-large", maxFile + "x", CancellationToken.None));
        await state.WriteFileAsync("/two", maxFile, CancellationToken.None);
        await state.WriteFileAsync("/three", maxFile, CancellationToken.None);
        await state.WriteFileAsync("/four", maxFile, CancellationToken.None);
        Assert.Throws<IOException>(() => state.WriteFileAsync("/over-session-limit", "x", CancellationToken.None));
        await state.RemoveAsync("/four", recursive: false, force: false, CancellationToken.None);
        await state.WriteFileAsync("/within-limit-again", "x", CancellationToken.None);
    }

    [Test]
    public async Task FileSystemEnforcesEntryAndPathLimitsWithoutPartialDirectoryCreation()
    {
        await using var state = NewState();

        for (var index = 0; index < CopilotVolatileSessionFsStore.MaxSessionFiles; index++)
            await state.WriteFileAsync($"/empty-{index}", string.Empty, CancellationToken.None);
        Assert.Throws<IOException>(() => state.WriteFileAsync("/overflow", string.Empty, CancellationToken.None));
        Assert.That(await state.ExistsAsync("/overflow", CancellationToken.None), Is.False);

        var deepPath = "/" + string.Join('/', Enumerable.Repeat("segment", 600));
        Assert.Throws<IOException>(() => state.MakeDirectoryAsync(deepPath, recursive: true, CancellationToken.None));
        Assert.That(await state.ExistsAsync("/segment", CancellationToken.None), Is.False,
            "a rejected recursive mkdir must not leave a partially created prefix");
    }

    [Test]
    public async Task FileSystemEnforcesDirectoryCountLimitAtomically()
    {
        await using var state = NewState();
        for (var index = 0; index < CopilotVolatileSessionFsStore.MaxSessionDirectories - 1; index++)
            await state.MakeDirectoryAsync($"/directory-{index}", recursive: false, CancellationToken.None);

        Assert.Throws<IOException>(() => state.MakeDirectoryAsync("/overflow/child", recursive: true, CancellationToken.None));
        Assert.That(await state.ExistsAsync("/overflow", CancellationToken.None), Is.False);
        Assert.That(await state.ReadDirectoryAsync("/", CancellationToken.None), Has.Count.EqualTo(
            CopilotVolatileSessionFsStore.MaxSessionDirectories - 1));
    }

    [Test]
    public async Task StoreLifecycleSerializesCreationAndDisposal()
    {
        var store = new CopilotVolatileSessionFsStore();
        var providers = new System.Collections.Concurrent.ConcurrentBag<CopilotVolatileSessionFsProvider>();
        using var barrier = new Barrier(33);
        var creators = Enumerable.Range(0, 32).Select(index => Task.Run(() =>
        {
            barrier.SignalAndWait();
            try { providers.Add(store.CreateProvider($"session-{index}")); }
            catch (ObjectDisposedException) { }
        })).ToArray();
        var dispose = Task.Run(async () =>
        {
            barrier.SignalAndWait();
            await store.DisposeAsync();
        });

        await Task.WhenAll(creators);
        await dispose;
        Assert.Throws<ObjectDisposedException>(() => store.CreateProvider("after-dispose"));
        foreach (var provider in providers)
            Assert.ThrowsAsync<ObjectDisposedException>(() => provider.ExistsAsync(CancellationToken.None));
    }

    [Test]
    public async Task SqliteExecRunQueryAndAtomicTransactionWorkInMemory()
    {
        await using var state = NewState();

        var exec = await state.QuerySqliteAsync(SessionFsSqliteQueryType.Exec,
            "CREATE TABLE samples(id INTEGER PRIMARY KEY, value TEXT NOT NULL)", null, CancellationToken.None);
        Assert.That(exec, Is.Null, "SDK contract returns null for exec queries");
        var sqliteLimit = await state.QuerySqliteAsync(SessionFsSqliteQueryType.Query,
            "PRAGMA max_page_count", null, CancellationToken.None);
        var pageSize = await state.QuerySqliteAsync(SessionFsSqliteQueryType.Query,
            "PRAGMA page_size", null, CancellationToken.None);
        Assert.That(Convert.ToInt64(sqliteLimit!.Rows.Single()["max_page_count"], CultureInfo.InvariantCulture) *
                    Convert.ToInt64(pageSize!.Rows.Single()["page_size"], CultureInfo.InvariantCulture),
            Is.LessThanOrEqualTo(CopilotVolatileSessionFsStore.MaxSessionSqliteBytes));
        var insertParams = new Dictionary<string, object?> { ["$value"] = "before" };
        var insert = await state.QuerySqliteAsync(SessionFsSqliteQueryType.Run,
            "INSERT INTO samples(value) VALUES ($value)", insertParams, CancellationToken.None);
        Assert.That(insert!.RowsAffected, Is.EqualTo(1));
        Assert.That(insert.LastInsertRowid, Is.EqualTo(1));

        var transaction = new List<SessionFsSqliteStatement>
        {
            new() { QueryType = SessionFsSqliteQueryType.Run, Query = "UPDATE samples SET value = 'after' WHERE id = 1" },
            new() { QueryType = SessionFsSqliteQueryType.Run, Query = "INSERT INTO missing_table(value) VALUES ('fail')" },
        };
        var transactionFailure = Assert.ThrowsAsync<SessionFsSqliteTransactionException>(
            () => state.ExecuteTransactionAsync(transaction, CancellationToken.None));
        Assert.That(transactionFailure!.ErrorClass, Is.EqualTo(SessionFsSqliteTransactionErrorClass.Fatal));

        var query = await state.QuerySqliteAsync(SessionFsSqliteQueryType.Query,
            "SELECT value FROM samples WHERE id = 1", null, CancellationToken.None);
        Assert.That(query!.Columns, Is.EqualTo(ValueColumn));
        Assert.That(query.Rows.Single()["value"], Is.EqualTo("before"), "failed transaction must roll back the first update");
    }

    [Test]
    public async Task ConcurrentSqliteStatementsAreSerializedWithoutLosingRows()
    {
        await using var state = NewState();
        await state.QuerySqliteAsync(SessionFsSqliteQueryType.Exec,
            "CREATE TABLE writes(id INTEGER PRIMARY KEY, value INTEGER NOT NULL)", null, CancellationToken.None);
        const int writers = 48;

        await Task.WhenAll(Enumerable.Range(0, writers).Select(value =>
            state.QuerySqliteAsync(SessionFsSqliteQueryType.Run,
                "INSERT INTO writes(value) VALUES ($value)",
                new Dictionary<string, object?> { ["$value"] = value }, CancellationToken.None)));

        var result = await state.QuerySqliteAsync(SessionFsSqliteQueryType.Query,
            "SELECT COUNT(*) AS count, SUM(value) AS total FROM writes", null, CancellationToken.None);
        Assert.That(Convert.ToInt64(result!.Rows.Single()["count"], CultureInfo.InvariantCulture), Is.EqualTo(writers));
        Assert.That(Convert.ToInt64(result.Rows.Single()["total"], CultureInfo.InvariantCulture),
            Is.EqualTo((writers - 1) * writers / 2));
    }

    [Test]
    public async Task StoreSharesStateForSessionIdAndDeletesItBeforeRecreation()
    {
        await using var store = new CopilotVolatileSessionFsStore();
        var provider = store.CreateProvider("session-id");
        Assert.That(store.ContainsSession("session-id"), Is.True);
        Assert.That(await provider.ExistsAsync(CancellationToken.None), Is.False);
        var handler = (ISessionFsHandler)provider;
        await handler.WriteFileAsync(new SessionFsWriteFileRequest
        {
            SessionId = "session-id", Path = "/transcript.txt", Content = "temporary",
        }, CancellationToken.None);
        await provider.QueryAsync(SessionFsSqliteQueryType.Exec,
            "CREATE TABLE ephemeral(value TEXT)", null, CancellationToken.None);

        var resumedProvider = store.CreateProvider("session-id");
        var resumedRead = await ((ISessionFsHandler)resumedProvider).ReadFileAsync(new SessionFsReadFileRequest
        {
            SessionId = "session-id", Path = "/transcript.txt",
        }, CancellationToken.None);
        Assert.That(resumedRead.Content, Is.EqualTo("temporary"));
        Assert.That(await resumedProvider.ExistsAsync(CancellationToken.None), Is.True);

        Assert.That(await store.DeleteSessionAsync("session-id"), Is.True);
        Assert.That(store.ContainsSession("session-id"), Is.False);
        var erasedRead = await ((ISessionFsHandler)provider).ReadFileAsync(new SessionFsReadFileRequest
        {
            SessionId = "session-id", Path = "/transcript.txt",
        }, CancellationToken.None);
        Assert.That(erasedRead.Error, Is.Not.Null,
            "providers handed to an SDK session must stop exposing erased file state");
        Assert.ThrowsAsync<ObjectDisposedException>(() => provider.QueryAsync(SessionFsSqliteQueryType.Query,
            "SELECT 1", null, CancellationToken.None));
        Assert.That(await store.DeleteSessionAsync("session-id"), Is.False);
        var freshProvider = store.CreateProvider("session-id");
        var freshRead = await ((ISessionFsHandler)freshProvider).ReadFileAsync(new SessionFsReadFileRequest
        {
            SessionId = "session-id", Path = "/transcript.txt",
        }, CancellationToken.None);
        Assert.That(freshRead.Error, Is.Not.Null);
        Assert.That(await freshProvider.ExistsAsync(CancellationToken.None), Is.False);
        Assert.CatchAsync<SqliteException>(() => freshProvider.QueryAsync(SessionFsSqliteQueryType.Query,
            "SELECT value FROM ephemeral", null, CancellationToken.None));
    }

    [Test]
    public async Task StoreKeepsDifferentSessionIdsIsolated()
    {
        await using var store = new CopilotVolatileSessionFsStore();
        var first = store.CreateProvider("first-session");
        var second = store.CreateProvider("second-session");
        await ((ISessionFsHandler)first).WriteFileAsync(new SessionFsWriteFileRequest
        {
            SessionId = "first-session", Path = "/private.txt", Content = "first only",
        }, CancellationToken.None);

        var firstRead = await ((ISessionFsHandler)store.CreateProvider("first-session")).ReadFileAsync(
            new SessionFsReadFileRequest { SessionId = "first-session", Path = "/private.txt" }, CancellationToken.None);
        var secondRead = await ((ISessionFsHandler)second).ReadFileAsync(
            new SessionFsReadFileRequest { SessionId = "second-session", Path = "/private.txt" }, CancellationToken.None);

        Assert.That(firstRead.Content, Is.EqualTo("first only"));
        Assert.That(secondRead.Error, Is.Not.Null);
    }

    [Test]
    public void SessionConfigSupportsPreallocatedSessionId()
    {
        var reserved = Guid.NewGuid().ToString("D");
        Assert.That(new SessionConfig { SessionId = reserved }.SessionId, Is.EqualTo(reserved));
    }

    [Test]
    public async Task RuntimeClientAndSessionConfigAreWiredToTheVolatileProvider()
    {
        await using var store = new CopilotVolatileSessionFsStore();
        var fsConfiguration = CopilotVolatileSessionFsStore.CreateConfiguration(Environment.CurrentDirectory);
        var clientOptions = CopilotRuntimeSettings.SessionClientOptions(Environment.CurrentDirectory, fsConfiguration);
        var sessionConfiguration = new SessionConfig();

        store.ConfigureSession(sessionConfiguration);

        Assert.Multiple(() =>
        {
            Assert.That(clientOptions.SessionFs, Is.SameAs(fsConfiguration));
            Assert.That(clientOptions.BaseDirectory, Does.Contain(".copilot"),
                "Copilot account and session clients must use the same official CLI home.");
            Assert.That(sessionConfiguration.CreateSessionFsProvider, Is.Not.Null,
                "each SDK session must bind its session ID to the in-memory handler");
            Assert.That(fsConfiguration.Capabilities?.Sqlite, Is.True);
        });
    }
}
#pragma warning restore GHCP001
