using System.Collections;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Reflection;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.AiContext;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Completion;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Facts;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Text;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;
using EsilvaSoft.KapibaraStudio.UnitTests.TokenCounting;

namespace EsilvaSoft.KapibaraStudio.UnitTests.AiContext;

/// <summary>
/// O pipeline de contexto sob orçamento. Tudo aqui roda contra o mini-BPE real de
/// <see cref="AdversarialBpeTokenizer"/>: um tokenizador de brinquedo "1 caractere = 1 token" tornaria a maioria
/// destas asserções tautológica, em especial a distinção entre estimativa e contagem exata.
/// </summary>
[TestFixture]
public sealed class AiContextPipelineTests
{
    private const string Sentinel = "SENTINEL_9f3a_segredo";

    private static AiFactSet Facts(int count) => AiFactSet.From(Enumerable.Range(1, count).Select(index =>
        new AiFact(AiFactKind.FieldSchema, AiFactOrigin.CatalogSchema, AiFactScope.ForCollection("loja", "Customers"),
            new AiFactPayload("campo" + index.ToString("000", CultureInfo.InvariantCulture))
            {
                LogicalType = "string",
                Presence = 0.9,
                Values = ["string", "null"]
            })));

    private static AutocompleteContextSnapshot Snapshot(string marker = "na", int padding = 6000)
        => new(new string('a', padding) + "\ndb.Customers.find({ " + marker, padding + 21 + marker.Length, "JavaScript (mongosh)",
            KnownNames: ["Customers", "Orders", "Products"],
            RecentCommands: ["db.Customers.findOne()", "db.Orders.countDocuments()"]);

    private static AiContextPipeline Pipeline(ITokenizer tokenizer, out TokenizedBlockCache cache, double ratio = 1.0)
    {
        cache = new TokenizedBlockCache(tokenizer, new TokenizerBoundaryOracle(tokenizer));
        return new AiContextPipeline(EditorContextV1Contract.Instance, new TokenizerTokenCounter(tokenizer),
            new TokenRatioEstimator(ratio), cache);
    }

    private static AiPromptRequest Request(int contextTokens, int facts = 12, string marker = "na")
        => new(Snapshot(marker), new AutocompleteSettings(), contextTokens, AiPromptFormatCost.QwenFim(64))
        {
            Facts = Facts(facts)
        };

    /// <summary>Tokens do cabeçalho do contrato sozinho, sem janela nem fatos; é o piso que o pipeline não corta.</summary>
    private static int HeaderTokens(AdversarialBpeTokenizer tokenizer, string marker = "na")
    {
        var built = AutocompleteContextBuilder.Build(Snapshot(marker), new());
        return tokenizer.Encode(AutocompleteContextBuilder.ModelPrefix(built with { Prefix = "" })).Count;
    }

    /// <summary>Tokens da janela do editor inteira, como o contrato a recorta.</summary>
    private static int WindowTokens(AdversarialBpeTokenizer tokenizer, string marker = "na")
    {
        var built = AutocompleteContextBuilder.Build(Snapshot(marker), new());
        return tokenizer.Encode(built.Prefix).Count + tokenizer.Encode(built.Suffix).Count;
    }

    [Test]
    public void CandidateThatOverflowsIsCutUntilTheExactCountFits()
    {
        var tokenizer = new AdversarialBpeTokenizer();
        var pipeline = Pipeline(tokenizer, out _);
        var request = Request(HeaderTokens(tokenizer) + 400);

        var result = pipeline.Build(request);

        Assert.That(result.Success, Is.True, "Sobrava margem acima do cabeçalho; o corte deveria ter bastado.");
        var recount = tokenizer.Encode(result.Prefix).Count + tokenizer.Encode(result.Suffix).Count;
        Assert.Multiple(() =>
        {
            Assert.That(recount, Is.EqualTo(result.PromptTokens), "A contagem publicada não é Encode(prefixo)+Encode(sufixo).");
            Assert.That(result.TotalTokens, Is.LessThanOrEqualTo(request.Budget.ContextTokens));
            Assert.That(result.PromptTokens, Is.LessThanOrEqualTo(request.Budget.AvailableTokens));
            Assert.That(result.FactsDropped + result.FactsIncluded, Is.EqualTo(request.Facts.Count));
        });
    }

    /// <summary>Propriedade do orçamento: por mais apertada que seja a janela, um sucesso nunca excede o contexto.</summary>
    [TestCase(80)]
    [TestCase(200)]
    [TestCase(420)]
    [TestCase(900)]
    [TestCase(3000)]
    public void ASuccessfulResultNeverExceedsTheContextWindow(int margin)
    {
        var tokenizer = new AdversarialBpeTokenizer();
        var pipeline = Pipeline(tokenizer, out _);
        var request = Request(HeaderTokens(tokenizer) + margin);

        var result = pipeline.Build(request);

        Assert.That(result.Success, Is.True);
        var exact = tokenizer.Encode(result.Prefix).Count + tokenizer.Encode(result.Suffix).Count
            + request.FormatCost.MarkerTokens + request.FormatCost.BosEosTokens + request.FormatCost.ReservedCompletionTokens;
        Assert.That(exact, Is.LessThanOrEqualTo(request.Budget.ContextTokens));
    }

    /// <summary>Fatos saem do menos relevante para o mais relevante; o seletor entrega em ordem decrescente.</summary>
    [Test]
    public void FactsAreDroppedFromTheLeastRelevantEnd()
    {
        var tokenizer = new AdversarialBpeTokenizer();
        var pipeline = Pipeline(tokenizer, out _);
        // Margem escolhida para caber a janela inteira e só parte dos fatos: é o caso em que a ordem do corte aparece.
        var request = Request(HeaderTokens(tokenizer) + WindowTokens(tokenizer) + 400, facts: 20);

        var result = pipeline.Build(request);

        Assert.That(result.Success, Is.True);
        Assert.That(result.FactsIncluded, Is.InRange(1, request.Facts.Count - 1), "A margem devia manter alguns fatos e derrubar o resto.");
        for (var index = 0; index < result.FactsIncluded; index++)
            Assert.That(result.Prefix, Does.Contain(request.Facts[index].Payload.Name));
        for (var index = result.FactsIncluded; index < request.Facts.Count; index++)
            Assert.That(result.Prefix, Does.Not.Contain(request.Facts[index].Payload.Name));
    }

    [Test]
    public void NothingLeftToCutFailsTypedInsteadOfThrowing()
    {
        var tokenizer = new AdversarialBpeTokenizer();
        var pipeline = Pipeline(tokenizer, out _);
        var request = Request(contextTokens: 120);

        AiPromptResult? result = null;
        Assert.DoesNotThrow(() => result = pipeline.Build(request), "O caminho normal do autocomplete não pode lançar por orçamento.");
        Assert.Multiple(() =>
        {
            Assert.That(result!.Success, Is.False);
            Assert.That(result.Failure, Is.EqualTo(AiPromptFailure.BudgetExhausted));
            Assert.That(result.Prefix, Is.Empty, "Uma recusa não carrega texto nenhum.");
            Assert.That(result.Suffix, Is.Empty);
            Assert.That(result.PromptTokens, Is.Zero);
        });
    }

    [Test]
    public void ColdAndHotCachesProduceTheSameBytes()
    {
        var tokenizer = new AdversarialBpeTokenizer();
        var pipeline = Pipeline(tokenizer, out var cache);
        var request = Request(HeaderTokens(tokenizer) + 500);

        var cold = pipeline.Build(request);
        var cachedAfterCold = cache.CachedBlocks;
        var hot = pipeline.Build(request);
        var otherPipeline = Pipeline(tokenizer, out _);
        var freshCold = otherPipeline.Build(request);

        Assert.Multiple(() =>
        {
            Assert.That(cachedAfterCold, Is.GreaterThan(0), "O cache precisa ter sido povoado, senão frio e quente são o mesmo caso.");
            Assert.That(hot.Prefix, Is.EqualTo(cold.Prefix).Using(StringComparer.Ordinal));
            Assert.That(hot.Suffix, Is.EqualTo(cold.Suffix).Using(StringComparer.Ordinal));
            Assert.That(freshCold.Prefix, Is.EqualTo(cold.Prefix).Using(StringComparer.Ordinal));
            Assert.That(freshCold.Suffix, Is.EqualTo(cold.Suffix).Using(StringComparer.Ordinal));
            Assert.That(hot.PromptTokens, Is.EqualTo(cold.PromptTokens));
            Assert.That(hot.FactsIncluded, Is.EqualTo(cold.FactsIncluded));
        });
    }

    [Test]
    public void PromptTokensIsRecordedOnceWithTheExactCount()
    {
        var tokenizer = new AdversarialBpeTokenizer();
        var pipeline = Pipeline(tokenizer, out _);
        var request = Request(HeaderTokens(tokenizer) + 500);

        AiPromptResult? result = null;
        var measurements = RecordPromptTokens(() => result = pipeline.Build(request));

        Assert.Multiple(() =>
        {
            Assert.That(measurements, Has.Count.EqualTo(1), "Uma chamada bem-sucedida grava exatamente uma medição.");
            Assert.That(measurements[0].Value, Is.EqualTo(result!.AuthorizedTokens));
            Assert.That(measurements[0].Value, Is.EqualTo(result.PromptTokens + request.FormatCost.OverheadTokens));
            Assert.That(measurements[0].Tags.Select(tag => tag.Key), Is.SubsetOf(AutocompleteMetrics.AllowedTags));
        });
    }

    /// <summary>A métrica mede o prompt autorizado; se a estimativa entrasse no lugar, o número não bateria com o Encode.</summary>
    [Test]
    public void TheRecordedValueIsTheExactCountNotTheEstimate()
    {
        var tokenizer = new AdversarialBpeTokenizer();
        // Razão absurda de propósito: a estimativa erra por quase o dobro e mesmo assim não contamina a métrica.
        var pipeline = Pipeline(tokenizer, out _, ratio: 1.9);
        var request = Request(HeaderTokens(tokenizer) + 600);

        AiPromptResult? result = null;
        var measurements = RecordPromptTokens(() => result = pipeline.Build(request));
        var exact = tokenizer.Encode(result!.Prefix).Count + tokenizer.Encode(result.Suffix).Count;

        Assert.Multiple(() =>
        {
            Assert.That(measurements, Has.Count.EqualTo(1));
            Assert.That(measurements[0].Value, Is.EqualTo(exact + request.FormatCost.OverheadTokens));
        });
    }

    [Test]
    public void AFailedRequestRecordsNothing()
    {
        var tokenizer = new AdversarialBpeTokenizer();
        var pipeline = Pipeline(tokenizer, out _);

        AiPromptResult? result = null;
        var measurements = RecordPromptTokens(() => result = pipeline.Build(Request(contextTokens: 120)));

        Assert.Multiple(() =>
        {
            Assert.That(result!.Success, Is.False);
            Assert.That(measurements, Is.Empty);
        });
    }

    /// <summary>
    /// Lado consumidor da invariante central da fase: o prompt final é tokenizado inteiro, e nunca montado somando
    /// identificadores guardados em cache.
    /// </summary>
    [Test]
    public void TheFinalPromptIsAlwaysTokenizedWholeAndNeverAssembledFromCachedIds()
    {
        var tokenizer = new CountingTokenizer(new AdversarialBpeTokenizer());
        var pipeline = Pipeline(tokenizer, out _);
        var request = Request(HeaderTokens(new AdversarialBpeTokenizer()) + 500);

        var result = pipeline.Build(request);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(tokenizer.EncodedTexts.Contains(result.Prefix, StringComparer.Ordinal), Is.True,
                "O prefixo final precisa ter sido submetido inteiro ao Encode.");
            Assert.That(tokenizer.EncodedTexts.Contains(result.Suffix, StringComparer.Ordinal), Is.True);
            Assert.That(typeof(TokenizedBlockCache).GetMembers(BindingFlags.Public | BindingFlags.Instance)
                    .OfType<MethodInfo>().Select(method => method.ReturnType)
                    .Where(type => typeof(IEnumerable<int>).IsAssignableFrom(type)),
                Is.Empty, "O cache não pode passar a expor identificadores de tokens.");
        });
    }

    /// <summary>
    /// Auditoria de privacidade: o texto do editor não sobrevive à chamada. O prompt devolvido é efêmero e é o único
    /// lugar onde o sentinela pode aparecer; nada no pipeline, no cache ou nos demais campos do resultado o retém.
    /// </summary>
    [Test]
    public void TheFinalPromptIsNeverRetainedBeyondTheCall()
    {
        var tokenizer = new AdversarialBpeTokenizer();
        var pipeline = Pipeline(tokenizer, out var cache);
        var request = Request(HeaderTokens(tokenizer) + 900, marker: Sentinel);

        AiPromptResult? result = null;
        var measurements = RecordPromptTokens(() => result = pipeline.Build(request));

        Assert.Multiple(() =>
        {
            Assert.That(result!.Prefix, Does.Contain(Sentinel), "Sem o sentinela no prompt a auditoria não prova nada.");
            Assert.That(Reachable(pipeline), Has.None.Contains(Sentinel), "O pipeline reteve texto do editor.");
            Assert.That(Reachable(cache), Has.None.Contains(Sentinel), "O cache de blocos reteve texto do editor.");
            Assert.That(OtherStringProperties(result), Has.None.Contains(Sentinel), "Um campo do resultado além do prompt reteve o texto.");
            Assert.That(measurements.SelectMany(measurement => measurement.Tags)
                .Select(tag => tag.Value?.ToString() ?? ""), Has.None.Contains(Sentinel), "Uma tag de métrica levou texto do editor.");
        });
    }

    /// <summary>Sem fatos, o prompt é byte a byte o prefixo do contrato congelado: o pipeline age acima dele, não no lugar dele.</summary>
    [Test]
    public void WithoutFactsThePromptIsTheFrozenContractPrefix()
    {
        var tokenizer = new AdversarialBpeTokenizer();
        var pipeline = Pipeline(tokenizer, out _);
        var snapshot = Snapshot(padding: 40);
        var settings = new AutocompleteSettings();
        var built = AutocompleteContextBuilder.Build(snapshot, settings);

        var result = pipeline.Build(new(snapshot, settings, 20000, AiPromptFormatCost.QwenFim(64)));

        Assert.Multiple(() =>
        {
            Assert.That(result.Prefix, Is.EqualTo(AutocompleteContextBuilder.ModelPrefix(built)).Using(StringComparer.Ordinal));
            Assert.That(result.Suffix, Is.EqualTo(built.Suffix).Using(StringComparer.Ordinal));
            Assert.That(result.ExactCountPasses, Is.EqualTo(1), "Cabendo de sobra, uma única passagem de autorização basta.");
        });
    }

    [Test]
    public void ArgumentsAreValidated()
    {
        var tokenizer = new AdversarialBpeTokenizer();
        var cache = new TokenizedBlockCache(tokenizer, new TokenizerBoundaryOracle(tokenizer));
        var counter = new TokenizerTokenCounter(tokenizer);
        var estimator = new TokenRatioEstimator();
        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentNullException>(() => _ = new AiContextPipeline(null!, counter, estimator, cache));
            Assert.Throws<ArgumentNullException>(() => _ = new AiContextPipeline(EditorContextV1Contract.Instance, null!, estimator, cache));
            Assert.Throws<ArgumentNullException>(() => _ = new AiContextPipeline(EditorContextV1Contract.Instance, counter, null!, cache));
            Assert.Throws<ArgumentNullException>(() => _ = new AiContextPipeline(EditorContextV1Contract.Instance, counter, estimator, null!));
            Assert.Throws<ArgumentNullException>(() => _ = new AiPromptRequest(null!, new(), 1024, default));
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new AiPromptRequest(Snapshot(), new(), 4, AiPromptFormatCost.QwenFim(64)));
            Assert.Throws<ArgumentNullException>(() => new AiContextPipeline(EditorContextV1Contract.Instance, counter, estimator, cache).Build(null!));
        });
    }

    // --- Janela sintática de A32b como fonte do par prefixo/sufixo -------------------------------------------------

    /// <summary>
    /// Script com quatro statements antes do cursor. Quatro é o número que separa as duas fontes: o recorte por
    /// caracteres do contrato alcança o documento inteiro, enquanto a janela sintática para em três vizinhos.
    /// </summary>
    private static string WindowScript(string marker) =>
        "db.Antigo.find({ legado: 1 });\ndb.Orders.find({ status: 1 });\ndb.Products.countDocuments();\n"
        + "db.Customers.updateOne({ _id: 1 }, { $set: { ativo: true } });\ndb.Customers.find({ " + marker;

    private static AutocompleteContextSnapshot WindowSnapshot(string marker = "na")
        => new(WindowScript(marker), WindowScript(marker).Length, "JavaScript (mongosh)",
            KnownNames: ["Customers", "Orders", "Products"], RecentCommands: ["db.Customers.findOne()"]);

    private static CompletionContext Context(int caret) =>
        new(new(1, 1), EditorDialects.MongoshScript, SymbolKinds.Field, "", new(caret, 0));

    /// <summary>A janela construída pelo builder real de A32b sobre o mesmo texto do snapshot.</summary>
    private static EditorWindow WindowOf(string marker = "na")
    {
        var text = WindowScript(marker);
        return new EditorWindowBuilder().Build(Context(text.Length), new StringTextSnapshot(text));
    }

    private static AiPromptRequest WindowRequest(int contextTokens, EditorWindow? window, string marker = "na",
        AutocompleteSettings? settings = null)
        => new(WindowSnapshot(marker), settings ?? new AutocompleteSettings(), contextTokens, AiPromptFormatCost.QwenFim(64))
        {
            Facts = Facts(12),
            Window = window
        };

    private static int WindowHeaderTokens(AdversarialBpeTokenizer tokenizer, string marker = "na")
    {
        var built = AutocompleteContextBuilder.Build(WindowSnapshot(marker), new());
        return tokenizer.Encode(AutocompleteContextBuilder.ModelPrefix(built with { Prefix = "" })).Count;
    }

    /// <summary>
    /// A diferença entre as duas fontes é observável e tem uma causa única: a janela sintática limita o contexto a
    /// três statements inteiros, o recorte por caracteres do contrato não limita statement nenhum.
    /// </summary>
    [Test]
    public void TheSyntacticWindowReplacesTheCharacterWindowOfTheContract()
    {
        var tokenizer = new AdversarialBpeTokenizer();
        var pipeline = Pipeline(tokenizer, out _);
        var window = WindowOf();

        var withWindow = pipeline.Build(WindowRequest(20000, window));
        var withoutWindow = pipeline.Build(WindowRequest(20000, window: null));

        Assert.Multiple(() =>
        {
            Assert.That(withWindow.Success, Is.True);
            Assert.That(withWindow.Prefix, Is.Not.EqualTo(withoutWindow.Prefix).Using(StringComparer.Ordinal));
            Assert.That(withoutWindow.Prefix, Does.Contain("db.Antigo"), "Sem janela, o recorte por caracteres leva o documento inteiro.");
            Assert.That(withWindow.Prefix, Does.Not.Contain("db.Antigo"), "Com janela, o quarto statement mais antigo não acompanha o pedido.");
            Assert.That(withWindow.Prefix, Does.Contain("db.Products.countDocuments();"));
            Assert.That(withWindow.Prefix, Does.EndWith("db.Customers.find({ na"), "O cursor continua colado ao fim do prefixo.");
            Assert.That(withWindow.PrefixCharacters, Is.EqualTo(window.Render().Length),
                "Cabendo de sobra, o prefixo cortado é a forma canônica da janela inteira.");
        });
    }

    /// <summary>
    /// Cursor no meio de um statement: o sufixo é a cauda daquele statement, não os 1024 caracteres seguintes do
    /// documento. É a diferença que o formato FIM enxerga.
    /// </summary>
    [Test]
    public void TheSuffixIsTheTailOfTheCurrentStatementAndNotTheRestOfTheDocument()
    {
        var tokenizer = new AdversarialBpeTokenizer();
        var pipeline = Pipeline(tokenizer, out _);
        const string text = "db.Orders.find({ status: 1 });\ndb.Customers.find({ nome: 1 });\ndb.Depois.find({});";
        var caret = text.IndexOf("nome", StringComparison.Ordinal);
        var snapshot = new AutocompleteContextSnapshot(text, caret, "JavaScript (mongosh)");
        var window = new EditorWindowBuilder().Build(Context(caret), new StringTextSnapshot(text));
        var settings = new AutocompleteSettings();

        var withWindow = pipeline.Build(new(snapshot, settings, 20000, AiPromptFormatCost.QwenFim(64)) { Window = window });
        var withoutWindow = pipeline.Build(new(snapshot, settings, 20000, AiPromptFormatCost.QwenFim(64)));

        Assert.Multiple(() =>
        {
            Assert.That(withWindow.Suffix, Is.EqualTo("nome: 1 });").Using(StringComparer.Ordinal));
            Assert.That(withWindow.Prefix, Does.EndWith("db.Customers.find({ "));
            Assert.That(withoutWindow.Suffix, Does.Contain("db.Depois"), "O contrato leva o resto do documento como sufixo.");
            Assert.That(withWindow.Suffix, Does.Not.Contain("db.Depois"));
        });
    }

    /// <summary>Determinismo de prompt é determinismo de sequência: a mesma janela, por conteúdo, dá os mesmos bytes.</summary>
    [Test]
    public void TheSameEditorWindowProducesTheSameBytesColdAndHot()
    {
        var tokenizer = new AdversarialBpeTokenizer();
        var pipeline = Pipeline(tokenizer, out var cache);
        var budget = WindowHeaderTokens(tokenizer) + 200;

        var cold = pipeline.Build(WindowRequest(budget, WindowOf()));
        var cachedAfterCold = cache.CachedBlocks;
        var hot = pipeline.Build(WindowRequest(budget, WindowOf()));
        var otherPipeline = Pipeline(tokenizer, out _);
        // Janela equivalente por conteúdo, construída de novo: a igualdade do prompt não pode depender da referência.
        var freshCold = otherPipeline.Build(WindowRequest(budget, WindowOf()));

        Assert.Multiple(() =>
        {
            Assert.That(cachedAfterCold, Is.GreaterThan(0), "Sem cache povoado, frio e quente seriam o mesmo caso.");
            Assert.That(cold.Success, Is.True);
            Assert.That(cold.FactsDropped, Is.GreaterThan(0), "A margem escolhida precisa exercitar o corte.");
            Assert.That(hot.Prefix, Is.EqualTo(cold.Prefix).Using(StringComparer.Ordinal));
            Assert.That(hot.Suffix, Is.EqualTo(cold.Suffix).Using(StringComparer.Ordinal));
            Assert.That(freshCold.Prefix, Is.EqualTo(cold.Prefix).Using(StringComparer.Ordinal));
            Assert.That(freshCold.Suffix, Is.EqualTo(cold.Suffix).Using(StringComparer.Ordinal));
            Assert.That(hot.PromptTokens, Is.EqualTo(cold.PromptTokens));
        });
    }

    /// <summary>Mesma auditoria de privacidade da fonte antiga, agora com o sentinela dentro de um statement da janela.</summary>
    [Test]
    public void ASentinelInsideAWindowStatementDoesNotSurviveTheCall()
    {
        var tokenizer = new AdversarialBpeTokenizer();
        var pipeline = Pipeline(tokenizer, out var cache);
        var window = WindowOf(Sentinel);

        AiPromptResult? result = null;
        var measurements = RecordPromptTokens(() =>
            result = pipeline.Build(WindowRequest(WindowHeaderTokens(tokenizer, Sentinel) + 900, window, Sentinel)));

        Assert.Multiple(() =>
        {
            Assert.That(window.Render(), Does.Contain(Sentinel), "Sem o sentinela na janela a auditoria não prova nada.");
            Assert.That(result!.Prefix, Does.Contain(Sentinel));
            Assert.That(Reachable(pipeline), Has.None.Contains(Sentinel), "O pipeline reteve texto da janela.");
            Assert.That(Reachable(cache), Has.None.Contains(Sentinel), "O cache de blocos reteve texto da janela.");
            Assert.That(OtherStringProperties(result), Has.None.Contains(Sentinel));
            Assert.That(measurements.SelectMany(measurement => measurement.Tags)
                .Select(tag => tag.Value?.ToString() ?? ""), Has.None.Contains(Sentinel));
        });
    }

    /// <summary>O opt-out de contexto do editor vale para qualquer fonte; uma janela fornecida não o contorna.</summary>
    [Test]
    public void TheEditorContextOptOutAlsoTurnsOffTheSyntacticWindow()
    {
        var tokenizer = new AdversarialBpeTokenizer();
        var pipeline = Pipeline(tokenizer, out _);
        var settings = new AutocompleteSettings { UseEditorContext = false };

        var withWindow = pipeline.Build(WindowRequest(20000, WindowOf(), settings: settings));
        var withoutWindow = pipeline.Build(WindowRequest(20000, window: null, settings: settings));

        Assert.Multiple(() =>
        {
            Assert.That(withWindow.Prefix, Is.EqualTo(withoutWindow.Prefix).Using(StringComparer.Ordinal));
            Assert.That(withWindow.Suffix, Is.EqualTo(withoutWindow.Suffix).Using(StringComparer.Ordinal));
            Assert.That(withWindow.Prefix, Does.Not.Contain("db.Antigo"), "Com opt-out continua valendo o teto curto do contrato.");
        });
    }

    /// <summary>Compatibilidade: janela ausente ou vazia é o comportamento anterior, byte a byte.</summary>
    [Test]
    public void AnAbsentOrEmptyWindowKeepsTheContractSourceByteForByte()
    {
        var tokenizer = new AdversarialBpeTokenizer();
        var pipeline = Pipeline(tokenizer, out _);

        var empty = pipeline.Build(WindowRequest(20000, EditorWindow.Empty));
        var absent = pipeline.Build(WindowRequest(20000, window: null));
        var built = AutocompleteContextBuilder.Build(WindowSnapshot(), new());

        Assert.Multiple(() =>
        {
            Assert.That(empty.Prefix, Is.EqualTo(absent.Prefix).Using(StringComparer.Ordinal));
            Assert.That(empty.Suffix, Is.EqualTo(absent.Suffix).Using(StringComparer.Ordinal));
            Assert.That(absent.Prefix, Does.EndWith(built.Prefix));
            Assert.That(absent.Suffix, Is.EqualTo(built.Suffix).Using(StringComparer.Ordinal));
        });
    }

    private static List<(long Value, KeyValuePair<string, object?>[] Tags)> RecordPromptTokens(Action action)
    {
        var measurements = new List<(long Value, KeyValuePair<string, object?>[] Tags)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, active) =>
        {
            if (instrument.Meter.Name == AutocompleteMetrics.MeterName && instrument.Name == "inference.prompt_tokens")
                active.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) => measurements.Add((value, tags.ToArray())));
        listener.Start();
        action();
        listener.Dispose();
        return measurements;
    }

    private static List<string> OtherStringProperties(AiPromptResult result) =>
    [
        .. typeof(AiPromptResult).GetProperties()
            .Where(property => property.Name is not (nameof(AiPromptResult.Prefix) or nameof(AiPromptResult.Suffix)))
            .Select(property => property.GetValue(result)?.ToString() ?? "")
    ];

    /// <summary>Todas as cadeias alcançáveis por campos a partir de um objeto, com limite de profundidade.</summary>
    private static List<string> Reachable(object root)
    {
        var found = new List<string>();
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        Walk(root, 6);
        return found;

        void Walk(object? value, int depth)
        {
            if (value is null || depth == 0) return;
            if (value is string text) { found.Add(text); return; }
            var type = value.GetType();
            if (type.IsPrimitive || value is decimal or DateTime or Type or Delegate) return;
            if (!type.IsValueType && !seen.Add(value)) return;
            if (value is IEnumerable items) { foreach (var item in items) Walk(item, depth - 1); return; }
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                Walk(field.GetValue(value), depth - 1);
        }
    }
}
