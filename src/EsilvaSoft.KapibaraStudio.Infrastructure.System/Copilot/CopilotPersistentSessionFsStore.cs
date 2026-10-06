using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GitHub.Copilot;
using GitHub.Copilot.Rpc;
using Microsoft.Data.Sqlite;

#pragma warning disable GHCP001 // SessionFs é experimental no SDK fixado; esta implementação fica isolada.

namespace EsilvaSoft.KapibaraStudio.SystemAdapters.Copilot;

/// <summary>SessionFs durável por ID, separado do COPILOT_HOME usado pela autenticação oficial.</summary>
internal sealed class CopilotPersistentSessionFsStore : ICopilotSessionFsStore
{
    public bool IsPersistent => true;
    public void ReserveSession(string sessionId) => throw new NotSupportedException("Sessões persistentes exigem reserva no histórico do produto.");
    private readonly string _root;
    private readonly string _lockRoot;
    private readonly StringComparer _pathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private readonly ConcurrentDictionary<string, SessionState> _sessions = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private bool _disposed;

    public CopilotPersistentSessionFsStore(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        _root = Path.GetFullPath(rootDirectory);
        _lockRoot = Path.Combine(_root, ".locks");
    }

    public static SessionFsConfig CreateConfiguration(string initialWorkingDirectory) => new()
    {
        // The process working directory is captured independently in CopilotClientOptions.
        // This virtual filesystem never mounts or opens that host directory.
        InitialWorkingDirectory = "/workspace",
        // This namespace is virtual on every host. A relative Windows state path is expanded by
        // the runtime to a drive-rooted host-shaped path, which this contained store must reject.
        SessionStatePath = "/session-state",
        Conventions = SessionFsSetProviderConventions.Posix,
        Capabilities = new SessionFsSetProviderCapabilities { Sqlite = true },
    };

    public void ConfigureSession(SessionConfigBase configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        configuration.CreateSessionFsProvider = session => CreateProvider(session.SessionId);
    }

    public CopilotPersistentSessionFsProvider CreateProvider(string sessionId)
    {
        ValidateSessionId(sessionId);
        _lifecycleGate.Wait();
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            EnsureRootExists();
            var state = _sessions.GetOrAdd(sessionId, id => new SessionState(
                SessionDirectory(id), SessionLockPath(id), _pathComparer));
            return new CopilotPersistentSessionFsProvider(state);
        }
        finally { _lifecycleGate.Release(); }
    }

    public bool ContainsSession(string sessionId)
    {
        ValidateSessionId(sessionId);
        _lifecycleGate.Wait();
        try
        {
            if (_disposed) return false;
            return _sessions.ContainsKey(sessionId) || Directory.Exists(_root) && Directory.Exists(SessionDirectory(sessionId));
        }
        finally { _lifecycleGate.Release(); }
    }

    public ValueTask<bool> DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default) =>
        DeleteSessionAsync(sessionId, deleteNativeSession: null, cancellationToken: cancellationToken);

    public async ValueTask<bool> DeleteSessionAsync(string sessionId,
        Func<CancellationToken, Task>? deleteNativeSession, CancellationToken cancellationToken = default)
    {
        ValidateSessionId(sessionId);
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (RootIsAbsent())
            {
                if (deleteNativeSession is not null)
                    await deleteNativeSession(cancellationToken).ConfigureAwait(false);
                return false;
            }
            Directory.CreateDirectory(_lockRoot);
            EnsureNotReparsePoint(_root);
            EnsureNotReparsePoint(_lockRoot);
            var directory = SessionDirectory(sessionId);
            var existed = Directory.Exists(directory) || File.Exists(directory);
            if (_sessions.TryRemove(sessionId, out var state)) await state.DisposeAsync().ConfigureAwait(false);
            using (AcquireSessionLock(sessionId))
            {
                if (existed) EnsureSessionDirectorySafe(directory);
                if (deleteNativeSession is not null)
                    await deleteNativeSession(cancellationToken).ConfigureAwait(false);
                if (!existed) return false;
                // Native deletion may complete despite cancellation; retain local state for explicit recovery.
                cancellationToken.ThrowIfCancellationRequested();
                Directory.Delete(directory, recursive: true);
            }
            return true;
        }
        finally { _lifecycleGate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            _disposed = true;
            var sessions = _sessions.ToArray();
            _sessions.Clear();
            await CopilotResourceCleanup.DisposeAllAsync(sessions.Select(item => item.Value)).ConfigureAwait(false);
        }
        finally { _lifecycleGate.Release(); }
    }

    private string SessionDirectory(string id) => Path.Combine(_root, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id))));

    private string SessionLockPath(string id) => Path.Combine(_lockRoot,
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id))) + ".lock");

    private void EnsureRootExists()
    {
        Directory.CreateDirectory(_root);
        EnsureNotReparsePoint(_root);
        Directory.CreateDirectory(_lockRoot);
        EnsureNotReparsePoint(_lockRoot);
    }

    private bool RootIsAbsent()
    {
        try
        {
            var attributes = File.GetAttributes(_root);
            if ((attributes & FileAttributes.Directory) == 0)
                throw new IOException("A raiz do SessionFs existe, mas não é um diretório.");
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("A raiz do SessionFs não pode ser um link simbólico ou reparse point.");
            return false;
        }
        catch (FileNotFoundException) { return true; }
        catch (DirectoryNotFoundException) { return true; }
    }

    private FileStream AcquireSessionLock(string sessionId)
    {
        var lockPath = SessionLockPath(sessionId);
        if (File.Exists(lockPath)) EnsureNotReparsePoint(lockPath);
        return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    private static void ValidateSessionId(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (id.Length > 128 || id.Any(char.IsControl)) throw new ArgumentException("Identificador de sessão Copilot inválido.", nameof(id));
    }

    private static string Normalize(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.Length > CopilotVolatileSessionFsStore.MaxPathLength || path.Contains('\0') || path.Any(char.IsControl))
            throw new IOException("Caminho SessionFs inválido.");
        var parts = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Any(static part => part is "." or ".." || part.Contains(':') || part.StartsWith("//", StringComparison.Ordinal)))
            throw new IOException("Caminho SessionFs fora da raiz virtual.");
        return parts.Length == 0 ? "/" : "/" + string.Join('/', parts);
    }

    private static void EnsureNotReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("SessionFs não aceita links simbólicos ou reparse points.");
    }

    private void EnsureSessionDirectorySafe(string directory)
    {
        var full = Path.GetFullPath(directory);
        var prefix = _root.EndsWith(Path.DirectorySeparatorChar) ? _root : _root + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new IOException("Diretório SessionFs fora da raiz configurada.");
        EnsureNotReparsePoint(full);
        _ = EnumerateSafeTree(full).ToArray();
    }

    private static IEnumerable<string> EnumerateSafeTree(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                EnsureNotReparsePoint(entry);
                yield return entry;
                if (Directory.Exists(entry)) pending.Push(entry);
            }
        }
    }

    internal sealed class SessionState : IAsyncDisposable
    {
        private readonly string _directory;
        private readonly string _filesRoot;
        private readonly StringComparer _comparer;
        private readonly FileStream _ownershipLock;
        private readonly object _filesGate = new();
        private readonly SemaphoreSlim _sqliteGate = new(1, 1);
        private SqliteConnection? _sqlite;
        private bool _disposed;

        public SessionState(string directory, string lockPath, StringComparer comparer)
        {
            _directory = Path.GetFullPath(directory);
            _filesRoot = Path.Combine(_directory, "files");
            _comparer = comparer;
            if (File.Exists(lockPath)) EnsureNotReparsePoint(lockPath);
            _ownershipLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            try
            {
                Directory.CreateDirectory(_directory);
                EnsureNotReparsePoint(_directory);
                Directory.CreateDirectory(_filesRoot);
                EnsureNotReparsePoint(_filesRoot);
                EnsureContainedAndNoReparsePoints(_filesRoot, allowMissingLeaf: false);
                RemoveInterruptedWrites();
                EnforceSessionQuotas();
            }
            catch
            {
                _ownershipLock.Dispose();
                throw;
            }
        }

        public Task<string> ReadFileAsync(string path, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var target = ResolveFile(path, allowMissingLeaf: false);
            lock (_filesGate)
            {
                ThrowIfDisposed();
                if (!File.Exists(target)) throw new FileNotFoundException("Arquivo SessionFs não encontrado.");
                return Task.FromResult(File.ReadAllText(target));
            }
        }

        public async Task WriteFileAsync(string path, string content, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(content); token.ThrowIfCancellationRequested();
            var target = ResolveFile(path, allowMissingLeaf: true);
            lock (_filesGate)
            {
                ThrowIfDisposed();
                EnforceFileQuota(target, Encoding.UTF8.GetByteCount(content));
                EnsureParents(target);
                AtomicWrite(target, content);
            }
            await Task.CompletedTask.ConfigureAwait(false);
        }

        public async Task AppendFileAsync(string path, string content, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(content); token.ThrowIfCancellationRequested();
            var target = ResolveFile(path, allowMissingLeaf: true);
            lock (_filesGate)
            {
                ThrowIfDisposed();
                var old = File.Exists(target) ? File.ReadAllText(target) : string.Empty;
                var combined = old + content;
                EnforceFileQuota(target, Encoding.UTF8.GetByteCount(combined));
                EnsureParents(target);
                AtomicWrite(target, combined);
            }
            await Task.CompletedTask.ConfigureAwait(false);
        }

        public Task<bool> ExistsAsync(string path, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); var target = ResolveFile(path, true);
            lock (_filesGate) { ThrowIfDisposed(); return Task.FromResult(File.Exists(target) || Directory.Exists(target)); }
        }

        public Task<SessionFsStatResult> StatAsync(string path, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); var target = ResolveFile(path, false);
            lock (_filesGate)
            {
                ThrowIfDisposed(); EnsureContainedAndNoReparsePoints(target, false);
                if (Directory.Exists(target)) { var info = new DirectoryInfo(target); return Task.FromResult(new SessionFsStatResult { IsDirectory = true, Birthtime = info.CreationTimeUtc, Mtime = info.LastWriteTimeUtc }); }
                if (File.Exists(target)) { var info = new FileInfo(target); return Task.FromResult(new SessionFsStatResult { IsFile = true, Birthtime = info.CreationTimeUtc, Mtime = info.LastWriteTimeUtc, Size = info.Length }); }
                throw new FileNotFoundException("Caminho SessionFs não encontrado.");
            }
        }

        public Task MakeDirectoryAsync(string path, bool recursive, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); var target = ResolveFile(path, true);
            lock (_filesGate)
            {
                ThrowIfDisposed(); EnsureParents(target, recursive, reserveTargetDirectory: !Directory.Exists(target));
                if (!recursive && !Directory.Exists(Path.GetDirectoryName(target)!)) throw new DirectoryNotFoundException("Diretório pai SessionFs não encontrado.");
                if (File.Exists(target)) throw new IOException("O caminho SessionFs é um arquivo.");
                if (!Directory.Exists(target) && EnumerateSafeTree(_filesRoot).Count(Directory.Exists) >= CopilotVolatileSessionFsStore.MaxSessionDirectories)
                    throw new IOException("Sessão SessionFs excede a cota de diretórios.");
                Directory.CreateDirectory(target); EnsureContainedAndNoReparsePoints(target, false); EnforceDirectoryQuota();
            }
            return Task.CompletedTask;
        }

        public Task<IList<string>> ReadDirectoryAsync(string path, CancellationToken token) =>
            Task.FromResult<IList<string>>(ReadEntries(path, token).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList()!);

        public Task<IList<SessionFsReaddirWithTypesEntry>> ReadDirectoryWithTypesAsync(string path, CancellationToken token) =>
            Task.FromResult<IList<SessionFsReaddirWithTypesEntry>>(ReadEntries(path, token).Select(entry => new SessionFsReaddirWithTypesEntry
            { Name = Path.GetFileName(entry), Type = Directory.Exists(entry) ? SessionFsReaddirWithTypesEntryType.Directory : SessionFsReaddirWithTypesEntryType.File }).OrderBy(entry => entry.Name, StringComparer.Ordinal).ToList());

        private string[] ReadEntries(string path, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); var target = ResolveFile(path, false);
            lock (_filesGate) { ThrowIfDisposed(); if (!Directory.Exists(target)) throw new DirectoryNotFoundException("Diretório SessionFs não encontrado."); EnsureContainedAndNoReparsePoints(target, false); return Directory.GetFileSystemEntries(target); }
        }

        public Task RemoveAsync(string path, bool recursive, bool force, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); var target = ResolveFile(path, false);
            lock (_filesGate)
            {
                ThrowIfDisposed(); if (target == _filesRoot) throw new IOException("A raiz SessionFs não pode ser removida.");
                if (!File.Exists(target) && !Directory.Exists(target)) { if (force) return Task.CompletedTask; throw new FileNotFoundException("Caminho SessionFs não encontrado."); }
                EnsureContainedAndNoReparsePoints(target, false);
                if (File.Exists(target)) File.Delete(target); else Directory.Delete(target, recursive);
                EnforceSessionQuotas();
            }
            return Task.CompletedTask;
        }

        public Task RenameAsync(string source, string destination, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); var src = ResolveFile(source, false); var dst = ResolveFile(destination, true);
            lock (_filesGate)
            {
                ThrowIfDisposed(); EnsureContainedAndNoReparsePoints(src, false); EnsureParents(dst);
                if (IsWithin(dst, src)) throw new IOException("Movimento SessionFs inválido.");
                if (!Directory.Exists(Path.GetDirectoryName(dst)!)) throw new DirectoryNotFoundException("Diretório destino SessionFs não encontrado.");
                if (File.Exists(src)) { if (Directory.Exists(dst)) throw new IOException("Destino SessionFs já existe."); File.Move(src, dst, overwrite: true); }
                else if (Directory.Exists(src)) { if (File.Exists(dst) || Directory.Exists(dst)) throw new IOException("Destino SessionFs já existe."); Directory.Move(src, dst); }
                else throw new FileNotFoundException("Origem SessionFs não encontrada.");
                EnforceSessionQuotas();
            }
            return Task.CompletedTask;
        }

        public async Task<SessionFsSqliteResult?> QuerySqliteAsync(SessionFsSqliteQueryType type, string query, IDictionary<string, object?>? parameters, CancellationToken token)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(query); await _sqliteGate.WaitAsync(token).ConfigureAwait(false);
            try { ThrowIfDisposed(); var db = await GetSqliteAsync(token).ConfigureAwait(false); await using var cmd = CreateCommand(db, query, parameters, null); return await ExecuteSqliteCommandAsync(cmd, type, token).ConfigureAwait(false); }
            finally { _sqliteGate.Release(); }
        }

        public async Task<IList<SessionFsSqliteResult>> ExecuteTransactionAsync(IList<SessionFsSqliteStatement> statements, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(statements); await _sqliteGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed(); var db = await GetSqliteAsync(token).ConfigureAwait(false);
                await using var tx = (SqliteTransaction)await db.BeginTransactionAsync(token).ConfigureAwait(false);
                var results = new List<SessionFsSqliteResult>(statements.Count);
                foreach (var item in statements) { await using var cmd = CreateCommand(db, item.Query, item.Params, tx); results.Add(await ExecuteSqliteCommandAsync(cmd, item.QueryType, token).ConfigureAwait(false) ?? new SessionFsSqliteResult()); }
                await tx.CommitAsync(token).ConfigureAwait(false); return results;
            }
            catch (Exception ex) when (ex is not SessionFsSqliteTransactionException and not OperationCanceledException)
            { var kind = ex is SqliteException se && se.SqliteErrorCode is 5 or 6 ? SessionFsSqliteTransactionErrorClass.BusyOrLocked : SessionFsSqliteTransactionErrorClass.Fatal; throw new SessionFsSqliteTransactionException("A transação SQLite SessionFs falhou e foi revertida.", kind, ex); }
            finally { _sqliteGate.Release(); }
        }

        public async Task<bool> SqliteExistsAsync(CancellationToken token)
        { token.ThrowIfCancellationRequested(); await _sqliteGate.WaitAsync(token).ConfigureAwait(false); try { ThrowIfDisposed(); return _sqlite is { State: System.Data.ConnectionState.Open }; } finally { _sqliteGate.Release(); } }

        public async ValueTask DisposeAsync()
        {
            await _sqliteGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_disposed) return;
                lock (_filesGate) _disposed = true;
                // The ownership lock must close even when SQLite reports a disposal failure.
                await CopilotResourceCleanup.DisposeAllAsync(_sqlite is null
                    ? [_ownershipLock] : [_sqlite, _ownershipLock]).ConfigureAwait(false);
            }
            finally { _sqliteGate.Release(); }
        }

        private async Task<SqliteConnection> GetSqliteAsync(CancellationToken token)
        {
            if (_sqlite is { State: System.Data.ConnectionState.Open }) return _sqlite;
            var path = Path.Combine(_directory, "session.sqlite"); EnsureContainedAndNoReparsePoints(path, true);
            _sqlite = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false, DefaultTimeout = 5 }.ToString());
            await _sqlite.OpenAsync(token).ConfigureAwait(false);
            await using var pageSize = _sqlite.CreateCommand(); pageSize.CommandText = "PRAGMA page_size";
            var size = Convert.ToInt64(await pageSize.ExecuteScalarAsync(token).ConfigureAwait(false), CultureInfo.InvariantCulture);
            await using var limit = _sqlite.CreateCommand(); limit.CommandText = $"PRAGMA max_page_count = {CopilotVolatileSessionFsStore.MaxSessionSqliteBytes / size}";
            await limit.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            await using var journal = _sqlite.CreateCommand(); journal.CommandText = "PRAGMA journal_mode=DELETE; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000";
            await journal.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            return _sqlite;
        }

        private static SqliteCommand CreateCommand(SqliteConnection db, string query, IDictionary<string, object?>? values, SqliteTransaction? tx)
        { var cmd = db.CreateCommand(); cmd.CommandText = query; cmd.Transaction = tx; if (values is not null) foreach (var (name, value) in values) cmd.Parameters.AddWithValue(name, ToSqliteValue(value)); return cmd; }
        private static object ToSqliteValue(object? value) => value switch
        { null => DBNull.Value, JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined } => DBNull.Value, JsonElement e when e.ValueKind == JsonValueKind.String => e.GetString() ?? "", JsonElement e when e.ValueKind == JsonValueKind.True => true, JsonElement e when e.ValueKind == JsonValueKind.False => false, JsonElement e when e.TryGetInt64(out var n) => n, JsonElement e when e.TryGetDouble(out var d) => d, JsonElement e => e.GetRawText(), _ => value };
        private static async Task<SessionFsSqliteResult?> ExecuteSqliteCommandAsync(SqliteCommand cmd, SessionFsSqliteQueryType type, CancellationToken token)
        {
            if (type == SessionFsSqliteQueryType.Exec) { await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false); return null; }
            if (type == SessionFsSqliteQueryType.Query)
            {
                await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false); var columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToList(); var rows = new List<IDictionary<string, object>>();
                while (await reader.ReadAsync(token).ConfigureAwait(false)) { var row = new Dictionary<string, object>(StringComparer.Ordinal); for (var i = 0; i < columns.Count; i++) row[columns[i]] = reader.IsDBNull(i) ? DBNull.Value : reader.GetValue(i); rows.Add(row); }
                return new SessionFsSqliteResult { Columns = columns, Rows = rows };
            }
            if (type == SessionFsSqliteQueryType.Run) { var affected = await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false); await using var id = cmd.Connection!.CreateCommand(); id.Transaction = cmd.Transaction; id.CommandText = "SELECT last_insert_rowid()"; var value = await id.ExecuteScalarAsync(token).ConfigureAwait(false); return new SessionFsSqliteResult { RowsAffected = affected, LastInsertRowid = value is long n ? n : null }; }
            throw new NotSupportedException("Tipo de consulta SQLite SessionFs não suportado.");
        }

        private string ResolveFile(string path, bool allowMissingLeaf)
        {
            var normalized = Normalize(path); var relative = normalized.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            var full = Path.GetFullPath(Path.Combine(_filesRoot, relative));
            var prefix = _filesRoot.EndsWith(Path.DirectorySeparatorChar) ? _filesRoot : _filesRoot + Path.DirectorySeparatorChar;
            if (full != _filesRoot && !full.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) throw new IOException("Caminho SessionFs fora da raiz virtual.");
            EnsureContainedAndNoReparsePoints(full, allowMissingLeaf); return full;
        }

        private void EnsureParents(string target, bool recursive = true, bool reserveTargetDirectory = false)
        {
            var parent = Path.GetDirectoryName(target)!;
            var missing = new Stack<string>();
            var cursor = parent;
            while (!Directory.Exists(cursor))
            {
                if (File.Exists(cursor)) throw new IOException("Um componente do caminho SessionFs é um arquivo.");
                if (!recursive) throw new DirectoryNotFoundException("Diretório pai SessionFs não encontrado.");
                missing.Push(cursor);
                var next = Path.GetDirectoryName(cursor);
                if (next is null || !IsWithin(cursor, _filesRoot) && !string.Equals(cursor, _filesRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                    throw new IOException("Caminho SessionFs fora da raiz da sessão.");
                cursor = next;
            }
            EnsureContainedAndNoReparsePoints(cursor, false);
            var currentDirectories = EnumerateSafeTree(_filesRoot).Count(Directory.Exists);
            if (currentDirectories + missing.Count + (reserveTargetDirectory ? 1 : 0) > CopilotVolatileSessionFsStore.MaxSessionDirectories)
                throw new IOException("Sessão SessionFs excede a cota de diretórios.");
            while (missing.TryPop(out var directory))
            {
                Directory.CreateDirectory(directory);
                EnsureContainedAndNoReparsePoints(directory, false);
            }
        }
        private void EnsureContainedAndNoReparsePoints(string target, bool allowMissingLeaf)
        {
            var full = Path.GetFullPath(target); var root = Path.GetFullPath(_directory);
            var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
            if (full != root && !full.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) throw new IOException("Caminho SessionFs fora da raiz da sessão.");
            var cursor = root; EnsureNotReparsePoint(cursor);
            var relative = Path.GetRelativePath(root, full);
            if (relative == ".") return;
            var parts = relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < parts.Length; i++)
            {
                cursor = Path.Combine(cursor, parts[i]);
                if (!File.Exists(cursor) && !Directory.Exists(cursor)) { if (allowMissingLeaf || i < parts.Length - 1) continue; throw new FileNotFoundException("Caminho SessionFs não encontrado."); }
                EnsureNotReparsePoint(cursor);
            }
        }
        private void EnforceFileQuota(string target, long newSize)
        {
            if (newSize > CopilotVolatileSessionFsStore.MaxFileBytes) throw new IOException("Arquivo SessionFs excede a cota.");
            var old = File.Exists(target) ? new FileInfo(target).Length : 0;
            var entries = EnumerateSafeTree(_filesRoot).Where(File.Exists).ToArray();
            if (!File.Exists(target) && entries.Length >= CopilotVolatileSessionFsStore.MaxSessionFiles) throw new IOException("Sessão SessionFs excede a cota de arquivos.");
            var total = entries.Sum(file => new FileInfo(file).Length) - old + newSize;
            if (total > CopilotVolatileSessionFsStore.MaxSessionFileBytes) throw new IOException("Sessão SessionFs excede a cota de bytes.");
        }
        private void AtomicWrite(string target, string content)
        {
            var temporary = Path.Combine(Path.GetDirectoryName(target)!, ".kapibara-sessionfs-tmp-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                {
                    writer.Write(content);
                    writer.Flush();
                    stream.Flush(flushToDisk: true);
                }
                EnsureContainedAndNoReparsePoints(temporary, false);
                File.Move(temporary, target, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        private void RemoveInterruptedWrites()
        {
            foreach (var entry in EnumerateSafeTree(_filesRoot).Where(File.Exists).ToArray())
                if (Path.GetFileName(entry).StartsWith(".kapibara-sessionfs-tmp-", StringComparison.Ordinal)) File.Delete(entry);
        }
        private void EnforceDirectoryQuota() { if (EnumerateSafeTree(_filesRoot).Count(Directory.Exists) > CopilotVolatileSessionFsStore.MaxSessionDirectories) throw new IOException("Sessão SessionFs excede a cota de diretórios."); }
        private void EnforceSessionQuotas() { var entries = EnumerateSafeTree(_filesRoot).ToArray(); if (entries.Count(File.Exists) > CopilotVolatileSessionFsStore.MaxSessionFiles || entries.Count(Directory.Exists) > CopilotVolatileSessionFsStore.MaxSessionDirectories || entries.Where(File.Exists).Sum(f => new FileInfo(f).Length) > CopilotVolatileSessionFsStore.MaxSessionFileBytes) throw new IOException("Operação excede cotas SessionFs."); }
        private static bool IsWithin(string path, string parent) => path.StartsWith(parent + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
    }
}

internal sealed class CopilotPersistentSessionFsProvider(CopilotPersistentSessionFsStore.SessionState state)
    : SessionFsProvider, ISessionFsSqliteProvider, ISessionFsSqliteTransactionProvider
{
    protected override Task<string> ReadFileAsync(string path, CancellationToken cancellationToken) => state.ReadFileAsync(path, cancellationToken);
    protected override Task WriteFileAsync(string path, string content, int? mode, CancellationToken cancellationToken) => state.WriteFileAsync(path, content, cancellationToken);
    protected override Task AppendFileAsync(string path, string content, int? mode, CancellationToken cancellationToken) => state.AppendFileAsync(path, content, cancellationToken);
    protected override Task<bool> ExistsAsync(string path, CancellationToken cancellationToken) => state.ExistsAsync(path, cancellationToken);
    protected override Task<SessionFsStatResult> StatAsync(string path, CancellationToken cancellationToken) => state.StatAsync(path, cancellationToken);
    protected override Task MakeDirectoryAsync(string path, bool recursive, int? mode, CancellationToken cancellationToken) => state.MakeDirectoryAsync(path, recursive, cancellationToken);
    protected override Task<IList<string>> ReadDirectoryAsync(string path, CancellationToken cancellationToken) => state.ReadDirectoryAsync(path, cancellationToken);
    protected override Task<IList<SessionFsReaddirWithTypesEntry>> ReadDirectoryWithTypesAsync(string path, CancellationToken cancellationToken) => state.ReadDirectoryWithTypesAsync(path, cancellationToken);
    protected override Task RemoveAsync(string path, bool recursive, bool force, CancellationToken cancellationToken) => state.RemoveAsync(path, recursive, force, cancellationToken);
    protected override Task RenameAsync(string source, string destination, CancellationToken cancellationToken) => state.RenameAsync(source, destination, cancellationToken);
    public Task<SessionFsSqliteResult?> QueryAsync(SessionFsSqliteQueryType type, string query, IDictionary<string, object?>? parameters, CancellationToken token) => state.QuerySqliteAsync(type, query, parameters, token);
    public Task<bool> ExistsAsync(CancellationToken token) => state.SqliteExistsAsync(token);
    public Task<IList<SessionFsSqliteResult>> TransactionAsync(IList<SessionFsSqliteStatement> statements, CancellationToken token) => state.ExecuteTransactionAsync(statements, token);
}

#pragma warning restore GHCP001
