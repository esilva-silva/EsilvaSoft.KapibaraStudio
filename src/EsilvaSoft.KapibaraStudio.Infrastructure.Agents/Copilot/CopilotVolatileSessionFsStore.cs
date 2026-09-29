using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using GitHub.Copilot;
using GitHub.Copilot.Rpc;
using Microsoft.Data.Sqlite;

#pragma warning disable GHCP001 // SessionFs is experimental in the pinned SDK; this isolated prototype validates its contract.

namespace EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Copilot;

/// <summary>
/// Armazenamento por sessão em memória usado quando o usuário desativa retenção de histórico.
/// O runtime oficial recebe esta implementação por meio do SessionFs experimental do SDK.
/// </summary>
internal sealed class CopilotVolatileSessionFsStore : ICopilotSessionFsStore
{
    internal const int MaxFileBytes = 4 * 1024 * 1024;
    internal const long MaxSessionFileBytes = 16 * 1024 * 1024;
    internal const long MaxSessionSqliteBytes = 16 * 1024 * 1024;
    internal const int MaxSessionFiles = 4096;
    internal const int MaxSessionDirectories = 4096;
    internal const int MaxPathLength = 4096;
    private readonly ConcurrentDictionary<string, SessionState> _sessions = new(StringComparer.Ordinal);
    private readonly StringComparer _pathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
    private bool _disposed;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);

    public static SessionFsConfig CreateConfiguration(string initialWorkingDirectory) => new()
    {
        InitialWorkingDirectory = Path.GetFullPath(initialWorkingDirectory),
        SessionStatePath = "session-state",
        Conventions = OperatingSystem.IsWindows()
            ? SessionFsSetProviderConventions.Windows
            : SessionFsSetProviderConventions.Posix,
        Capabilities = new SessionFsSetProviderCapabilities { Sqlite = true },
    };

    public void ConfigureSession(SessionConfigBase configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _lifecycleGate.Wait();
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            configuration.CreateSessionFsProvider = session => CreateProvider(session.SessionId);
        }
        finally { _lifecycleGate.Release(); }
    }

    public CopilotVolatileSessionFsProvider CreateProvider(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        _lifecycleGate.Wait();
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return new CopilotVolatileSessionFsProvider(_sessions.GetOrAdd(sessionId,
                _ => new SessionState(_pathComparer)));
        }
        finally { _lifecycleGate.Release(); }
    }

    public bool ContainsSession(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        _lifecycleGate.Wait();
        try { return !_disposed && _sessions.ContainsKey(sessionId); }
        finally { _lifecycleGate.Release(); }
    }

    /// <summary>Descarta o filesystem e o SQLite em memória após remoção da sessão Copilot.</summary>
    public async ValueTask<bool> DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        cancellationToken.ThrowIfCancellationRequested();
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_sessions.TryRemove(sessionId, out var state)) return false;
            await state.DisposeAsync().ConfigureAwait(false);
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
            foreach (var (_, session) in sessions)
                await session.DisposeAsync().ConfigureAwait(false);
        }
        finally { _lifecycleGate.Release(); }
    }

    internal sealed class SessionState(StringComparer pathComparer) : IAsyncDisposable
    {
        private readonly object _filesGate = new();
        private readonly StringComparer _pathComparer = pathComparer;
        private readonly Dictionary<string, FileEntry> _files = new(pathComparer);
        private readonly Dictionary<string, DateTimeOffset> _directories = new(pathComparer)
        {
            ["/"] = DateTimeOffset.UtcNow,
        };
        private readonly SemaphoreSlim _sqliteGate = new(1, 1);
        private readonly SqliteConnection _sqlite = new("Data Source=:memory:;Mode=Memory;Cache=Shared");
        private bool _sqliteLimitConfigured;
        private bool _disposed;
        private long _fileBytes;

        private sealed record FileEntry(string Content, DateTimeOffset Birthtime, DateTimeOffset Mtime)
        {
            public int Size => System.Text.Encoding.UTF8.GetByteCount(Content);
        }

        public async Task InitializeSqliteAsync(CancellationToken cancellationToken)
        {
            if (_sqlite.State != System.Data.ConnectionState.Open)
                await _sqlite.OpenAsync(cancellationToken).ConfigureAwait(false);
            if (_sqliteLimitConfigured) return;

            await using var pageSizeCommand = _sqlite.CreateCommand();
            pageSizeCommand.CommandText = "PRAGMA page_size";
            var pageSize = Convert.ToInt64(await pageSizeCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
            var maxPages = CopilotVolatileSessionFsStore.MaxSessionSqliteBytes / pageSize;
            await using var limitCommand = _sqlite.CreateCommand();
            limitCommand.CommandText = $"PRAGMA max_page_count = {maxPages}";
            await limitCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            _sqliteLimitConfigured = true;
        }

        public static string Normalize(string path)
        {
            ArgumentNullException.ThrowIfNull(path);
            if (path.Length > MaxPathLength || path.Contains('\0') || path.Any(char.IsControl))
                throw new IOException("O caminho SessionFs contém caracteres inválidos.");

            var parts = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Any(static part => part is "." or ".."))
                throw new IOException("Caminho SessionFs fora da raiz virtual.");
            return parts.Length == 0 ? "/" : "/" + string.Join('/', parts);
        }

        public Task<string> ReadFileAsync(string path, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var key = Normalize(path);
            lock (_filesGate)
            {
                ThrowIfDisposed();
                if (_files.TryGetValue(key, out var entry)) return Task.FromResult(entry.Content);
                throw new FileNotFoundException("Arquivo SessionFs não encontrado.");
            }
        }

        public Task WriteFileAsync(string path, string content, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(content);
            var key = Normalize(path);
            if (key == "/") throw new IOException("A raiz SessionFs não pode ser arquivo.");
            lock (_filesGate)
            {
                ThrowIfDisposed();
                EnsureParentDirectories(key);
                if (_directories.ContainsKey(key)) throw new IOException("O caminho SessionFs é um diretório.");
                var current = _files.GetValueOrDefault(key);
                _files[key] = CreateFileEntry(key, content, current?.Birthtime, current?.Mtime);
            }
            return Task.CompletedTask;
        }

        public async Task AppendFileAsync(string path, string content, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(content);
            token.ThrowIfCancellationRequested();
            var key = Normalize(path);
            if (key == "/") throw new IOException("A raiz SessionFs não pode ser arquivo.");
            lock (_filesGate)
            {
                ThrowIfDisposed();
                EnsureParentDirectories(key);
                if (_directories.ContainsKey(key)) throw new IOException("O caminho SessionFs é um diretório.");
                var current = _files.GetValueOrDefault(key);
                _files[key] = CreateFileEntry(key, (current?.Content ?? string.Empty) + content, current?.Birthtime, current?.Mtime);
            }
            await Task.CompletedTask.ConfigureAwait(false);
        }

        public Task<bool> ExistsAsync(string path, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var key = Normalize(path);
            lock (_filesGate)
            {
                ThrowIfDisposed();
                return Task.FromResult(_files.ContainsKey(key) || _directories.ContainsKey(key));
            }
        }

        public Task<SessionFsStatResult> StatAsync(string path, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var key = Normalize(path);
            lock (_filesGate)
            {
                ThrowIfDisposed();
                if (_directories.TryGetValue(key, out var directoryTime))
                    return Task.FromResult(new SessionFsStatResult
                    {
                        IsDirectory = true,
                        Birthtime = directoryTime,
                        Mtime = directoryTime,
                        Size = 0,
                    });
                if (_files.TryGetValue(key, out var entry))
                {
                    return Task.FromResult(new SessionFsStatResult
                    {
                        IsFile = true,
                        Birthtime = entry.Birthtime,
                        Mtime = entry.Mtime,
                        Size = entry.Size,
                    });
                }
                throw new FileNotFoundException("Caminho SessionFs não encontrado.");
            }
        }

        public Task MakeDirectoryAsync(string path, bool recursive, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var key = Normalize(path);
            lock (_filesGate)
            {
                ThrowIfDisposed();
                if (_files.ContainsKey(key)) throw new IOException("O caminho SessionFs é um arquivo.");
                if (recursive)
                {
                    var current = "/";
                    var missing = new List<string>();
                    foreach (var part in key.Split('/', StringSplitOptions.RemoveEmptyEntries))
                    {
                        current = current == "/" ? "/" + part : current + "/" + part;
                        if (_files.ContainsKey(current)) throw new IOException("Um componente do caminho SessionFs é um arquivo.");
                        if (!_directories.ContainsKey(current)) missing.Add(current);
                    }

                    EnsureDirectoryCapacity(missing.Count);
                    var now = DateTimeOffset.UtcNow;
                    foreach (var directory in missing) _directories.Add(directory, now);
                }
                else if (!_directories.ContainsKey(Parent(key)))
                {
                    throw new DirectoryNotFoundException("Diretório pai SessionFs não encontrado.");
                }
                else
                {
                    AddDirectory(key, DateTimeOffset.UtcNow);
                }
            }
            return Task.CompletedTask;
        }

        public Task<IList<string>> ReadDirectoryAsync(string path, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var key = Normalize(path);
            lock (_filesGate)
            {
                ThrowIfDisposed();
                if (!_directories.ContainsKey(key)) throw new DirectoryNotFoundException("Diretório SessionFs não encontrado.");
                return Task.FromResult<IList<string>>(GetEntries(key).Select(static entry => entry.Name).Order(StringComparer.Ordinal).ToList());
            }
        }

        public Task<IList<SessionFsReaddirWithTypesEntry>> ReadDirectoryWithTypesAsync(string path, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var key = Normalize(path);
            lock (_filesGate)
            {
                ThrowIfDisposed();
                if (!_directories.ContainsKey(key)) throw new DirectoryNotFoundException("Diretório SessionFs não encontrado.");
                return Task.FromResult<IList<SessionFsReaddirWithTypesEntry>>(GetEntries(key)
                    .Select(static entry => new SessionFsReaddirWithTypesEntry
                    {
                        Name = entry.Name,
                        Type = entry.IsDirectory ? SessionFsReaddirWithTypesEntryType.Directory : SessionFsReaddirWithTypesEntryType.File,
                    }).OrderBy(static entry => entry.Name, StringComparer.Ordinal).ToList());
            }
        }

        public Task RemoveAsync(string path, bool recursive, bool force, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var key = Normalize(path);
            if (key == "/") throw new IOException("A raiz SessionFs não pode ser removida.");
            lock (_filesGate)
            {
                ThrowIfDisposed();
                var exists = _files.ContainsKey(key) || _directories.ContainsKey(key);
                if (!exists)
                {
                    if (force) return Task.CompletedTask;
                    throw new FileNotFoundException("Caminho SessionFs não encontrado.");
                }
                if (_files.Remove(key, out var removedFile)) { _fileBytes -= removedFile.Size; return Task.CompletedTask; }
                var descendants = _files.Keys.Where(path => IsWithin(path, key)).ToArray();
                var childDirectories = _directories.Keys.Where(path => path != key && IsWithin(path, key)).ToArray();
                if (!recursive && (descendants.Length > 0 || childDirectories.Length > 0))
                    throw new IOException("Diretório SessionFs não vazio.");
                foreach (var descendant in descendants)
                    if (_files.Remove(descendant, out var removed)) _fileBytes -= removed.Size;
                foreach (var directory in childDirectories) _directories.Remove(directory);
                _directories.Remove(key);
            }
            return Task.CompletedTask;
        }

        public Task RenameAsync(string source, string destination, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var src = Normalize(source);
            var dest = Normalize(destination);
            if (src == "/" || dest == "/" || IsWithin(dest, src))
                throw new IOException("Movimento SessionFs inválido.");
            if (_pathComparer.Equals(src, dest)) return Task.CompletedTask;
            lock (_filesGate)
            {
                ThrowIfDisposed();
                var sourceIsFile = _files.TryGetValue(src, out var content);
                var sourceIsDirectory = _directories.ContainsKey(src);
                if (!sourceIsFile && !sourceIsDirectory) throw new FileNotFoundException("Origem SessionFs não encontrada.");
                if (!_directories.ContainsKey(Parent(dest))) throw new DirectoryNotFoundException("Diretório destino SessionFs não encontrado.");
                var destinationExists = _files.ContainsKey(dest) || _directories.ContainsKey(dest);
                if (destinationExists)
                {
                    if (sourceIsDirectory || _directories.ContainsKey(dest)) throw new IOException("Destino SessionFs já existe.");
                    if (_files.Remove(dest, out var replaced)) _fileBytes -= replaced.Size;
                }

                if (sourceIsFile)
                {
                    _files.Remove(src);
                    _files[dest] = content!;
                    return Task.CompletedTask;
                }

                var fileMoves = _files.Where(pair => IsWithin(pair.Key, src)).ToArray();
                var directoryMoves = _directories.Where(pair => _pathComparer.Equals(pair.Key, src) || IsWithin(pair.Key, src)).ToArray();
                foreach (var pair in fileMoves) _files.Remove(pair.Key);
                foreach (var pair in directoryMoves) _directories.Remove(pair.Key);
                foreach (var pair in directoryMoves) _directories[ReplacePrefix(pair.Key, src, dest)] = pair.Value;
                foreach (var pair in fileMoves) _files[ReplacePrefix(pair.Key, src, dest)] = pair.Value;
            }
            return Task.CompletedTask;
        }

        public async Task<SessionFsSqliteResult?> QuerySqliteAsync(
            SessionFsSqliteQueryType queryType, string query, IDictionary<string, object?>? parameters, CancellationToken token)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(query);
            await _sqliteGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                await InitializeSqliteAsync(token).ConfigureAwait(false);
                await using var command = CreateCommand(query, parameters, transaction: null);
                return await ExecuteSqliteCommandAsync(command, queryType, token).ConfigureAwait(false);
            }
            finally { _sqliteGate.Release(); }
        }

        public async Task<IList<SessionFsSqliteResult>> ExecuteTransactionAsync(
            IList<SessionFsSqliteStatement> statements, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(statements);
            await _sqliteGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                await InitializeSqliteAsync(token).ConfigureAwait(false);
                await using var transaction = (SqliteTransaction)await _sqlite.BeginTransactionAsync(token).ConfigureAwait(false);
                var results = new List<SessionFsSqliteResult>(statements.Count);
                foreach (var statement in statements)
                {
                    await using var command = CreateCommand(statement.Query, statement.Params, transaction);
                    var result = await ExecuteSqliteCommandAsync(command, statement.QueryType, token).ConfigureAwait(false);
                    results.Add(result ?? new SessionFsSqliteResult());
                }
                await transaction.CommitAsync(token).ConfigureAwait(false);
                return results;
            }
            catch (Exception exception) when (exception is not SessionFsSqliteTransactionException and not OperationCanceledException)
            {
                var classification = exception is SqliteException sqliteException && sqliteException.SqliteErrorCode is 5 or 6
                    ? SessionFsSqliteTransactionErrorClass.BusyOrLocked
                    : SessionFsSqliteTransactionErrorClass.Fatal;
                throw new SessionFsSqliteTransactionException("A transação SQLite SessionFs falhou e foi revertida.", classification, exception);
            }
            finally { _sqliteGate.Release(); }
        }

        public async Task<bool> SqliteExistsAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            await _sqliteGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                return _sqlite.State == System.Data.ConnectionState.Open;
            }
            finally { _sqliteGate.Release(); }
        }

        public async ValueTask DisposeAsync()
        {
            await _sqliteGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_disposed) return;
                _disposed = true;
                lock (_filesGate) { _files.Clear(); _directories.Clear(); _fileBytes = 0; }
                await _sqlite.DisposeAsync().ConfigureAwait(false);
            }
            finally { _sqliteGate.Release(); }
        }

        private SqliteCommand CreateCommand(string query, IDictionary<string, object?>? parameters, SqliteTransaction? transaction)
        {
            var command = _sqlite.CreateCommand();
            command.CommandText = query;
            command.Transaction = transaction;
            if (parameters is not null)
                foreach (var (name, value) in parameters)
                    command.Parameters.AddWithValue(name, ToSqliteValue(value));
            return command;
        }

        private static object ToSqliteValue(object? value) => value switch
        {
            null => DBNull.Value,
            JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined } => DBNull.Value,
            JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonElement element when element.ValueKind == JsonValueKind.True => true,
            JsonElement element when element.ValueKind == JsonValueKind.False => false,
            JsonElement element when element.TryGetInt64(out var number) => number,
            JsonElement element when element.TryGetDouble(out var number) => number,
            JsonElement element => element.GetRawText(),
            _ => value,
        };

        private static async Task<SessionFsSqliteResult?> ExecuteSqliteCommandAsync(
            SqliteCommand command, SessionFsSqliteQueryType queryType, CancellationToken token)
        {
            if (queryType == SessionFsSqliteQueryType.Exec)
            {
                await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                return null;
            }
            if (queryType == SessionFsSqliteQueryType.Query)
            {
                await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
                var columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToList();
                var rows = new List<IDictionary<string, object>>();
                while (await reader.ReadAsync(token).ConfigureAwait(false))
                {
                    var row = new Dictionary<string, object>(StringComparer.Ordinal);
                    for (var index = 0; index < columns.Count; index++)
                        row[columns[index]] = reader.IsDBNull(index) ? DBNull.Value : reader.GetValue(index);
                    rows.Add(row);
                }
                return new SessionFsSqliteResult { Columns = columns, Rows = rows };
            }
            if (queryType == SessionFsSqliteQueryType.Run)
            {
                var affected = await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                await using var rowIdCommand = command.Connection!.CreateCommand();
                rowIdCommand.Transaction = command.Transaction;
                rowIdCommand.CommandText = "SELECT last_insert_rowid()";
                var rowIdValue = await rowIdCommand.ExecuteScalarAsync(token).ConfigureAwait(false);
                return new SessionFsSqliteResult
                {
                    RowsAffected = affected,
                    LastInsertRowid = rowIdValue is long rowId ? rowId : null,
                };
            }
            throw new NotSupportedException("Tipo de consulta SQLite SessionFs não suportado.");
        }

        private void EnsureParentDirectories(string path)
        {
            var parent = Parent(path);
            var current = "/";
            var missing = new List<string>();
            foreach (var part in parent.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                current = current == "/" ? "/" + part : current + "/" + part;
                if (_files.ContainsKey(current)) throw new IOException("Um componente do caminho SessionFs é um arquivo.");
                if (!_directories.ContainsKey(current)) missing.Add(current);
            }

            EnsureDirectoryCapacity(missing.Count);
            var now = DateTimeOffset.UtcNow;
            foreach (var directory in missing) _directories.Add(directory, now);
        }

        private FileEntry CreateFileEntry(string path, string content, DateTimeOffset? birthtime = null, DateTimeOffset? previousMtime = null)
        {
            var size = System.Text.Encoding.UTF8.GetByteCount(content);
            if (size > MaxFileBytes) throw new IOException($"Arquivo SessionFs excede o limite de {MaxFileBytes} bytes.");
            var hasExisting = _files.TryGetValue(path, out var existing);
            if (!hasExisting && _files.Count >= MaxSessionFiles)
                throw new IOException($"Sessão SessionFs excede o limite de {MaxSessionFiles} arquivos.");
            var previousSize = hasExisting ? existing!.Size : 0;
            if (_fileBytes - previousSize + size > MaxSessionFileBytes)
                throw new IOException($"Sessão SessionFs excede o limite de {MaxSessionFileBytes} bytes.");
            _fileBytes = _fileBytes - previousSize + size;
            var now = DateTimeOffset.UtcNow;
            if (previousMtime is { } mtime && now <= mtime) now = mtime.AddTicks(1);
            return new FileEntry(content, birthtime ?? now, now);
        }

        private void AddDirectory(string path, DateTimeOffset created)
        {
            if (_directories.ContainsKey(path)) return;
            EnsureDirectoryCapacity(1);
            _directories.Add(path, created);
        }

        private void EnsureDirectoryCapacity(int additional)
        {
            if (additional > MaxSessionDirectories - _directories.Count)
                throw new IOException($"Sessão SessionFs excede o limite de {MaxSessionDirectories} diretórios.");
        }

        private IEnumerable<(string Name, bool IsDirectory)> GetEntries(string directory)
        {
            return _directories.Keys.Where(path => !_pathComparer.Equals(path, directory) && _pathComparer.Equals(Parent(path), directory))
                .Select(path => (Name(path), true))
                .Concat(_files.Keys.Where(path => _pathComparer.Equals(Parent(path), directory)).Select(path => (Name(path), false)));
        }

        private static string Parent(string path)
        {
            if (path == "/") return "/";
            var index = path.LastIndexOf('/');
            return index <= 0 ? "/" : path[..index];
        }

        private static string Name(string path) => path[(path.LastIndexOf('/') + 1)..];
        private bool IsWithin(string path, string parent) => path.StartsWith(parent + "/",
            _pathComparer == StringComparer.OrdinalIgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        private static string ReplacePrefix(string path, string source, string destination) => destination + path[source.Length..];
        private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    }
}

internal sealed class CopilotVolatileSessionFsProvider(CopilotVolatileSessionFsStore.SessionState state)
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
    public Task<SessionFsSqliteResult?> QueryAsync(SessionFsSqliteQueryType queryType, string query, IDictionary<string, object?>? bindParams, CancellationToken cancellationToken) => state.QuerySqliteAsync(queryType, query, bindParams, cancellationToken);
    public Task<bool> ExistsAsync(CancellationToken cancellationToken) => state.SqliteExistsAsync(cancellationToken);
    public Task<IList<SessionFsSqliteResult>> TransactionAsync(IList<SessionFsSqliteStatement> statements, CancellationToken cancellationToken) => state.ExecuteTransactionAsync(statements, cancellationToken);
}

#pragma warning restore GHCP001
