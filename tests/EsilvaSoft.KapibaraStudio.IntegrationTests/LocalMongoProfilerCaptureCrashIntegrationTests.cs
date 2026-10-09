using System.Diagnostics;
using System.Reflection;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Infrastructure;
using EsilvaSoft.KapibaraStudio.SystemAdapters;
using MongoDB.Driver;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

[TestFixture]
[Category("Integration")]
[NonParallelizable]
public sealed class LocalMongoProfilerCaptureCrashIntegrationTests
{
    private const string OptInVariable = "KAPIBARA_F6_LIVE_MONGO";
    private const string ConnectionVariable = "KAPIBARA_F6_MONGO_URI";
    private const string WorkerModeVariable = "KAPIBARA_F6_PROFILER_CRASH_WORKER";
    private const string WorkspaceVariable = "KAPIBARA_F6_PROFILER_CRASH_WORKSPACE";
    private const string ReadyVariable = "KAPIBARA_F6_PROFILER_CRASH_READY";
    private const string ProfileIdVariable = "KAPIBARA_F6_PROFILER_CRASH_PROFILE_ID";
    private const string DatabaseVariable = "KAPIBARA_F6_PROFILER_CRASH_DATABASE";
    private const string SampleDatabaseName = "sample_mflix";

    [Test]
    public async Task ProcessCrashAfterMongoDispatchLeavesPreparedReceiptAndRecoveryRestoresProfiler()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionVariable);
        if (!string.Equals(Environment.GetEnvironmentVariable(OptInVariable), "1", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(connectionString))
            Assert.Ignore($"Teste destrutivo opt-in: defina {OptInVariable}=1 e {ConnectionVariable}; nenhum banco foi acessado.");

        var mongoUrl = MongoUrl.Create(connectionString);
        Assert.That(mongoUrl.DatabaseName, Is.EqualTo(SampleDatabaseName), "A URI precisa apontar para sample_mflix.");
        Assert.That(mongoUrl.Username, Is.Null.Or.Empty, "O preflight aceita somente autenticação local sem usuário/senha.");
        Assert.That(mongoUrl.Password, Is.Null.Or.Empty, "O preflight aceita somente autenticação local sem usuário/senha.");
        Assert.That(mongoUrl.Server.Host, Is.AnyOf("localhost", "127.0.0.1", "::1"),
            "Este teste aceita somente MongoDB local em loopback.");

        var fixtureDatabase = "f6_profiler_crash_" + Guid.NewGuid().ToString("N");
        var client = new MongoClient(connectionString);
        var databasesBefore = await client.ListDatabaseNames().ToListAsync();
        Assert.That(databasesBefore, Does.Not.Contain(fixtureDatabase), "A base temporária já existe; nenhuma escrita foi iniciada.");
        var profileId = Guid.NewGuid();
        var profile = CreateProfile(profileId, connectionString, fixtureDatabase);
        var mongo = CreateWorkspace("parent", Guid.NewGuid().ToString("N"));
        var previousStatusJson = await mongo.GetProfilerStatusAsync(profile, fixtureDatabase);
        var previous = ProfilerSettings.Parse(previousStatusJson);
        var topology = await mongo.GetTopologyAsync(profile);
        Assert.That(ProfilerSettings.IsMongos(topology), Is.False, "O teste requer conexão direta com mongod.");
        Assert.That(previous.Level, Is.Zero, "O teste não altera uma configuração de profiler preexistente.");
        Assert.That(previous.FilterDigest, Is.Null, "O teste não altera profiler com filtro ativo.");
        Assert.That(previous.SlowMs, Is.Not.Null.And.GreaterThan(0), "O estado anterior precisa ser restaurável.");
        Assert.That(previous.SampleRate, Is.Not.Null.And.InRange(0m, 1m), "O estado anterior precisa ser restaurável.");
        var databaseAfterSnapshot = await client.ListDatabaseNames().ToListAsync();
        Assert.That(databaseAfterSnapshot, Does.Not.Contain(fixtureDatabase),
            "A leitura inicial não deve criar a base temporária antes da fixture começar.");
        TestContext.Progress.WriteLine($"Snapshot F6-18: URI loopback/auth local validada; {fixtureDatabase} ausente antes da mutação; profiler anterior was={previous.Level}, slowms={previous.SlowMs}, sampleRate={previous.SampleRate}; nenhum documento da fixture foi lido.");

        var marker = Guid.NewGuid().ToString("N");
        var root = Path.Combine(Path.GetTempPath(), "KapibaraStudio.F6.ProfilerCrash", marker);
        Directory.CreateDirectory(root);
        var workspacePath = Path.Combine(root, "workspace.db");
        var readyPath = Path.Combine(root, "mongo-dispatched.txt");
        Process? process = null;
        var recoveryVerified = false;
        ProfilerCaptureTicket? recoveryTicket = null;
        try
        {
            process = StartWorker(workspacePath, readyPath, profileId, connectionString, fixtureDatabase);
            var deadline = DateTimeOffset.UtcNow.AddSeconds(60);
            while (!File.Exists(readyPath) && !process.HasExited && DateTimeOffset.UtcNow < deadline)
                await Task.Delay(25);

            Assert.That(File.Exists(readyPath), Is.True,
                "O worker não confirmou readback do comando de profiler antes do deadline.");
            Assert.That(await File.ReadAllTextAsync(readyPath), Is.EqualTo(profileId.ToString("N")));
            Assert.That(process.HasExited, Is.False, "O worker deve estar parado após a mutação e antes da persistência Active.");

            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            Assert.That(process.ExitCode, Is.Not.EqualTo(0), "A terminação forçada deve interromper o worker abruptamente.");

            using (var reopened = new LiteDbConnectionProfileRepository(workspacePath))
            {
                var pending = await ((IProfilerCaptureRepository)reopened).GetPendingAsync();
                Assert.That(pending, Has.Count.EqualTo(1));
                Assert.Multiple(() =>
                {
                    Assert.That(pending[0].ProfileId, Is.EqualTo(profileId));
                    Assert.That(pending[0].Database, Is.EqualTo(fixtureDatabase));
                    Assert.That(pending[0].State, Is.EqualTo(ProfilerCaptureState.Prepared));
                    Assert.That(pending[0].Previous, Is.EqualTo(previous));
                    Assert.That(pending[0].Applied.Level, Is.EqualTo(1));
                });
            }

            var ticketId = await FindOwnedTicketIdAsync(workspacePath, profileId);
            ProfilerCaptureTicket ticket;
            using (var snapshotOwner = CreateRepository(workspacePath))
                ticket = (await ((IProfilerCaptureRepository)snapshotOwner).GetPendingAsync()).Single(item => item.Id == ticketId);
            recoveryTicket = ticket;
            using (var recoveryOwner = CreateRepository(workspacePath))
                await new ProfilerCaptureCoordinator(
                    (IProfilerCaptureRepository)recoveryOwner, mongo).RestoreAsync(profile, ticketId, fixtureDatabase);
            var restored = ProfilerSettings.Parse(await mongo.GetProfilerStatusAsync(profile, fixtureDatabase));
            Assert.That(restored, Is.EqualTo(previous));
            try
            {
                await DropOwnedFixtureDatabaseAsync(client, fixtureDatabase);
            }
            catch
            {
                await PreserveReceiptAsync(workspacePath, ticket);
                throw;
            }
            using (var reopened = new LiteDbConnectionProfileRepository(workspacePath))
                Assert.That(await ((IProfilerCaptureRepository)reopened).GetPendingAsync(), Is.Empty);
            recoveryVerified = true;
        }
        finally
        {
            if (process is { HasExited: false })
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            }

            if (!recoveryVerified && File.Exists(workspacePath))
            {
                try
                {
                    var ticketId = await FindOwnedTicketIdAsync(workspacePath, profileId);
                    using (var snapshotOwner = CreateRepository(workspacePath))
                        recoveryTicket = (await ((IProfilerCaptureRepository)snapshotOwner).GetPendingAsync())
                            .Single(item => item.Id == ticketId);
                    using (var recoveryOwner = CreateRepository(workspacePath))
                        await new ProfilerCaptureCoordinator(
                            (IProfilerCaptureRepository)recoveryOwner, mongo).RestoreAsync(profile, ticketId, fixtureDatabase);
                    recoveryVerified = ProfilerSettings.Parse(await mongo.GetProfilerStatusAsync(profile, fixtureDatabase)) == previous;
                    if (recoveryVerified)
                        await DropOwnedFixtureDatabaseAsync(client, fixtureDatabase);
                }
                catch
                {
                    if (recoveryTicket is not null)
                    {
                        try { await PreserveReceiptAsync(workspacePath, recoveryTicket); }
                        catch (Exception receiptFailure)
                        {
                            TestContext.Progress.WriteLine($"Não foi possível preservar o receipt de recuperação: {receiptFailure.GetType().Name}");
                        }
                    }
                    else if (!File.Exists(readyPath))
                    {
                        recoveryVerified = await VerifyBaselineAndDropAsync(mongo, profile, previous, client, fixtureDatabase);
                    }
                }
            }

            process?.Dispose();
            if (recoveryVerified && Directory.Exists(root))
                Directory.Delete(root, recursive: true);
            else
                TestContext.Progress.WriteLine($"Recibo de recuperação preservado para inspeção em: {workspacePath}");
        }
    }

    [Test]
    public async Task CrashWorkerPausesAfterVerifiedMongoMutationBeforeActiveReceiptWrite()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable(WorkerModeVariable), "1", StringComparison.Ordinal))
            Assert.Ignore("Worker interno: iniciado apenas pelo teste pai de crash do profiler.");

        var connectionString = Environment.GetEnvironmentVariable(ConnectionVariable)
            ?? throw new InvalidOperationException("URI Mongo do worker não informada.");
        var workspacePath = Environment.GetEnvironmentVariable(WorkspaceVariable)
            ?? throw new InvalidOperationException("Caminho LiteDB do worker não informado.");
        var readyPath = Environment.GetEnvironmentVariable(ReadyVariable)
            ?? throw new InvalidOperationException("Handshake do worker não informado.");
        var databaseName = Environment.GetEnvironmentVariable(DatabaseVariable)
            ?? throw new InvalidOperationException("Nome do banco sintético do worker não informado.");
        var profileId = Guid.ParseExact(Environment.GetEnvironmentVariable(ProfileIdVariable)
            ?? throw new InvalidOperationException("ID da conexão do worker não informado."), "N");
        var profile = CreateProfile(profileId, connectionString, databaseName);
        var mongo = CreateWorkspace("worker", profileId.ToString("N"));
        var repository = CreateRepository(workspacePath);
        var pausingMongo = DispatchProxy.Create<IMongoWorkspaceService, PauseAfterProfilerDispatchProxy>();
        ((PauseAfterProfilerDispatchProxy)pausingMongo).Inner = mongo;
        ((PauseAfterProfilerDispatchProxy)pausingMongo).ReadyPath = readyPath;
        var coordinator = new ProfilerCaptureCoordinator(repository, pausingMongo);
        var beforeJson = await mongo.GetProfilerStatusAsync(profile, databaseName);
        var topologyJson = await mongo.GetTopologyAsync(profile);
        var before = ProfilerSettings.Parse(beforeJson);
        var request = new ProfilerConfigurationRequest(databaseName, 1, 100, 1m,
            ProfilerFilterMode.Unset, null, databaseName, beforeJson, topologyJson);

        _ = await coordinator.StartAsync(profile, request, TimeSpan.FromMinutes(1));
        Assert.Fail("O worker deveria permanecer bloqueado depois do dispatch até o processo pai terminá-lo.");
    }

    private static Process StartWorker(string workspacePath, string readyPath, Guid profileId, string connectionString,
        string databaseName)
    {
        var assemblyPath = typeof(LocalMongoProfilerCaptureCrashIntegrationTests).Assembly.Location;
        var startInfo = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } host
            ? host : "dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("vstest");
        startInfo.ArgumentList.Add(assemblyPath);
        startInfo.ArgumentList.Add("--TestCaseFilter:FullyQualifiedName~CrashWorkerPausesAfterVerifiedMongoMutationBeforeActiveReceiptWrite");
        startInfo.ArgumentList.Add("--logger:console;verbosity=minimal");
        startInfo.Environment[WorkerModeVariable] = "1";
        startInfo.Environment[ConnectionVariable] = connectionString;
        startInfo.Environment[WorkspaceVariable] = workspacePath;
        startInfo.Environment[ReadyVariable] = readyPath;
        startInfo.Environment[ProfileIdVariable] = profileId.ToString("N");
        startInfo.Environment[DatabaseVariable] = databaseName;
        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Não foi possível iniciar o worker de crash do profiler.");
        _ = process.StandardOutput.ReadToEndAsync();
        _ = process.StandardError.ReadToEndAsync();
        return process;
    }

    private static ConnectionProfile CreateProfile(Guid id, string connectionString, string databaseName) =>
        new(id, "F6 profiler crash synthetic", connectionString, databaseName);

    private static MongoWorkspaceService CreateWorkspace(string directory, string marker) =>
        new(new LocalMongoDatabaseExportFileAccess(Path.Combine(Path.GetTempPath(), "KapibaraStudio.F6.ProfilerCrash", directory, marker)));

    private static LiteDbConnectionProfileRepository CreateRepository(string path) => new(path);

    private static async Task<Guid> FindOwnedTicketIdAsync(string workspacePath, Guid profileId)
    {
        using var owner = new LiteDbConnectionProfileRepository(workspacePath);
        var pending = await ((IProfilerCaptureRepository)owner).GetPendingAsync();
        return pending.Single(ticket => ticket.ProfileId == profileId).Id;
    }

    private static async Task PreserveReceiptAsync(string workspacePath, ProfilerCaptureTicket ticket)
    {
        using var owner = CreateRepository(workspacePath);
        var repository = owner;
        var pending = await repository.GetPendingAsync();
        if (pending.Any(item => item.Id == ticket.Id))
            return;
        await repository.CreateAsync(ticket with { State = ProfilerCaptureState.Uncertain });
    }

    private static async Task DropOwnedFixtureDatabaseAsync(MongoClient client, string databaseName)
    {
        var databaseNames = await client.ListDatabaseNames().ToListAsync();
        if (!databaseNames.Contains(databaseName, StringComparer.Ordinal))
            return;
        var collectionNames = await client.GetDatabase(databaseName).ListCollectionNames().ToListAsync();
        if (collectionNames.Any(name => name != "system.profile"))
            throw new InvalidOperationException("A limpeza foi recusada porque o banco sintético contém namespaces inesperados.");
        await client.DropDatabaseAsync(databaseName);
        var remainingNames = await client.ListDatabaseNames().ToListAsync();
        if (remainingNames.Contains(databaseName, StringComparer.Ordinal))
            throw new InvalidOperationException("O banco sintético ainda existe após dropDatabase.");
    }

    private static async Task<bool> VerifyBaselineAndDropAsync(MongoWorkspaceService mongo, ConnectionProfile profile,
        ProfilerSettings previous, MongoClient client, string databaseName)
    {
        var current = ProfilerSettings.Parse(await mongo.GetProfilerStatusAsync(profile, databaseName));
        if (current != previous)
            return false;
        await DropOwnedFixtureDatabaseAsync(client, databaseName);
        return true;
    }

    public class PauseAfterProfilerDispatchProxy : DispatchProxy
    {
        public IMongoWorkspaceService Inner { get; set; } = null!;
        public string ReadyPath { get; set; } = string.Empty;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name != nameof(IMongoWorkspaceService.ConfigureProfilerAsync))
                return targetMethod!.Invoke(Inner, args);
            return PauseAfterDispatchAsync((ConnectionProfile)args![0]!,
                (ProfilerConfigurationRequest)args[1]!, (CancellationToken)args[2]!);
        }

        private async Task<ProfilerConfigurationResult> PauseAfterDispatchAsync(ConnectionProfile profile,
            ProfilerConfigurationRequest request, CancellationToken cancellationToken)
        {
            var result = await Inner.ConfigureProfilerAsync(profile, request, cancellationToken);
            await File.WriteAllTextAsync(ReadyPath, profile.Id.ToString("N"), CancellationToken.None);
            await Task.Delay(Timeout.InfiniteTimeSpan, CancellationToken.None);
            return result;
        }
    }
}
