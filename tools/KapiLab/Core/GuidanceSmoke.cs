using System.Text.Json.Serialization;
using Microsoft.ML.OnnxRuntimeGenAI;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Core;

/// <summary>
/// Small adapter for the GenAI guidance API plus deterministic result classification.
/// It does not load a model or claim that generation followed the supplied grammar.
/// </summary>
internal static class GuidanceSmoke
{
    public const string ResultSchema = "kapilab-guidance-smoke-v1";

    public static string ClassifyStatus(bool supported, int? guidedOutputTokens) =>
        supported ? "supported" : guidedOutputTokens == 0 ? "inconclusive" : "not_supported";

    /// <summary>Applies caller-provided Lark grammar through the GenAI 0.15.2 API.</summary>
    public static void ApplyLarkGrammar(GeneratorParams parameters, string grammar)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentException.ThrowIfNullOrWhiteSpace(grammar);
        try
        {
            parameters.SetGuidance("lark_grammar", grammar, enableFFTokens: false);
        }
        catch (Exception exception)
        {
            // Native errors can include input text. Preserve only the exception type.
            throw new GuidanceUnavailableException(exception.GetType().Name);
        }
    }

    /// <summary>
    /// Runs a supplied measurement delegate. The caller must use the same prompt/model for
    /// baseline and guided measurements and may report grammar conformance only if it has
    /// an independent authoritative check.
    /// </summary>
    public static GuidanceSmokeResult Measure(string prompt, string grammar,
        Func<string, string, GuidanceSmokeMeasurement> measure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        ArgumentException.ThrowIfNullOrWhiteSpace(grammar);
        ArgumentNullException.ThrowIfNull(measure);
        try
        {
            var observation = measure(prompt, grammar);
            if (!observation.Supported)
                return Unsupported(observation.Error);

            double? overhead = observation.OutputTokens > 0
                && observation.BaselineElapsed >= TimeSpan.Zero
                && observation.GuidedElapsed >= TimeSpan.Zero
                ? (observation.GuidedElapsed - observation.BaselineElapsed).TotalMicroseconds / observation.OutputTokens
                : null;
            return new(ResultSchema, true, Sanitize(observation.Error), observation.GrammarOk, overhead);
        }
        catch (OperationCanceledException) { throw; }
        catch (GuidanceUnavailableException)
        {
            return Unsupported("Guidance indisponível; detalhes omitidos.");
        }
    }

    private static GuidanceSmokeResult Unsupported(string? error) =>
        new(ResultSchema, false, Sanitize(error) ?? "Guidance indisponível.", null, null);

    private static string? Sanitize(string? error)
    {
        if (string.IsNullOrWhiteSpace(error)) return null;
        // Never emit exception messages, which may contain model paths, prompt, or grammar.
        return error is "Guidance indisponível." or "Falha no guidance (detalhes omitidos)."
            ? error
            : "Falha no guidance (detalhes omitidos).";
    }

}

internal sealed record GuidanceSmokeResult(
    [property: JsonPropertyName("schema")] string Schema,
    [property: JsonPropertyName("supported")] bool Supported,
    [property: JsonPropertyName("error")] string? Error,
    [property: JsonPropertyName("grammar_ok")] bool? GrammarOk,
    [property: JsonPropertyName("overhead_us_per_token")] double? OverheadUsPerToken);

internal sealed record GuidanceSmokeMeasurement(
    bool Supported,
    string? Error,
    bool? GrammarOk,
    TimeSpan BaselineElapsed,
    TimeSpan GuidedElapsed,
    int OutputTokens);

internal sealed class GuidanceUnavailableException(string exceptionType)
    : Exception($"Guidance indisponível ({exceptionType}).");
