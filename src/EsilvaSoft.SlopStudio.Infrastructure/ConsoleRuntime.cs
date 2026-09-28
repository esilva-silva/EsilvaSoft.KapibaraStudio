using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Acornima;
using Acornima.Ast;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Autocomplete.Core;
using EsilvaSoft.SlopStudio.Core;
using Jint;
using MongoDB.Bson;

namespace EsilvaSoft.SlopStudio.Infrastructure;

public sealed class ConsoleRuntime(IConnectionProfileRepository profiles, IEnvironmentVaultRepository environments,
    IConnectionSecretStore secrets, IConsoleDatabaseSessionFactory sessions, IConsoleHistoryRepository history,
    IAuditRepository audit, IMetadataInvalidationBus? metadata = null, ISecretStore? credentialStore = null) : IConsoleRuntime
{
    private Func<string, string>? _localize;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly HashSet<string> ReadMethods = ["find", "findOne", "aggregate", "countDocuments", "estimatedDocumentCount", "distinct", "stats", "databaseStats"];
    private static readonly HashSet<string> WriteMethods = ["insertOne", "insertMany", "updateOne", "updateMany", "replaceOne", "deleteOne", "deleteMany", "drop", "dropDatabase", "createIndex", "dropIndex", "createCollection"];
    private static readonly string Bootstrap = ReadBootstrap();
    public void SetLocalization(Func<string, string> localize)
    {
        _localize = localize ?? throw new ArgumentNullException(nameof(localize));
        sessions.SetLocalization(localize);
    }
    private string L(string key, string fallback) => _localize?.Invoke(key) ?? fallback;
    public ConsoleStatement GetStatement(string script, int caret)
    {
        var program = new Parser().ParseScript(script);
        var statement = program.Body.FirstOrDefault(s => s.Start <= caret && caret <= s.End);
        return statement is null ? new(0, 0) : new(statement.Start, statement.End - statement.Start);
    }

    public async Task<ConsoleExecutionResult> ExecuteAsync(ConsoleRequest request,
        Func<ConsoleWriteConfirmation, CancellationToken, Task<bool>> confirmWrite, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Script.Length > 1_000_000 || string.IsNullOrWhiteSpace(request.Database)) throw new ArgumentException(L("consoleDatabaseAndScriptLimit", "Informe banco e script de até 1 MB."));
        if (request.DocumentLimit is < 1 or > 1000 || request.TimeoutMs is < 1 or > 300000) throw new ArgumentException(L("consoleDocumentLimit", "Limite: 1–1000 documentos; timeout: 1–300000 ms."));
        // Capture the environment and process values before the first await; additional profiles use this same snapshot.
        var environment = new OperationEnvironment(environments, null, request.Primary.Id);
        var values = environment.ScriptValues.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        var started = Stopwatch.GetTimestamp();
        var occurredAt = DateTimeOffset.UtcNow;
        var output = new List<ConsoleResultSet>(); var messages = new StringBuilder(); var used = new HashSet<Guid>();
        string? error = null; var canceled = false; var timedOut = false;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(request.TimeoutMs);
        var token = timeout.Token;
        try
        {
            var saved = await profiles.GetAllAsync(token).ConfigureAwait(false);
            var captured = saved.Where(p => p.Id != request.Primary.Id).Append(request.Primary).ToArray();
            // Resolve every URI without DNS/network. The session resolves routing lazily when a connection is used.
            var resolutionErrors = new Dictionary<Guid, string>();
            var resolved = new List<ConnectionProfile>(captured.Length);
            foreach (var p in captured)
            {
                var password = secrets.GetPassword(p.Id);
                string? Legacy(string key) => key == "MONGODB_PASSWORD" && password is not null ? password : environment.GetLegacy(key);
                try
                {
                    var source = await OperationEnvironment.ResolveStoredConnectionUriAsync(p, credentialStore, token)
                        .ConfigureAwait(false);
                    resolved.Add(p with { ConnectionString = (p with { ConnectionString = source })
                        .ResolveConnectionString(Legacy, key => environment.Get(key)) });
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception ex) { resolutionErrors[p.Id] = ex.Message; resolved.Add(p); }
            }
            using var session = sessions.Create(resolved, request.DocumentLimit, request.TimeoutMs);
            await Task.Run(() =>
            {
                using var engine = new Engine(o => o.LimitMemory(64_000_000).MaxStatements(1_000_000)
                    .LimitRecursion(128).TimeoutInterval(TimeSpan.FromMilliseconds(request.TimeoutMs)).CancellationToken(token));
                var capture = "__result_" + Guid.NewGuid().ToString("N");
                // Driver replies of document reads, kept so an unmodified result keeps the server field order after the JavaScript round trip.
                var replies = new List<string?>();
                var retained = 0L;
                var program = new Parser().ParseScript(request.Script);
                var transformed = new StringBuilder(request.Script);
                foreach (var expression in program.Body.OfType<ExpressionStatement>().Reverse())
                {
                    transformed.Insert(expression.Expression.End, "))");
                    transformed.Insert(expression.Expression.Start, capture + "((");
                }
                engine.SetValue("__hostCall", new Func<string, string>(payload =>
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        var operation = JsonSerializer.Deserialize<ConsoleOperation>(payload, JsonOptions) ?? throw new InvalidOperationException(L("consoleInvalidOperation", "Operação inválida."));
                        var profile = resolved.Single(p => p.Id == operation.ProfileId);
                        if (resolutionErrors.TryGetValue(profile.Id, out var resolutionError)) throw new InvalidOperationException(resolutionError);
                        if (!ReadMethods.Contains(operation.Method) && !WriteMethods.Contains(operation.Method)) throw new InvalidOperationException(L("consoleUnsupportedMethod", "Método não suportado."));
                        used.Add(profile.Id);
                        var write = WriteMethods.Contains(operation.Method);
                        ConsoleDatabaseSession.Validate(operation, MongoDB.Bson.Serialization.BsonSerializer.Deserialize<MongoDB.Bson.BsonArray>(operation.ArgumentsJson), profile, _localize);
                        if (write)
                        {
                            profile.EnsureWriteAllowed();
                            if (!confirmWrite(new(profile, operation.Database, operation.Collection, operation.Method, operation.Method is "deleteOne" or "deleteMany" ? MongoDB.Bson.Serialization.BsonSerializer.Deserialize<MongoDB.Bson.BsonArray>(operation.ArgumentsJson)[0].ToJson() : null), token).WaitAsync(token).GetAwaiter().GetResult())
                                throw new InvalidOperationException(L("consoleWriteNotConfirmed", "Operação de escrita não confirmada."));
                            audit.SaveAsync(AuditEntry.Create("console." + operation.Method, profile.Id, operation.Database, operation.Collection, "Envio confirmado; conclusão ainda não conhecida."), token).GetAwaiter().GetResult();
                        }
                        var result = session.ExecuteAsync(operation, token).GetAwaiter().GetResult();
                        if (write)
                        {
                            audit.SaveAsync(AuditEntry.Create("console." + operation.Method, profile.Id, operation.Database, operation.Collection, "Operação concluída."), token).GetAwaiter().GetResult();
                            foreach (var invalidation in MetadataInvalidations(profile.Id, operation)) metadata?.Publish(invalidation);
                        }
                        if (result.Length > 8_000_000) throw new InvalidOperationException(L("consoleResultTooLarge", "Resultado excede 8 MB."));
                        var keep = operation.Method is "find" or "findOne" or "aggregate" && retained + result.Length <= 8_000_000;
                        if (keep) retained += result.Length;
                        replies.Add(keep ? result : null);
                        return "{\"reply\":" + (replies.Count - 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"result\":" + result + "}";
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) { return JsonSerializer.Serialize(new { error = OperationErrorMessages.Describe(ex, localize: _localize) }); }
                }));
                var size = 0;
                engine.SetValue("__hostOutput", new Action<string>(json =>
                {
                    size += json.Length;
                    if (size > 8_000_000 || output.Count >= 100) throw new InvalidOperationException(L("consoleOutputLimit", "Saída limitada a 100 resultados / 8 MB."));
                    using var document = JsonDocument.Parse(json);
                    var root = document.RootElement; var value = root.GetProperty("value");
                    Guid? id = null; string? db = null, collection = null, method = null; var truncated = false; var projected = false; string? rawReply = null;
                    if (root.TryGetProperty("source", out var source) && source.ValueKind == JsonValueKind.Object)
                    {
                        id = source.GetProperty("profileId").GetGuid(); db = source.GetProperty("database").GetString(); collection = source.GetProperty("collection").GetString();
                        truncated = source.TryGetProperty("truncated", out var t) && t.GetBoolean();
                        method = source.TryGetProperty("method", out var m) ? m.GetString() : null;
                        projected = source.TryGetProperty("projected", out var p) && p.ValueKind == JsonValueKind.True;
                        if (source.TryGetProperty("reply", out var r) && r.TryGetInt32(out var index) && index >= 0 && index < replies.Count) rawReply = replies[index];
                    }
                    using var reply = rawReply is null ? null : JsonDocument.Parse(rawReply);
                    // JavaScript enumerates integer-like keys first. An equivalent value uses the driver text; a value changed by the script keeps its own.
                    if (reply is not null && reply.RootElement.TryGetProperty("value", out var original) && ExtendedJsonComparer.AreEquivalent(value, original, ignoreObjectOrder: true))
                        value = original;
                    IReadOnlyList<string>? documents = method is "find" or "aggregate" && value.ValueKind == JsonValueKind.Array
                        ? value.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.Object).Select(v => v.GetRawText()).ToArray()
                        : method == "findOne" && value.ValueKind == JsonValueKind.Object ? [value.GetRawText()] : null;
                    output.Add(new(output.Count + 1, value.GetRawText(), id, db, collection, documents, truncated)
                        { SourceProfile = captured.FirstOrDefault(p => p.Id == id), Method = method, IsProjected = projected });
                }));
                engine.SetValue("__hostMessage", new Action<string>(message => { if (messages.Length + message.Length > 1_000_000) throw new InvalidOperationException(L("consoleMessagesTooLarge", "Mensagens excedem 1 MB.")); messages.AppendLine(message); }));
                engine.SetValue("__hostEnvironment", new Func<string, string>(key => values.TryGetValue(key, out var value)
                    ? JsonSerializer.Serialize(new { value }) : JsonSerializer.Serialize(new { error = L("consoleEnvironmentKeyMissing", "Chave não definida: ") + key })));
                engine.SetValue("__hostUuid", new Func<string, string, string>((name, text) =>
                {
                    try
                    {
                        return UuidCodec.TryParseConstructor(name, out var representation)
                            ? "{\"value\":" + UuidCodec.ToExtendedJson(UuidCodec.ParseText(name, text), representation) + "}"
                            : JsonSerializer.Serialize(new { error = L("consoleUnknownUuidConstructor", "Construtor UUID desconhecido.") });
                    }
                    catch (FormatException ex) { return JsonSerializer.Serialize(new { error = ex.Message }); }
                }));
                engine.SetValue("__hostUuidJson", new Func<string, string>(text =>
                {
                    try { return JsonSerializer.Serialize(new { value = IdentifierRepresentationService.RewriteConstructors(text) }); }
                    catch (FormatException ex) { return JsonSerializer.Serialize(new { error = ex.Message }); }
                }));
                engine.Execute("var __profiles = " + JsonSerializer.Serialize(captured.Select(p => new { id = p.Id, name = p.Name })) + ";");
                engine.SetValue("__primary", request.Primary.Id.ToString()); engine.SetValue("__database", request.Database);
                engine.SetValue("__captureName", capture); engine.SetValue("__maxDocuments", request.DocumentLimit);
                engine.Execute(Bootstrap);
                engine.Execute(transformed.ToString());
            }, token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            canceled = cancellationToken.IsCancellationRequested;
            timedOut = !canceled && (token.IsCancellationRequested || ex is TimeoutException || ex.GetType().Name == "TimeoutException");
            error = canceled ? L("consoleExecutionCancelled", "Execução interrompida; efeitos no servidor não são revertidos.") : timedOut
                ? L("consoleExecutionTimedOut", "Tempo limite excedido; efeitos enviados ao servidor não são revertidos.") : ex.Message;
        }
        var duration = Stopwatch.GetElapsedTime(started);
        if (request.SaveHistory)
        {
            try
            {
                await history.SaveConsoleHistoryAsync(new(1, Guid.NewGuid(), occurredAt, request.Primary.Id, request.Primary.Name,
                    request.Database, environment.Vault.Name, request.Script, duration.TotalMilliseconds,
                    canceled ? "Cancelado" : timedOut ? "Tempo limite" : error is null ? "Concluído" : "Erro", used.ToArray()) { TargetHost = request.Primary.TargetHost, DocumentLimit = request.DocumentLimit }, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) { messages.AppendLine(L("consoleHistoryNotSaved", "Histórico não salvo: ") + ex.Message); }
        }
        return new(output, messages.ToString(), error, duration, canceled, used.ToArray(), environment.Vault.Name) { IsTimedOut = timedOut };
    }

    /// <summary>Metadata affected by a completed Console write. Inserts and updates can create collections implicitly.</summary>
    internal static IReadOnlyList<MetadataInvalidation> MetadataInvalidations(Guid profileId, ConsoleOperation operation) => operation.Method switch
    {
        "drop" or "createCollection" => [new(profileId, MetadataChange.Collections, operation.Database, operation.Collection)],
        "dropDatabase" => [new(profileId, MetadataChange.Databases, operation.Database)],
        "createIndex" or "dropIndex" => [new(profileId, MetadataChange.Indexes, operation.Database, operation.Collection)],
        "insertOne" or "insertMany" or "updateOne" or "updateMany" or "replaceOne" =>
        [
            new(profileId, MetadataChange.Databases, Strength: InvalidationStrength.Soft),
            new(profileId, MetadataChange.Collections, operation.Database, Strength: InvalidationStrength.Soft)
        ],
        _ => []
    };

    private static string ReadBootstrap()
    {
        using var stream = typeof(ConsoleRuntime).Assembly.GetManifestResourceStream("EsilvaSoft.SlopStudio.Infrastructure.ConsoleBootstrap.js")!;
        using var reader = new StreamReader(stream); return reader.ReadToEnd();
    }
}
