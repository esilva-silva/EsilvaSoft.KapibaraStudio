using System.Text.RegularExpressions;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

/// <summary>
/// Lote A43: as linhas da <see href="../../docs/auto-complite/ai-autocomplete.md">matriz de fallback</see> que não são
/// recusa direta do serviço de modelo — prazo rígido, contexto que excede a janela, preempção e carga em andamento —,
/// mais a regra de que todas elas são decididas por motivo tipado e nunca por texto de mensagem.
/// </summary>
/// <remarks>
/// As seis linhas de recusa imediata (sem modelo, modelo inválido, sem capacidade, provider indisponível, cooldown e
/// contexto sensível) já são exercitadas em <c>AiCompletionUiTests</c> e não são repetidas aqui.
/// </remarks>
[TestFixture]
public sealed class AiCompletionFallbackTests
{
    private const string Document = "db.Customers.find({ })";
    private const int Caret = 20;

    private static AiGenerationRequest Request(int contextTokens = 4096) =>
        new(new AutocompleteContextSnapshot(Document, Caret, "JavaScript (mongosh)", KnownNames: ["Customers", "Orders"]),
            new AutocompleteSettings { ContextTokens = contextTokens });

    private static AiCompletionProvider Provider(ILocalAiModelService models, TimeProvider? clock = null) =>
        new(new AiGenerationPipeline(models, _ => new CompletionTokenizerFake(), new QwenFimPromptBuilder(), null, clock));

    private static async Task<List<AiCompletionUpdate>> CollectAsync(AiCompletionProvider provider, AiGenerationRequest request)
    {
        var updates = new List<AiCompletionUpdate>();
        await foreach (var update in provider.RequestAsync(request)) updates.Add(update);
        return updates;
    }

    /// <summary>
    /// Prazo rígido com texto já gerado: a geração é interrompida e o que existia de válido vira candidato. Sucesso
    /// parcial, e não falha — a prévia que o usuário está lendo não é apagada pelo relógio.
    /// </summary>
    [Test]
    public async Task TheHardTimeoutKeepsAValidPartialPreviewAsTheFinalCandidate()
    {
        var clock = new ManualTimeProvider();
        var service = new StreamingModelServiceFake { Chunks = ["status", ": 'A'"], HangAfterChunks = true };
        // O falso nunca termina sozinho: só o prazo o interrompe. Sem o avanço do relógio este teste ficaria pendurado,
        // que é justamente o que prova que o corte veio do limite e não do fim natural da geração.
        _ = service.Hanging.Task.ContinueWith(_ => clock.Advance(TimeSpan.FromSeconds(11)), TaskScheduler.Default);

        var updates = await CollectAsync(Provider(service, clock), Request());

        Assert.Multiple(() =>
        {
            Assert.That(updates[^1].Success, Is.True, "Prazo vencido com prévia válida é sucesso parcial.");
            Assert.That(updates[^1].Candidate?.Text, Is.EqualTo("status: 'A'"));
            Assert.That(updates[^1].Candidate?.TimedOut, Is.True);
            Assert.That(updates[^1].Candidate?.IsComplete, Is.False, "Nada garante que o modelo tinha terminado.");
            Assert.That(updates[^1].Failure, Is.EqualTo(AiCompletionFailure.None));
        });
    }

    /// <summary>Prazo rígido sem nenhum texto: não há prévia a manter, e a recusa tipada leva ao fallback da lista.</summary>
    [Test]
    public async Task TheHardTimeoutWithoutAnyGeneratedTextEndsInTheTypedTimeoutFailure()
    {
        var clock = new ManualTimeProvider();
        var service = new StreamingModelServiceFake { Chunks = [], HangAfterChunks = true };
        _ = service.Hanging.Task.ContinueWith(_ => clock.Advance(TimeSpan.FromSeconds(11)), TaskScheduler.Default);

        var updates = await CollectAsync(Provider(service, clock), Request());

        Assert.Multiple(() =>
        {
            Assert.That(updates, Has.Count.EqualTo(1), "Sem texto nenhum não há prévia a exibir.");
            Assert.That(updates[0].IsFinal, Is.True);
            Assert.That(updates[0].Failure, Is.EqualTo(AiCompletionFailure.Timeout));
            Assert.That(updates[0].Candidate, Is.Null);
        });
    }

    /// <summary>O prazo é o configurado, e não uma constante: metade dele não interrompe nada.</summary>
    [Test]
    public async Task TheTimeoutIsTheConfiguredOneAndNotAFixedConstant()
    {
        var clock = new ManualTimeProvider();
        var service = new StreamingModelServiceFake { Chunks = ["status: 'A'"], HangAfterChunks = true };
        _ = service.Hanging.Task.ContinueWith(_ =>
        {
            // Metade do prazo configurado não corta nada; o restante corta.
            clock.Advance(TimeSpan.FromSeconds(1));
            clock.Advance(TimeSpan.FromSeconds(2));
        }, TaskScheduler.Default);
        var request = Request() with { Settings = new AutocompleteSettings { ContextTokens = 4096, AiTimeoutMilliseconds = 3000 } };

        var updates = await CollectAsync(Provider(service, clock), request);

        Assert.That(updates[^1].Candidate?.TimedOut, Is.True, "O corte veio do prazo de 3 s configurado.");
    }

    /// <summary>
    /// Contexto que não cabe: o orçamento é reduzido <strong>uma vez</strong> — o que reaproveita o corte por
    /// orçamento da Fase 3 — e o pedido é repetido. Cabendo, o usuário recebe a sugestão e nunca vê a recusa.
    /// </summary>
    [Test]
    public async Task AContextOverflowReducesTheBudgetOnceAndSucceedsOnTheSecondAttempt()
    {
        // 4096 estoura; 4096 × 0,75 = 3072 cabe.
        var service = new StreamingModelServiceFake { ContextOverflowAbove = 3072 };

        var updates = await CollectAsync(Provider(service), Request());

        Assert.Multiple(() =>
        {
            Assert.That(updates[^1].Candidate?.Text, Is.EqualTo("status: 'A'"), "A segunda tentativa é uma sugestão comum.");
            Assert.That(service.Requests, Has.Count.EqualTo(2), "Exatamente uma repetição.");
            Assert.That(service.Requests[1].ContextTokens, Is.LessThan(service.Requests[0].ContextTokens),
                "A repetição precisa pedir menos contexto; repetir igual só gastaria o dobro do tempo.");
        });
    }

    /// <summary>Persistindo depois da redução, aí sim é recusa — com o motivo tipado da matriz e sem uma terceira tentativa.</summary>
    [Test]
    public async Task AContextOverflowThatPersistsAfterTheReductionFallsBackWithTheTypedReason()
    {
        var service = new StreamingModelServiceFake { ContextOverflowAbove = 8 };

        var updates = await CollectAsync(Provider(service), Request());

        Assert.Multiple(() =>
        {
            Assert.That(service.Requests, Has.Count.EqualTo(2), "Reduz uma vez, não indefinidamente.");
            Assert.That(updates[^1].Failure, Is.EqualTo(AiCompletionFailure.ModelUnavailable));
            Assert.That(updates[^1].Reason, Is.EqualTo(LocalModelUnavailableReason.ContextOverflow));
        });
    }

    /// <summary>
    /// Preempção por chat ou pelo teste de modelo: descarte silencioso. O fluxo termina com uma falha própria, que é
    /// o que permite à interface apagar o indicador sem abrir lista nem explicar nada.
    /// </summary>
    [Test]
    public async Task PreemptionEndsTheStreamWithItsOwnTypedFailureAndNoModelReason()
    {
        var service = new StreamingModelServiceFake { Chunks = ["status"], PreemptAfterChunks = true };

        var updates = await CollectAsync(Provider(service), Request());

        Assert.Multiple(() =>
        {
            Assert.That(updates[^1].Failure, Is.EqualTo(AiCompletionFailure.Preempted));
            Assert.That(updates[^1].Failure, Is.Not.EqualTo(AiCompletionFailure.ModelUnavailable),
                "Preempção não é recusa do modelo: confundir as duas abriria a lista com uma explicação falsa.");
            Assert.That(updates[^1].Reason, Is.Null);
            Assert.That(updates[^1].Candidate, Is.Null);
        });
    }

    /// <summary>Carga em andamento: o fluxo anuncia a espera pela carga antes de qualquer token.</summary>
    [Test]
    public async Task AModelThatIsNotLoadedYetAnnouncesTheLoadingStepFirst()
    {
        var service = new StreamingModelServiceFake { Loaded = false };

        var updates = await CollectAsync(Provider(service), Request());

        Assert.Multiple(() =>
        {
            Assert.That(updates[0].IsLoading, Is.True, "A primeira atualização é a espera pela carga.");
            Assert.That(updates[0].IsFinal, Is.False, "Esperar a carga não encerra nada.");
            Assert.That(service.LoadCalls, Is.EqualTo(1));
            Assert.That(updates[^1].Candidate?.Text, Is.EqualTo("status: 'A'"));
        });
    }

    /// <summary>Com o modelo já carregado não há espera a anunciar, e o indicador de carga nunca pisca.</summary>
    [Test]
    public async Task AnAlreadyLoadedModelNeverAnnouncesALoadingStep()
    {
        var updates = await CollectAsync(Provider(new StreamingModelServiceFake()), Request());

        Assert.That(updates.Any(update => update.IsLoading), Is.False);
    }

    /// <summary>
    /// Nenhuma decisão de fallback pode ser tomada por inspeção do <em>texto</em> de uma mensagem
    /// (DEC-R41-REASONS): motivo e comportamento saem do enum e do tipo da exceção. Guarda estrutural sobre o
    /// próprio código de produção, porque o defeito que ela previne é uma linha nova, não um tipo novo.
    /// </summary>
    [Test]
    public void NoFallbackDecisionIsTakenByInspectingMessageText()
    {
        string[] files =
        [
            @"src\EsilvaSoft.KapibaraStudio.Application\AiGenerationPipeline.cs",
            @"src\EsilvaSoft.KapibaraStudio.Application\AiCompletionProvider.cs",
            @"src\EsilvaSoft.KapibaraStudio.Application\CompletionOutputProcessor.cs",
            @"src\EsilvaSoft.KapibaraStudio.Desktop\AiCompletionFallbackMessages.cs",
            @"src\EsilvaSoft.KapibaraStudio.Desktop\WorkspaceTabView.Autocomplete.Ai.cs"
        ];
        // Comparar/procurar dentro de qualquer coisa chamada Message, Reason (texto do provider) ou Status.Message.
        var forbidden = new Regex(@"(Message|\.Reason)\s*(==|!=)\s*""|(Message|\.Reason)\.(Contains|StartsWith|EndsWith|IndexOf)\s*\(",
            RegexOptions.CultureInvariant);
        var root = RepositoryRoot();

        var offenders = files.SelectMany(relative =>
        {
            var path = Path.Combine(root, relative.Replace('\\', Path.DirectorySeparatorChar));
            Assert.That(File.Exists(path), Is.True, "Fonte varrido não encontrado: " + relative);
            return File.ReadAllLines(path).Select((line, index) => (relative, number: index + 1, line))
                .Where(entry => forbidden.IsMatch(entry.line));
        }).Select(entry => $"{entry.relative}:{entry.number}").ToArray();

        Assert.That(offenders, Is.Empty, "Decisão por texto de mensagem: " + string.Join(", ", offenders));
    }

    private static string RepositoryRoot()
    {
        var root = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "EsilvaSoft.KapibaraStudio.slnx"))) root = root.Parent;
        Assert.That(root, Is.Not.Null, "Raiz do repositório não encontrada.");
        return root!.FullName;
    }
}
