namespace EsilvaSoft.KapibaraStudio.Core;

/// <summary>
/// Additive to session version 1. <see cref="Bindings"/> overrides <see cref="Defaults"/> per command: a command without an
/// entry keeps its defaults and an empty list leaves it unbound. Unknown commands, malformed gestures, null lists, a gesture
/// bound twice <em>inside the same <see cref="EditorCommandScope"/></em> and versions other than 1 are invalid and make the
/// whole session unreadable. The same gesture in two different scopes is legal and is resolved by scope precedence at the
/// call site, which is what lets Tab accept a completion item, jump to the next snippet placeholder and accept ghost text.
/// </summary>
public sealed record EditorKeyBindings
{
    public const int CurrentVersion = 1;
    public int Version { get; init; } = CurrentVersion;
    public Dictionary<string, string[]> Bindings { get; init; } = [];

    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Defaults { get; } = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
    {
        [EditorCommandIds.CompletionShow] = ["Ctrl+Space"],
        [EditorCommandIds.CompletionAi] = ["Ctrl+;"],
        [EditorCommandIds.CompletionNext] = ["Down"],
        [EditorCommandIds.CompletionPrevious] = ["Up"],
        [EditorCommandIds.CompletionAccept] = ["Tab"],
        [EditorCommandIds.CompletionAcceptEnter] = ["Enter"],
        [EditorCommandIds.CompletionClose] = ["Escape"],
        [EditorCommandIds.SnippetNext] = ["Tab"],
        [EditorCommandIds.SnippetPrevious] = ["Shift+Tab"],
        [EditorCommandIds.SnippetCancel] = ["Escape"],
        [EditorCommandIds.InlineAccept] = ["Tab"],
        [EditorCommandIds.InlineDismiss] = ["Escape"]
    };

    /// <summary>Throws <see cref="InvalidDataException"/> with a visible reason; returns this instance when valid.</summary>
    public EditorKeyBindings Validate()
    {
        _ = Build(this);
        return this;
    }

    /// <summary>Effective gestures for every known command; null (absent from the session) yields the defaults.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<EditorKeyGesture>> Resolve(EditorKeyBindings? bindings) => Build(bindings);

    private static Dictionary<string, IReadOnlyList<EditorKeyGesture>> Build(EditorKeyBindings? bindings)
    {
        if (bindings is not null)
        {
            if (bindings.Version != CurrentVersion) throw Invalid($"versão {bindings.Version} não suportada.");
            if (bindings.Bindings is null) throw Invalid("lista de atalhos ausente.");
            foreach (var command in bindings.Bindings.Keys)
                if (!EditorCommandIds.TryGetScope(command, out _)) throw Invalid($"comando '{command}' desconhecido.");
        }
        var effective = new Dictionary<string, IReadOnlyList<EditorKeyGesture>>(StringComparer.Ordinal);
        var owners = new Dictionary<(EditorCommandScope Scope, EditorKeyGesture Gesture), string>();
        foreach (var command in EditorCommandIds.All)
        {
            _ = EditorCommandIds.TryGetScope(command, out var scope);
            string[]? custom = null;
            var overridden = bindings is not null && bindings.Bindings.TryGetValue(command, out custom);
            IReadOnlyList<string>? texts = overridden ? custom : Defaults[command];
            if (texts is null) throw Invalid($"lista de gestos nula para '{command}'.");
            var gestures = new EditorKeyGesture[texts.Count];
            for (var index = 0; index < texts.Count; index++)
            {
                if (!EditorKeyGesture.TryParse(texts[index], out var gesture, out var error)) throw Invalid($"{error} (comando '{command}').");
                if (!owners.TryAdd((scope, gesture), command))
                {
                    var owner = owners[(scope, gesture)];
                    throw Invalid(owner == command
                        ? $"gesto '{gesture}' repetido em '{command}' (escopo {scope})."
                        : $"gesto '{gesture}' atribuído a '{owner}' e '{command}' no mesmo escopo {scope}.");
                }
                gestures[index] = gesture;
            }
            effective[command] = gestures;
        }
        return effective;
    }

    private static InvalidDataException Invalid(string reason) => new("Preferência de atalhos do editor inválida: " + reason);
}
