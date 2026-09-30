using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.AiContext;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Facts;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;

namespace EsilvaSoft.KapibaraStudio.Benchmarks.Ai;

/// <summary>
/// Origem de um número deste relatório. Nunca é decorativa: fake determinístico e modelo real não são a mesma
/// categoria de evidência e não podem ser somados, comparados nem citados como se fossem.
/// </summary>
public enum AiRuntimeEvidence
{
    /// <summary>
    /// Runtime falso com latência programada. Prova <em>lógica</em> — fila, prioridade, preempção, cooldown, limite
    /// de espera — e absolutamente nada sobre o custo de inferência de um modelo de verdade.
    /// </summary>
    DeterministicFake,

    /// <summary>
    /// Pesos ONNX reais em hardware real. É a única origem que autoriza afirmar TTFT, tokens/s ou working set de um
    /// pacote; vale só para a máquina descrita em <see cref="AiRuntimeReport.Environment"/>.
    /// </summary>
    RealModel
}

/// <summary>Em qual fase um caso falhou, quando falhou.</summary>
public enum AiRuntimeOutcome
{
    /// <summary>O caso produziu texto e medições.</summary>
    Generated,

    /// <summary>O contexto mínimo não coube no orçamento; o modelo não chegou a ser chamado.</summary>
    BudgetExhausted,

    /// <summary>O serviço de modelo recusou (pacote, capacidade, cooldown, janela) ou a geração falhou.</summary>
    Refused,

    /// <summary>A geração terminou sem nenhum token.</summary>
    Empty
}

/// <summary>
/// Um contexto de editor a medir: a dimensão "contexto" da matriz modelo × hardware × contexto × geração.
/// </summary>
/// <param name="Id">Identificador estável do caso, usado no relatório.</param>
/// <param name="EditorText">Texto da aba no momento do <c>Ctrl+;</c>.</param>
/// <param name="Caret">Posição do cursor dentro de <paramref name="EditorText"/>.</param>
public sealed record AiRuntimeScenario(string Id, string EditorText, int Caret)
{
    /// <summary>Janela de contexto pedida ao modelo, em tokens.</summary>
    public int ContextTokens { get; init; } = 2048;

    /// <summary>Reserva de geração, em tokens: a dimensão "geração" da matriz.</summary>
    public int MaximumTokens { get; init; } = 32;

    /// <summary>Campos do painel de resultado visíveis na aba.</summary>
    public IReadOnlyList<string> ResultFields { get; init; } = [];

    /// <summary>Nomes conhecidos oferecidos ao contrato de contexto.</summary>
    public IReadOnlyList<string> KnownNames { get; init; } = [];

    /// <summary>Comandos recentes do console.</summary>
    public IReadOnlyList<string> RecentCommands { get; init; } = [];

    /// <summary>Aba capturada equivalente, como a interface a produziria.</summary>
    public AutocompleteContextSnapshot ToSnapshot() =>
        new(EditorText, Math.Clamp(Caret, 0, EditorText.Length), Language, "", ResultFields, KnownNames, RecentCommands);

    /// <summary>Linguagem declarada ao contrato; o console é sempre mongosh.</summary>
    public const string Language = "Mongo Console JavaScript";

    /// <summary>
    /// Conjunto padrão: três tamanhos de contexto cruzados com duas reservas de geração. Pequeno o bastante para
    /// caber numa sessão de medição manual e variado o bastante para separar custo de prefill de custo de decode.
    /// </summary>
    public static IReadOnlyList<AiRuntimeScenario> CreateDefault()
    {
        var scenarios = new List<AiRuntimeScenario>();
        foreach (var (id, text, caret, fields, names, history) in Bases())
            foreach (var generation in (int[])[32, 96])
                scenarios.Add(new(id + "-g" + generation.ToString(CultureInfo.InvariantCulture), text, caret)
                {
                    ContextTokens = 2048,
                    MaximumTokens = generation,
                    ResultFields = fields,
                    KnownNames = names,
                    RecentCommands = history
                });
        return scenarios;
    }

    private static IEnumerable<(string Id, string Text, int Caret, string[] Fields, string[] Names, string[] History)> Bases()
    {
        const string small = "db.getCollection(\"customers\").find({ ";
        yield return ("curto", small, small.Length, [], [], []);

        var medium = new StringBuilder()
            .AppendLine("// Relatório mensal de pedidos")
            .AppendLine("const inicio = new Date(\"2026-01-01\");")
            .AppendLine("const fim = new Date(\"2026-02-01\");")
            .AppendLine("db.getCollection(\"orders\").aggregate([")
            .AppendLine("  { $match: { createdAt: { $gte: inicio, $lt: fim } } },")
            .Append("  { $group: { ")
            .ToString();
        yield return ("medio", medium, medium.Length,
            ["_id", "customerId", "total", "status", "createdAt"],
            ["orders", "customers", "products"],
            ["db.orders.countDocuments({})", "db.customers.find({}).limit(5)"]);

        var builder = new StringBuilder();
        builder.AppendLine("// Pipeline de consolidação — não executar em produção sem revisão.");
        for (var index = 0; index < 60; index++)
            builder.Append(CultureInfo.InvariantCulture,
                $"const filtro{index} = {{ status: \"ativo\", regiao: \"BR-{index:D2}\", revisadoEm: null }};\n");
        builder.AppendLine("db.getCollection(\"invoices\").aggregate([");
        builder.AppendLine("  { $match: filtro0 },");
        builder.Append("  { $lookup: { ");
        var large = builder.ToString();
        yield return ("longo", large, large.Length,
            ["_id", "invoiceNumber", "customerId", "amount", "currency", "status", "issuedAt", "paidAt"],
            ["invoices", "customers", "payments", "regions"],
            ["db.invoices.find({ status: \"ativo\" })", "db.payments.aggregate([])", "db.regions.distinct(\"codigo\")"]);
    }
}

/// <summary>Um pacote de modelo a medir e o acelerador pedido.</summary>
/// <param name="PackageId">Nome da pasta do pacote; é o que aparece no relatório.</param>
/// <param name="PackagePath">Caminho absoluto da pasta do pacote.</param>
/// <param name="Hardware">Acelerador pedido; o provider efetivo vem do runtime, não daqui.</param>
public sealed record AiRuntimeTarget(string PackageId, string PackagePath, AiAccelerationMode Hardware)
{
    /// <summary>Identificador do alvo no relatório.</summary>
    public string Id => PackageId + " · " + Hardware;

    /// <summary>Preferências efetivas deste alvo, com a pasta externa e o acelerador pedidos.</summary>
    public AutocompleteSettings Settings(int contextTokens, int maximumTokens) => new()
    {
        ModelPath = PackagePath,
        Acceleration = Hardware,
        ContextTokens = contextTokens,
        MaximumCompletionTokens = maximumTokens
    };
}

/// <summary>
/// Tudo que o harness precisa para medir um alvo: o serviço de modelo compartilhado e o tokenizador do pacote.
/// </summary>
/// <remarks>
/// O harness nunca constrói serviço nem runtime; recebe esta sessão pronta. É o que permite medir o caminho real
/// (<c>LocalAiModelService</c> sobre <c>OnnxLocalModelRuntime</c>) e o caminho falso com exatamente o mesmo código de
/// medição — e, ao mesmo tempo, mantém o harness livre do pacote ONNX.
/// </remarks>
/// <param name="models">Serviço de modelo, com a fila de prioridade e o cooldown reais.</param>
/// <param name="tokenizerFactory">Tokenizador do pacote, para a contagem exata do orçamento.</param>
/// <param name="promptBuilder">Construtor de prompt do formato; sem ele o runtime tokeniza por conta própria.</param>
/// <param name="owned">Recurso extra a liberar junto com a sessão.</param>
public sealed class AiRuntimeSession(ILocalAiModelService models, Func<LocalModelDefinition, ITokenizer> tokenizerFactory,
    ICompletionPromptBuilder? promptBuilder = null, IDisposable? owned = null) : IAsyncDisposable
{
    /// <summary>Serviço de modelo do alvo.</summary>
    public ILocalAiModelService Models { get; } = models ?? throw new ArgumentNullException(nameof(models));

    /// <summary>Tokenizador do pacote.</summary>
    public Func<LocalModelDefinition, ITokenizer> TokenizerFactory { get; } =
        tokenizerFactory ?? throw new ArgumentNullException(nameof(tokenizerFactory));

    /// <summary>Construtor de prompt do formato, quando existe.</summary>
    public ICompletionPromptBuilder? PromptBuilder { get; } = promptBuilder;

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await Models.DisposeAsync().ConfigureAwait(false);
        owned?.Dispose();
    }
}

/// <summary>Uma execução cronometrada de um caso: uma repetição, um alvo, um cenário.</summary>
public sealed record AiRuntimeMeasurement
{
    /// <summary>Alvo medido (pacote e acelerador).</summary>
    public required string TargetId { get; init; }

    /// <summary>Cenário medido.</summary>
    public required string ScenarioId { get; init; }

    /// <summary>Repetição, a partir de zero; as de aquecimento não entram no relatório.</summary>
    public required int Repetition { get; init; }

    /// <summary>Desfecho do caso.</summary>
    public required AiRuntimeOutcome Outcome { get; init; }

    /// <summary>Tokens autorizados do prompt, contados pelo tokenizador do pacote.</summary>
    public int PromptTokens { get; init; }

    /// <summary>Tokens gerados pelo modelo.</summary>
    public int GeneratedTokens { get; init; }

    /// <summary>Montagem do contexto sob orçamento (contrato, fatos, corte, contagem exata), em microssegundos.</summary>
    public double ContextMicroseconds { get; init; }

    /// <summary>Tokenização do prompt final pelo construtor do formato, em microssegundos.</summary>
    public double TokenizationMicroseconds { get; init; }

    /// <summary>
    /// Tempo até o primeiro token relatado pelo runtime; ausente quando o runtime não o mede ou não houve geração.
    /// </summary>
    public double? TimeToFirstTokenMilliseconds { get; init; }

    /// <summary>
    /// Tempo de relógio, medido pelo harness, entre o pedido e o primeiro pedaço com texto. Inclui fila e despacho;
    /// é o número que se parece com o que o usuário espera, e por isso é reportado ao lado do do runtime.
    /// </summary>
    public double ObservedFirstTextMilliseconds { get; init; }

    /// <summary>Tempo total da geração, medido pelo harness, em milissegundos.</summary>
    public double TotalMilliseconds { get; init; }

    /// <summary>Tokens por segundo do trecho de decode, quando houve tokens suficientes para calcular.</summary>
    public double? TokensPerSecond { get; init; }

    /// <summary>Working set do processo ao fim do caso, em bytes.</summary>
    public long WorkingSetBytes { get; init; }

    /// <summary>Provider efetivo relatado pelo runtime.</summary>
    public string Provider { get; init; } = "";

    /// <summary>Mensagem da recusa, quando houve; nunca contém texto do editor.</summary>
    public string Message { get; init; } = "";
}

/// <summary>Resultado agregado de um alvo: um pacote em um acelerador.</summary>
public sealed record AiRuntimeTargetReport
{
    /// <summary>Identificador do alvo.</summary>
    public required string TargetId { get; init; }

    /// <summary>Pacote medido.</summary>
    public required string PackageId { get; init; }

    /// <summary>Acelerador pedido.</summary>
    public required AiAccelerationMode RequestedHardware { get; init; }

    /// <summary>Backend efetivo relatado pelo runtime depois da carga.</summary>
    public required string EffectiveProvider { get; init; }

    /// <summary>Origem da evidência deste alvo.</summary>
    public required AiRuntimeEvidence Evidence { get; init; }

    /// <summary>Tempo de carga do pacote, em milissegundos; zero quando o alvo não chegou a carregar.</summary>
    public required double LoadMilliseconds { get; init; }

    /// <summary>Working set do processo logo depois da carga, em bytes.</summary>
    public required long WorkingSetAfterLoadBytes { get; init; }

    /// <summary>Maior working set observado no processo durante o alvo, em bytes.</summary>
    public required long PeakWorkingSetBytes { get; init; }

    /// <summary>Casos cronometrados.</summary>
    public required int Cases { get; init; }

    /// <summary>Casos que geraram texto.</summary>
    public required int GeneratedCases { get; init; }

    /// <summary>TTFT relatado pelo runtime, em milissegundos.</summary>
    public required AiMetricSummary TimeToFirstToken { get; init; }

    /// <summary>TTFT observado pelo harness (fila inclusa), em milissegundos.</summary>
    public required AiMetricSummary ObservedFirstText { get; init; }

    /// <summary>Tempo total da geração, em milissegundos.</summary>
    public required AiMetricSummary Total { get; init; }

    /// <summary>Tokens por segundo do decode.</summary>
    public required AiMetricSummary TokensPerSecond { get; init; }

    /// <summary>Montagem do contexto, em microssegundos.</summary>
    public required AiMetricSummary ContextMicroseconds { get; init; }

    /// <summary>Tokenização do prompt, em microssegundos.</summary>
    public required AiMetricSummary TokenizationMicroseconds { get; init; }

    /// <summary>Tokens autorizados do prompt.</summary>
    public required AiMetricSummary PromptTokens { get; init; }

    /// <summary>Tokens gerados.</summary>
    public required AiMetricSummary GeneratedTokens { get; init; }

    /// <summary>Working set ao fim de cada caso, em bytes.</summary>
    public required AiMetricSummary WorkingSetBytes { get; init; }

    /// <summary>Recusas por desfecho; só aparece o que aconteceu.</summary>
    public required IReadOnlyDictionary<string, int> Failures { get; init; }

    /// <summary>Mensagem da primeira recusa, quando houve; ajuda a explicar um alvo sem números.</summary>
    public string FirstFailureMessage { get; init; } = "";

    /// <summary>
    /// Recorte por cenário. Sem ele um alvo com contextos de 85 e de 1 594 tokens publica um desvio padrão maior que
    /// a própria média e parece máquina instável, quando a amostra é multimodal por construção.
    /// </summary>
    public required IReadOnlyList<AiRuntimeScenarioReport> Scenarios { get; init; }

    /// <summary>Agrega as medições cronometradas de um alvo.</summary>
    public static AiRuntimeTargetReport Aggregate(AiRuntimeTarget target, AiRuntimeEvidence evidence, string effectiveProvider,
        double loadMilliseconds, long workingSetAfterLoad, long peakWorkingSet, IReadOnlyList<AiRuntimeMeasurement> measurements)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(measurements);
        var generated = measurements.Where(measurement => measurement.Outcome == AiRuntimeOutcome.Generated).ToArray();
        var failed = measurements.Where(measurement => measurement.Outcome != AiRuntimeOutcome.Generated).ToArray();
        var prepared = measurements.Where(measurement => measurement.ContextMicroseconds > 0).ToArray();
        return new()
        {
            TargetId = target.Id,
            PackageId = target.PackageId,
            RequestedHardware = target.Hardware,
            EffectiveProvider = effectiveProvider,
            Evidence = evidence,
            LoadMilliseconds = loadMilliseconds,
            WorkingSetAfterLoadBytes = workingSetAfterLoad,
            PeakWorkingSetBytes = peakWorkingSet,
            Cases = measurements.Count,
            GeneratedCases = generated.Length,
            TimeToFirstToken = AiMetricSummary.Of(generated.Where(m => m.TimeToFirstTokenMilliseconds.HasValue)
                .Select(m => m.TimeToFirstTokenMilliseconds!.Value)),
            ObservedFirstText = AiMetricSummary.Of(generated.Select(m => m.ObservedFirstTextMilliseconds)),
            Total = AiMetricSummary.Of(generated.Select(m => m.TotalMilliseconds)),
            TokensPerSecond = AiMetricSummary.Of(generated.Where(m => m.TokensPerSecond.HasValue).Select(m => m.TokensPerSecond!.Value)),
            // A fase de contexto só conta onde ela aconteceu: um alvo recusado na carga nunca montou prompt, e
            // registrá-lo como "0 µs de contexto" inventaria uma amostra rápida que ninguém mediu.
            ContextMicroseconds = AiMetricSummary.Of(prepared.Select(m => m.ContextMicroseconds)),
            TokenizationMicroseconds = AiMetricSummary.Of(prepared.Select(m => m.TokenizationMicroseconds)),
            PromptTokens = AiMetricSummary.Of(prepared.Select(m => m.PromptTokens)),
            GeneratedTokens = AiMetricSummary.Of(generated.Select(m => m.GeneratedTokens)),
            WorkingSetBytes = AiMetricSummary.Of(generated.Select(m => (double)m.WorkingSetBytes)),
            Failures = failed.GroupBy(m => m.Outcome.ToString())
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal),
            FirstFailureMessage = failed.Length == 0 ? "" : failed[0].Message,
            Scenarios = [.. generated.GroupBy(m => m.ScenarioId, StringComparer.Ordinal)
                .Select(group => new AiRuntimeScenarioReport
                {
                    ScenarioId = group.Key,
                    Cases = group.Count(),
                    PromptTokens = group.First().PromptTokens,
                    GeneratedTokens = AiMetricSummary.Of(group.Select(m => m.GeneratedTokens)),
                    TimeToFirstToken = AiMetricSummary.Of(group.Where(m => m.TimeToFirstTokenMilliseconds.HasValue)
                        .Select(m => m.TimeToFirstTokenMilliseconds!.Value)),
                    ObservedFirstText = AiMetricSummary.Of(group.Select(m => m.ObservedFirstTextMilliseconds)),
                    Total = AiMetricSummary.Of(group.Select(m => m.TotalMilliseconds)),
                    TokensPerSecond = AiMetricSummary.Of(group.Where(m => m.TokensPerSecond.HasValue).Select(m => m.TokensPerSecond!.Value))
                })]
        };
    }
}

/// <summary>Recorte de um alvo por cenário de editor: é onde o tamanho do prompt aparece separado.</summary>
public sealed record AiRuntimeScenarioReport
{
    /// <summary>Cenário medido.</summary>
    public required string ScenarioId { get; init; }

    /// <summary>Casos gerados neste cenário.</summary>
    public required int Cases { get; init; }

    /// <summary>Tokens autorizados do prompt; constante dentro do cenário, por construção do contrato.</summary>
    public required int PromptTokens { get; init; }

    /// <summary>Tokens gerados.</summary>
    public required AiMetricSummary GeneratedTokens { get; init; }

    /// <summary>TTFT relatado pelo runtime, em milissegundos.</summary>
    public required AiMetricSummary TimeToFirstToken { get; init; }

    /// <summary>Primeiro texto observado pelo harness, em milissegundos.</summary>
    public required AiMetricSummary ObservedFirstText { get; init; }

    /// <summary>Tempo total da geração, em milissegundos.</summary>
    public required AiMetricSummary Total { get; init; }

    /// <summary>Tokens por segundo do decode.</summary>
    public required AiMetricSummary TokensPerSecond { get; init; }
}

/// <summary>
/// Relatório de uma execução do <see cref="AiRuntimeHarness"/>: um documento por máquina, com a matriz de alvos
/// medidos e a origem da evidência de cada um.
/// </summary>
public sealed record AiRuntimeReport
{
    /// <summary>Versão do formato deste documento.</summary>
    public const int FormatVersion = 1;

    /// <summary>
    /// Limitação declarada do instrumento, gravada dentro do próprio relatório para que ninguém a perca ao copiar o
    /// arquivo. Ver <see cref="AiRuntimeHarness"/>.
    /// </summary>
    public const string UiFrameBudgetLimitation =
        "Este harness é um processo de console sem Avalonia: ele não mede orçamento por quadro da interface durante a "
        + "geração. Ausência de travamento observável em CPU está coberta pelos testes Headless de A42; a medição de "
        + "quadros exige o aplicativo nativo rodando e é evidência manual, não deste relatório.";

    /// <summary>Versão do formato.</summary>
    public int Version { get; init; } = FormatVersion;

    /// <summary>Instante UTC da agregação.</summary>
    public required DateTimeOffset GeneratedAtUtc { get; init; }

    /// <summary>Repetições cronometradas por caso.</summary>
    public required int Repetitions { get; init; }

    /// <summary>Repetições descartadas por caso antes de cronometrar.</summary>
    public required int WarmupRepetitions { get; init; }

    /// <summary>Máquina e runtime da medição.</summary>
    public required AiEvaluationEnvironment Environment { get; init; }

    /// <summary>Aceleradores que o runtime desta compilação relatou nesta máquina.</summary>
    public required IReadOnlyList<AiHardwareDevice> DetectedHardware { get; init; }

    /// <summary>Cenários medidos, na ordem.</summary>
    public required IReadOnlyList<string> Scenarios { get; init; }

    /// <summary>Um bloco por alvo: pacote × acelerador.</summary>
    public required IReadOnlyList<AiRuntimeTargetReport> Targets { get; init; }

    /// <summary>Limitação de medição de interface; constante, e por isso sempre presente.</summary>
    public string UiFrameBudgetNote { get; init; } = UiFrameBudgetLimitation;
}

/// <summary>
/// Perfil de latência da IA local por modelo × hardware × contexto × geração (fase 4, incrementos 4.5 e 4.7).
/// </summary>
/// <remarks>
/// <para><b>O que este harness mede.</b> Para cada alvo (pacote de modelo em um acelerador) e cada cenário de editor:
/// tempo de carga do pacote, montagem de contexto sob orçamento, tokenização do prompt, TTFT, tempo total,
/// tokens por segundo e working set do processo. As fases de contexto e tokenização são cronometradas
/// <em>separadamente</em> da inferência, com os mesmos componentes da Fase 3 que a produção usa
/// (<see cref="AiContextPipeline"/> e o <see cref="ICompletionPromptBuilder"/> do pacote) — não é uma reimplementação
/// nem uma segunda medição do que R42 já mediu.</para>
/// <para><b>O que este harness não mede: a interface.</b> Ele é um processo de console e não instancia Avalonia, logo
/// não existe quadro para cronometrar. O critério de aceite 10 (orçamento por quadro durante geração em CPU) só pode
/// ser fechado com o aplicativo nativo rodando; ver <see cref="AiRuntimeReport.UiFrameBudgetLimitation"/>. Declarar
/// aqui um número de quadro seria inventar evidência.</para>
/// <para><b>Fake não prova real.</b> O harness roda com qualquer <see cref="AiRuntimeSession"/>, mas o relatório
/// carrega a <see cref="AiRuntimeEvidence"/> de cada alvo. Um alvo <see cref="AiRuntimeEvidence.DeterministicFake"/>
/// mede a lógica de fila, cooldown e limite de espera, e nada mais: nenhum número dele descreve o custo de um modelo.
/// Os dois nunca são agregados juntos.</para>
/// <para><b>Como o tempo é medido.</b> <see cref="Stopwatch.GetTimestamp"/> em volta de cada fase, com
/// <see cref="WarmupRepetitions"/> execuções descartadas por cenário e <see cref="Repetitions"/> cronometradas. As
/// estatísticas (média, desvio padrão amostral, p50, p95) saem de <see cref="AiMetricSummary"/> sobre as execuções do
/// alvo inteiro; número sem desvio padrão nem máquina não é medição, e a máquina vai dentro do relatório.</para>
/// <para><b>BenchmarkDotNet não é usado aqui</b> e o motivo é o mesmo do harness da Fase 3: um job de processo
/// separado por caso carregaria pesos ONNX de novo a cada iteração, e o que domina a medição passaria a ser a carga.
/// Para o caminho quente isolado e sem pesos existe <see cref="IncrementalDecodeBenchmarks"/>.</para>
/// </remarks>
/// <param name="sessionFactory">Abre a sessão de um alvo; o harness não constrói serviço nem runtime.</param>
/// <param name="hardware">Sonda de aceleradores; a mesma da produção no caminho real, um fake no teste.</param>
/// <param name="environmentProvider">Captura o ambiente relatado; omitido, usa os metadados do runtime corrente.</param>
public sealed class AiRuntimeHarness(Func<AiRuntimeTarget, AiRuntimeSession> sessionFactory, IAiHardwareProbe hardware,
    Func<AiEvaluationEnvironment>? environmentProvider = null)
{
    private readonly Func<AiRuntimeTarget, AiRuntimeSession> _sessionFactory =
        sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
    private readonly IAiHardwareProbe _hardware = hardware ?? throw new ArgumentNullException(nameof(hardware));
    private readonly Func<AiEvaluationEnvironment> _environmentProvider = environmentProvider ?? AiEvaluationEnvironment.Current;

    /// <summary>Execuções descartadas por cenário antes de cronometrar; absorvem JIT, primeiro toque e warmup do provider.</summary>
    public int WarmupRepetitions { get; init; } = 1;

    /// <summary>Execuções cronometradas por cenário.</summary>
    public int Repetitions { get; init; } = 5;

    /// <summary>Origem da evidência dos alvos desta execução.</summary>
    public AiRuntimeEvidence Evidence { get; init; } = AiRuntimeEvidence.RealModel;

    /// <summary>Aceleradores que o runtime desta compilação relata nesta máquina.</summary>
    public Task<IReadOnlyList<AiHardwareDevice>> DetectHardwareAsync(CancellationToken cancellationToken = default) =>
        _hardware.GetAvailableHardwareAsync(cancellationToken);

    /// <summary>
    /// Cruza pacotes com os aceleradores realmente disponíveis. Um acelerador que a sonda não reporta não vira alvo:
    /// medir o que não existe produziria uma linha de recusa disfarçada de resultado.
    /// </summary>
    /// <param name="packages">Pares (identificador, caminho absoluto) dos pacotes a medir.</param>
    /// <param name="devices">Aceleradores relatados pela sonda.</param>
    public static IReadOnlyList<AiRuntimeTarget> SelectTargets(IReadOnlyList<(string Id, string Path)> packages,
        IReadOnlyList<AiHardwareDevice> devices)
    {
        ArgumentNullException.ThrowIfNull(packages);
        ArgumentNullException.ThrowIfNull(devices);
        var modes = devices.Where(device => device.IsAvailable)
            .Select(device => device.Kind)
            .Where(kind => kind != AiAccelerationMode.Auto)
            .Distinct()
            .OrderBy(kind => kind)
            .ToArray();
        if (modes.Length == 0) modes = [AiAccelerationMode.Cpu];
        return [.. packages.SelectMany(package => modes.Select(mode => new AiRuntimeTarget(package.Id, package.Path, mode)))];
    }

    /// <summary>Mede a matriz inteira e agrega o relatório.</summary>
    /// <param name="targets">Alvos a medir.</param>
    /// <param name="scenarios">Cenários de editor.</param>
    /// <param name="cancellationToken">Cancelamento cooperativo.</param>
    public async Task<AiRuntimeReport> RunAsync(IReadOnlyList<AiRuntimeTarget> targets, IReadOnlyList<AiRuntimeScenario> scenarios,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(scenarios);
        var reports = new List<AiRuntimeTargetReport>(targets.Count);
        foreach (var target in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            reports.Add(await RunTargetAsync(target, scenarios, cancellationToken).ConfigureAwait(false));
        }
        return new()
        {
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            Repetitions = Math.Max(1, Repetitions),
            WarmupRepetitions = Math.Max(0, WarmupRepetitions),
            Environment = _environmentProvider(),
            DetectedHardware = await DetectHardwareAsync(cancellationToken).ConfigureAwait(false),
            Scenarios = [.. scenarios.Select(scenario => scenario.Id)],
            Targets = reports
        };
    }

    /// <summary>Mede um alvo: carrega o pacote uma vez e percorre os cenários.</summary>
    public async Task<AiRuntimeTargetReport> RunTargetAsync(AiRuntimeTarget target, IReadOnlyList<AiRuntimeScenario> scenarios,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(scenarios);
        var measurements = new List<AiRuntimeMeasurement>();
        await using var session = _sessionFactory(target);
        var settings = target.Settings(scenarios.Count == 0 ? 2048 : scenarios[0].ContextTokens,
            scenarios.Count == 0 ? 32 : scenarios[0].MaximumTokens);

        LocalModelDefinition model;
        var startedLoad = Stopwatch.GetTimestamp();
        try
        {
            model = await session.Models.LoadModelAsync(LocalModelRole.Autocomplete, settings, cancellationToken).ConfigureAwait(false);
        }
        catch (LocalModelUnavailableException ex)
        {
            return AiRuntimeTargetReport.Aggregate(target, Evidence, "", Milliseconds(startedLoad), System.Environment.WorkingSet,
                System.Environment.WorkingSet,
                [new() { TargetId = target.Id, ScenarioId = "(carga)", Repetition = 0, Outcome = AiRuntimeOutcome.Refused, Message = ex.Message }]);
        }
        var loadMilliseconds = Milliseconds(startedLoad);
        var workingSetAfterLoad = System.Environment.WorkingSet;
        var peak = workingSetAfterLoad;

        var tokenizer = session.TokenizerFactory(model);
        var context = new AiContextPipeline(AiContextContractResolver.Resolve(model.Metadata), new TokenizerTokenCounter(tokenizer),
            new TokenRatioEstimator(), new TokenizedBlockCache(tokenizer, new TokenizerBoundaryOracle(tokenizer)));

        foreach (var scenario in scenarios)
        {
            for (var warmup = 0; warmup < Math.Max(0, WarmupRepetitions); warmup++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await MeasureAsync(session, target, model, context, tokenizer, scenario, -1, cancellationToken).ConfigureAwait(false);
            }
            for (var repetition = 0; repetition < Math.Max(1, Repetitions); repetition++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var measurement = await MeasureAsync(session, target, model, context, tokenizer, scenario, repetition, cancellationToken)
                    .ConfigureAwait(false);
                measurements.Add(measurement);
                peak = Math.Max(peak, measurement.WorkingSetBytes);
            }
        }

        var provider = session.Models.Status.Provider ?? measurements.Select(m => m.Provider).FirstOrDefault(p => p.Length > 0) ?? "";
        return AiRuntimeTargetReport.Aggregate(target, Evidence, provider, loadMilliseconds, workingSetAfterLoad, peak, measurements);
    }

    /// <summary>
    /// Um caso: contexto, tokenização e geração, cada fase com o próprio cronômetro. O pedido sai como
    /// <see cref="AiRequestPriority.Interactive"/> porque é o que <c>Ctrl+;</c> faz.
    /// </summary>
    private static async Task<AiRuntimeMeasurement> MeasureAsync(AiRuntimeSession session, AiRuntimeTarget target, LocalModelDefinition model,
        AiContextPipeline context, ITokenizer tokenizer, AiRuntimeScenario scenario, int repetition, CancellationToken cancellationToken)
    {
        var settings = target.Settings(scenario.ContextTokens, scenario.MaximumTokens);
        var cost = model.PromptFormat is LocalModelPromptFormats.QwenFim or LocalModelPromptFormats.DeepSeekCoderFim
            ? AiPromptFormatCost.QwenFim(scenario.MaximumTokens)
            : new AiPromptFormatCost(0, 0, scenario.MaximumTokens);

        var startedContext = Stopwatch.GetTimestamp();
        var prompt = context.Build(new(scenario.ToSnapshot(), settings, scenario.ContextTokens, cost));
        var contextMicroseconds = Microseconds(startedContext);
        if (!prompt.Success)
            return new()
            {
                TargetId = target.Id, ScenarioId = scenario.Id, Repetition = repetition, Outcome = AiRuntimeOutcome.BudgetExhausted,
                ContextMicroseconds = contextMicroseconds, WorkingSetBytes = System.Environment.WorkingSet,
                Message = prompt.Failure.ToString()
            };

        IReadOnlyList<int>? promptTokens = null;
        var tokenizationMicroseconds = 0d;
        if (session.PromptBuilder is { } builder)
        {
            var startedTokenization = Stopwatch.GetTimestamp();
            try { promptTokens = builder.Build(prompt.Prefix, prompt.Suffix, scenario.ContextTokens, tokenizer); }
            catch (InvalidDataException) { promptTokens = null; }
            tokenizationMicroseconds = Microseconds(startedTokenization);
        }

        var request = new ModelGenerationRequest(prompt.Prefix, prompt.Suffix, scenario.ContextTokens, scenario.MaximumTokens)
        {
            Temperature = model.Metadata?.Autocomplete.Temperature ?? 0,
            PromptTokens = promptTokens
        };

        var startedGeneration = Stopwatch.GetTimestamp();
        double? observedFirstText = null;
        GeneratedChunk? last = null;
        try
        {
            await foreach (var chunk in session.Models.StreamAsync(LocalModelRole.Autocomplete, settings, _ => request,
                AiRequestPriority.Interactive, AiModelLoadPolicy.LoadedOnly, cancellationToken).ConfigureAwait(false))
            {
                if (observedFirstText is null && chunk.Text.Length > 0) observedFirstText = Milliseconds(startedGeneration);
                if (chunk.IsFinal) last = chunk;
            }
        }
        catch (LocalModelUnavailableException ex)
        {
            return new()
            {
                TargetId = target.Id, ScenarioId = scenario.Id, Repetition = repetition, Outcome = AiRuntimeOutcome.Refused,
                PromptTokens = prompt.AuthorizedTokens, ContextMicroseconds = contextMicroseconds,
                TokenizationMicroseconds = tokenizationMicroseconds, TotalMilliseconds = Milliseconds(startedGeneration),
                WorkingSetBytes = System.Environment.WorkingSet, Message = ex.Message
            };
        }
        var total = Milliseconds(startedGeneration);

        return new()
        {
            TargetId = target.Id,
            ScenarioId = scenario.Id,
            Repetition = repetition,
            Outcome = (last?.GeneratedTokens ?? 0) > 0 ? AiRuntimeOutcome.Generated : AiRuntimeOutcome.Empty,
            PromptTokens = prompt.AuthorizedTokens,
            GeneratedTokens = last?.GeneratedTokens ?? 0,
            ContextMicroseconds = contextMicroseconds,
            TokenizationMicroseconds = tokenizationMicroseconds,
            TimeToFirstTokenMilliseconds = last?.TimeToFirstToken?.TotalMilliseconds,
            ObservedFirstTextMilliseconds = observedFirstText ?? total,
            TotalMilliseconds = total,
            TokensPerSecond = DecodeRate(last),
            WorkingSetBytes = System.Environment.WorkingSet,
            Provider = last?.Provider ?? ""
        };
    }

    /// <summary>
    /// Tokens por segundo do trecho de decode: os tokens depois do primeiro, sobre o tempo depois do primeiro. É a
    /// mesma conta de <c>LocalAiModelService.TokensPerSecond</c> — separar prefill de decode é o que impede um TTFT
    /// alto de ser lido como modelo lento a gerar.
    /// </summary>
    private static double? DecodeRate(GeneratedChunk? chunk)
    {
        if (chunk is null || chunk.GeneratedTokens <= 0) return null;
        if (chunk.GeneratedTokens >= 2 && chunk.TimeToFirstToken is { } first && chunk.Elapsed > first)
            return (chunk.GeneratedTokens - 1) / (chunk.Elapsed - first).TotalSeconds;
        return chunk.Elapsed > TimeSpan.Zero ? chunk.GeneratedTokens / chunk.Elapsed.TotalSeconds : null;
    }

    private static double Microseconds(long started) => Stopwatch.GetElapsedTime(started).TotalMicroseconds;

    private static double Milliseconds(long started) => Stopwatch.GetElapsedTime(started).TotalMilliseconds;
}

/// <summary>
/// Serializa um <see cref="AiRuntimeReport"/> em JSON (critério de aceite 8 e incremento 4.7) e em Markdown. Os dois
/// carregam o mesmo conteúdo.
/// </summary>
/// <remarks>
/// A saída é efêmera e mora em <c>tests/EsilvaSoft.KapibaraStudio.Benchmarks/Ai/output/</c>, ignorada pelo git, pelo
/// mesmo motivo do relatório da Fase 3: medição só vale junto com a máquina que a produziu, e a máquina está dentro
/// do arquivo.
/// </remarks>
public static class AiRuntimeReportWriter
{
    /// <summary>Nome base dos dois arquivos de saída.</summary>
    public const string FileBaseName = "ai-runtime-latency";

    /// <summary>Títulos das seções do Markdown, na ordem em que aparecem.</summary>
    public static IReadOnlyList<string> MarkdownSections { get; } =
    [
        "## Execução",
        "## Máquina",
        "## Hardware detectado",
        "## Alvos",
        "## Métricas por alvo",
        "## Limitação declarada"
    ];

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Serializa em JSON indentado com nomes em <c>camelCase</c>.</summary>
    public static string ToJson(AiRuntimeReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return JsonSerializer.Serialize(report, Json);
    }

    /// <summary>Serializa em Markdown com as seções de <see cref="MarkdownSections"/>.</summary>
    public static string ToMarkdown(AiRuntimeReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var culture = CultureInfo.InvariantCulture;
        var builder = new StringBuilder(4096);
        builder.Append("# Perfil de latência da IA local\n\nSaída efêmera do `AiRuntimeHarness`; não commitar.\n\n");

        builder.Append(MarkdownSections[0]).Append("\n\n")
            .Append(culture, $"- Versão do formato: {report.Version}\n")
            .Append(culture, $"- Gerado em (UTC): {report.GeneratedAtUtc:O}\n")
            .Append(culture, $"- Repetições cronometradas por cenário: {report.Repetitions} (aquecimento: {report.WarmupRepetitions})\n")
            .Append(culture, $"- Cenários: {string.Join(", ", report.Scenarios)}\n\n");

        builder.Append(MarkdownSections[1]).Append("\n\n")
            .Append(culture, $"- Sistema: {report.Environment.OperatingSystem}\n")
            .Append(culture, $"- Arquitetura: {report.Environment.Architecture}\n")
            .Append(culture, $"- Processadores lógicos: {report.Environment.LogicalProcessors}\n")
            .Append(culture, $"- Runtime: {report.Environment.Runtime}\n")
            .Append(culture, $"- GC de servidor: {report.Environment.ServerGarbageCollection}\n")
            .Append(culture, $"- Depurador anexado: {report.Environment.DebuggerAttached}\n")
            .Append(culture, $"- Configuração: {report.Environment.Configuration}\n\n");

        builder.Append(MarkdownSections[2]).Append("\n\n")
            .Append("| Tipo | Provider | Dispositivo | Disponível | Motivo |\n| --- | --- | --- | --- | --- |\n");
        foreach (var device in report.DetectedHardware)
            builder.Append(culture, $"| {device.Kind} | {device.Provider} | {device.Name} | {device.IsAvailable} | {device.Reason ?? "—"} |\n");
        builder.Append('\n');

        builder.Append(MarkdownSections[3]).Append("\n\n")
            .Append("| Alvo | Evidência | Provider efetivo | Carga (ms) | Working set pós-carga (MiB) | Pico (MiB) | Casos | Gerados | Recusas |\n")
            .Append("| --- | --- | --- | --- | --- | --- | --- | --- | --- |\n");
        foreach (var target in report.Targets)
            builder.Append(culture,
                $"| {target.TargetId} | {target.Evidence} | {target.EffectiveProvider} | {target.LoadMilliseconds:F0} | "
                + $"{Mebibytes(target.WorkingSetAfterLoadBytes):F0} | {Mebibytes(target.PeakWorkingSetBytes):F0} | {target.Cases} | "
                + $"{target.GeneratedCases} | {Failures(target)} |\n");
        builder.Append('\n');

        builder.Append(MarkdownSections[4]).Append("\n\n");
        foreach (var target in report.Targets)
        {
            builder.Append(culture, $"### {target.TargetId} ({target.Evidence})\n\n")
                .Append("| Métrica | Média | Desvio padrão | Mín | p50 | p95 | Máx |\n| --- | --- | --- | --- | --- | --- | --- |\n");
            AppendMetric(builder, "TTFT do runtime (ms)", target.TimeToFirstToken);
            AppendMetric(builder, "1º texto observado (ms)", target.ObservedFirstText);
            AppendMetric(builder, "total (ms)", target.Total);
            AppendMetric(builder, "tokens/s (decode)", target.TokensPerSecond);
            AppendMetric(builder, "contexto (µs)", target.ContextMicroseconds);
            AppendMetric(builder, "tokenização (µs)", target.TokenizationMicroseconds);
            AppendMetric(builder, "tokens do prompt", target.PromptTokens);
            AppendMetric(builder, "tokens gerados", target.GeneratedTokens);
            AppendMetric(builder, "working set (MiB)", target.WorkingSetBytes, 1d / 1024 / 1024);
            if (target.Scenarios.Count > 0)
            {
                builder.Append("\n| Cenário | Tokens do prompt | Tokens gerados (p50) | TTFT p50 (ms) | TTFT p95 (ms) | Total p50 (ms) | Total p95 (ms) | tokens/s p50 |\n")
                    .Append("| --- | --- | --- | --- | --- | --- | --- | --- |\n");
                foreach (var scenario in target.Scenarios)
                    builder.Append(culture,
                        $"| {scenario.ScenarioId} | {scenario.PromptTokens} | {scenario.GeneratedTokens.Median:F0} | "
                        + $"{scenario.TimeToFirstToken.Median:F1} | {scenario.TimeToFirstToken.Percentile95:F1} | "
                        + $"{scenario.Total.Median:F1} | {scenario.Total.Percentile95:F1} | {scenario.TokensPerSecond.Median:F1} |\n");
            }
            if (target.FirstFailureMessage.Length > 0)
                builder.Append(culture, $"\nPrimeira recusa: {target.FirstFailureMessage}\n");
            builder.Append('\n');
        }

        builder.Append(MarkdownSections[5]).Append("\n\n").Append(report.UiFrameBudgetNote).Append('\n');
        return builder.ToString();
    }

    /// <summary>Escreve os dois arquivos e devolve os caminhos, na ordem JSON, Markdown.</summary>
    public static async Task<IReadOnlyList<string>> WriteAsync(AiRuntimeReport report, string directory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentException.ThrowIfNullOrEmpty(directory);
        Directory.CreateDirectory(directory);
        var json = Path.Combine(directory, FileBaseName + ".json");
        var markdown = Path.Combine(directory, FileBaseName + ".md");
        await File.WriteAllTextAsync(json, ToJson(report), Utf8, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(markdown, ToMarkdown(report), Utf8, cancellationToken).ConfigureAwait(false);
        return [json, markdown];
    }

    /// <summary>Diretório de saída padrão, o mesmo do relatório da Fase 3.</summary>
    public static string DefaultDirectory() => AiEvaluationReportWriter.DefaultDirectory();

    private static string Failures(AiRuntimeTargetReport target) =>
        target.Failures.Count == 0 ? "—" : string.Join(", ", target.Failures.Select(entry => entry.Key + "=" + entry.Value.ToString(CultureInfo.InvariantCulture)));

    private static double Mebibytes(long bytes) => bytes / 1024d / 1024d;

    /// <summary>
    /// Uma linha de métrica. O fator só troca a unidade: média, desvio padrão, mínimo, mediana, p95 e máximo são
    /// todos lineares na escala, então multiplicar cada um pelo mesmo fator preserva a estatística.
    /// </summary>
    private static void AppendMetric(StringBuilder builder, string name, AiMetricSummary metric, double scale = 1)
    {
        // Sem amostra não se escreve 0,00: um alvo recusado não é um alvo instantâneo, e a tabela não pode sugerir isso.
        if (metric.Count == 0)
        {
            builder.Append(CultureInfo.InvariantCulture, $"| {name} | — | — | — | — | — | — |\n");
            return;
        }
        builder.Append(CultureInfo.InvariantCulture,
            $"| {name} | {metric.Mean * scale:F2} | {metric.StandardDeviation * scale:F2} | {metric.Minimum * scale:F2} | "
            + $"{metric.Median * scale:F2} | {metric.Percentile95 * scale:F2} | {metric.Maximum * scale:F2} |\n");
    }
}
