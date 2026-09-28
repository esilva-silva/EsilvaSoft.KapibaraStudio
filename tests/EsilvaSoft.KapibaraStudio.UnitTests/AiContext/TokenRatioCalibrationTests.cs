using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.AiContext;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Facts;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;
using EsilvaSoft.KapibaraStudio.UnitTests.TokenCounting;

namespace EsilvaSoft.KapibaraStudio.UnitTests.AiContext;

/// <summary>
/// Calibração da razão caracteres/token contra o tokenizer real.
/// </summary>
/// <remarks>
/// <para>O corpus é sintético e local em vez de reaproveitar <c>EditorContextV1Corpus</c> por dois motivos: aquele
/// corpus existe para congelar bytes do contrato e tem um caso de 90 000 caracteres, cujo custo no mini-BPE de
/// referência é quadrático; e acoplar a calibração a ele faria uma mudança de fixture do teste-ouro mexer no valor
/// calibrado. Os textos abaixo cobrem as formas que o prompt realmente tem: cabeçalho v1, mongosh, JSON de pipeline,
/// comentário em pt-BR acentuado e identificadores não latinos.</para>
/// <para><strong>Tolerância declarada.</strong> A razão recomendada precisa (a) não subestimar mais de 5% das
/// amostras — é a definição do percentil escolhido — e (b) ficar dentro de 60% acima da razão média do corpus. O teto é
/// largo porque em um BPE byte-level a cauda é dominada por texto não latino, cujo custo por caractere UTF-16 é várias
/// vezes o do ASCII; o teto
/// existe porque uma razão muito acima da média desperdiça contexto em todo prompt; o piso é a média, porque
/// subestimar obriga o pipeline a recontar depois de cortar.</para>
/// </remarks>
[TestFixture]
public sealed class TokenRatioCalibrationTests
{
    private static readonly string[] Samples =
    [
        AutocompleteContextBuilder.ModelPrefix(AutocompleteContextBuilder.Build(new("db.Customers.find({ na", 22, "JavaScript (mongosh)"), new())),
        "db.getCollection(\"Customers\").find({ status: \"active\" }).sort({ createdAt: -1 }).limit(50)",
        "[{ \"$match\": { \"status\": \"active\" } }, { \"$group\": { \"_id\": \"$customerId\", \"total\": { \"$sum\": \"$amount\" } } }]",
        "// Consulta de faturamento por região; não inclui devoluções nem cancelamentos já conciliados.",
        "const pedidos = db.Pedidos.aggregate(pipeline).toArray(); print(pedidos.length);",
        "campo colecao:loja.Customers accountId: objectId [objectId] presenca=0.98",
        "db.用户.find({ 姓名: /^张/ })",
        "{\"_id\":{\"$oid\":\"64f0c2a1b3d4e5f6a7b8c9d0\"},\"criadoEm\":{\"$date\":\"2026-01-31T12:00:00Z\"}}",
        "informação de configuração da conexão à produção, com acentuação portuguesa completa: ção, ãe, ê, ô",
        "function resumo(colecao) {\n  return db.getCollection(colecao).countDocuments({});\n}"
    ];

    private static TokenizerTokenCounter Counter(out AdversarialBpeTokenizer tokenizer)
    {
        tokenizer = new AdversarialBpeTokenizer();
        return new(tokenizer);
    }

    [Test]
    public void TheMeanMatchesWhatTheRealCounterMeasures()
    {
        var counter = Counter(out var tokenizer);
        var report = TokenRatioCalibration.Calibrate(counter, Samples);
        var measuredTokens = Samples.Sum(sample => (long)tokenizer.Encode(sample).Count);
        var measuredCharacters = Samples.Sum(sample => (long)sample.Length);

        Assert.Multiple(() =>
        {
            Assert.That(report.SampleCount, Is.EqualTo(Samples.Length));
            Assert.That(report.TotalTokens, Is.EqualTo(measuredTokens));
            Assert.That(report.TotalCharacters, Is.EqualTo(measuredCharacters));
            Assert.That(report.Mean, Is.EqualTo((double)measuredTokens / measuredCharacters).Within(1e-12));
            Assert.That(report.Minimum, Is.LessThanOrEqualTo(report.Mean));
            Assert.That(report.Maximum, Is.GreaterThanOrEqualTo(report.Mean));
        });
    }

    [Test]
    public void TheCalibratedRatioIsNotTheGuessedDefault()
    {
        var report = TokenRatioCalibration.Calibrate(Counter(out _), Samples);

        Assert.Multiple(() =>
        {
            Assert.That(Math.Abs(report.Recommended - TokenRatioEstimator.DefaultTokensPerCharacter), Is.GreaterThan(0.1),
                "Se o chute inicial já servisse, calibrar não teria sentido.");
            Assert.That(report.Recommended, Is.GreaterThanOrEqualTo(report.Mean));
            Assert.That(report.Recommended, Is.LessThanOrEqualTo(report.Mean * 1.6), "Razão calibrada desperdiçaria contexto demais.");
            Assert.That(report.Recommended, Is.EqualTo(report.Percentile));
            Assert.That(report.PercentileRank, Is.EqualTo(TokenRatioCalibration.DefaultPercentile));
        });
    }

    /// <summary>O percentil escolhido é exatamente a promessa de não subestimar: no máximo 5% das amostras ficam acima dele.</summary>
    [Test]
    public void TheCalibratedEstimatorDoesNotUnderestimateTheCorpus()
    {
        var counter = Counter(out var tokenizer);
        var estimator = TokenRatioCalibration.CreateEstimator(counter, Samples);
        var underestimated = Samples.Count(sample => estimator.Estimate(sample) < tokenizer.Encode(sample).Count);

        Assert.Multiple(() =>
        {
            Assert.That(underestimated, Is.LessThanOrEqualTo((int)Math.Ceiling(Samples.Length * (1 - TokenRatioCalibration.DefaultPercentile))));
            Assert.That(estimator.TokensPerCharacter, Is.EqualTo(TokenRatioCalibration.Calibrate(counter, Samples).Recommended));
        });
    }

    /// <summary>O default chutado erra por muito neste tokenizer; é a evidência de que calibrar muda o comportamento do corte.</summary>
    [Test]
    public void TheGuessedDefaultUnderestimatesWhereTheCalibratedRatioDoesNot()
    {
        var counter = Counter(out var tokenizer);
        var guessed = new TokenRatioEstimator();
        var calibrated = TokenRatioCalibration.CreateEstimator(counter, Samples);
        var guessedMisses = Samples.Count(sample => guessed.Estimate(sample) < tokenizer.Encode(sample).Count);
        var calibratedMisses = Samples.Count(sample => calibrated.Estimate(sample) < tokenizer.Encode(sample).Count);

        Assert.That(calibratedMisses, Is.LessThan(guessedMisses));
    }

    [TestCase(0.0)]
    [TestCase(0.5)]
    [TestCase(1.0)]
    public void ThePercentileIsMonotonicInTheRequestedRank(double rank)
    {
        var report = TokenRatioCalibration.Calibrate(Counter(out _), Samples, rank);

        Assert.Multiple(() =>
        {
            Assert.That(report.Recommended, Is.GreaterThanOrEqualTo(report.Minimum));
            Assert.That(report.Recommended, Is.LessThanOrEqualTo(report.Maximum));
            Assert.That(report.PercentileRank, Is.EqualTo(rank));
        });
    }

    [Test]
    public void TheReportIsReadableAndCultureInvariant()
    {
        var report = TokenRatioCalibration.Calibrate(Counter(out _), Samples).Format();

        Assert.Multiple(() =>
        {
            Assert.That(report, Does.Contain("recomendada="));
            Assert.That(report, Does.Contain("media="));
            Assert.That(report, Does.Contain("amostras=" + Samples.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            Assert.That(report, Does.Not.Contain(","), "Separador decimal de cultura local vazaria para um relatório.");
        });
    }

    [Test]
    public void EmptySamplesAreIgnoredAndAnEmptyCorpusIsRefused()
    {
        var counter = Counter(out _);
        var withBlanks = TokenRatioCalibration.Calibrate(counter, ["", .. Samples, ""]);

        Assert.Multiple(() =>
        {
            Assert.That(withBlanks.SampleCount, Is.EqualTo(Samples.Length));
            Assert.Throws<ArgumentException>(() => TokenRatioCalibration.Calibrate(counter, [""]));
            Assert.Throws<ArgumentNullException>(() => TokenRatioCalibration.Calibrate(null!, Samples));
            Assert.Throws<ArgumentNullException>(() => TokenRatioCalibration.Calibrate(counter, null!));
            Assert.Throws<ArgumentNullException>(() => TokenRatioCalibration.Calibrate(counter, [null!]));
            Assert.Throws<ArgumentOutOfRangeException>(() => TokenRatioCalibration.Calibrate(counter, Samples, 1.5));
            Assert.Throws<ArgumentOutOfRangeException>(() => TokenRatioCalibration.Calibrate(counter, Samples, -0.1));
        });
    }
}
