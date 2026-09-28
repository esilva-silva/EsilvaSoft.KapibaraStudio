using System.Globalization;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Syntax;

namespace EsilvaSoft.KapibaraStudio.UnitTests.Language.Syntax;

[TestFixture]
public sealed class MongoLexerTests
{
    // Non-ASCII characters are built from code points so offsets never depend on source encoding or normalization.
    private static readonly string CCedilla = C(0x00E7), ATilde = C(0x00E3), Name = C(0x540D) + C(0x524D), Emoji = char.ConvertFromUtf32(0x1F600);

    [Test]
    public void TokensCarryLexicalKindsAndLookaheadTraitsWithoutMongoMeaning()
    {
        const string text = "db.Projects.find({ $match: 1, \"k\": 'v' })";
        var tokens = Lex(text);
        Assert.That(Describe(text, tokens), Is.EqualTo(Items(
            "Identifier:db", "Punctuation:.", "Identifier:Projects", "Punctuation:.", "Identifier:find", "Punctuation:(", "Punctuation:{",
            "Identifier:$match", "Punctuation::", "Number:1", "Punctuation:,", "String:\"k\"", "Punctuation::", "String:'v'", "Punctuation:}", "Punctuation:)")));
        Assert.That(tokens[4].Traits, Is.EqualTo(MongoTokenTraits.FollowedByOpenParenthesis));
        Assert.That(tokens[7].Traits, Is.EqualTo(MongoTokenTraits.FollowedByColon));
        Assert.That(tokens[11].Traits, Is.EqualTo(MongoTokenTraits.FollowedByColon));
        Assert.That(tokens[13].Traits, Is.EqualTo(MongoTokenTraits.None));
    }

    [Test]
    public void UnterminatedStringsAndTemplatesContinueOnTheNextLineThroughTheState()
    {
        const string text = "x = \"abc\ndef\" + `t ${a}\nu` // c";
        var (tokens, states) = LexLines(text);
        Assert.That(Describe(text, tokens), Is.EqualTo(Items(
            "Identifier:x", "Operator:=", "String:\"abc\n", "String:def\"", "Operator:+", "Template:`t ${a}\n", "Template:u`", "LineComment:// c")));
        Assert.That(tokens[2].Traits, Is.EqualTo(MongoTokenTraits.Unterminated));
        Assert.That(tokens[3].Traits, Is.EqualTo(MongoTokenTraits.Continuation));
        Assert.That(tokens[5].Traits, Is.EqualTo(MongoTokenTraits.Unterminated));
        Assert.That(tokens[6].Traits, Is.EqualTo(MongoTokenTraits.Continuation));
        Assert.That(states.Select(s => s.OpenQuote), Is.EqualTo(Items('"', '`', '\0')));
        Assert.That(states.Select(s => s.IsCodeContext), Is.EqualTo(Items(false, false, true)));
    }

    [Test]
    public void BackslashAtTheEndOfALineKeepsTheStringOpen()
    {
        const string text = "'a\\\nb' c";
        var (tokens, states) = LexLines(text);
        Assert.That(Describe(text, tokens), Is.EqualTo(Items("String:'a\\\n", "String:b'", "Identifier:c")));
        Assert.That(tokens[0].IsTerminated, Is.False);
        Assert.That(states[0].OpenQuote, Is.EqualTo('\''));
    }

    [Test]
    public void BlockCommentsSpanLinesButLineCommentsNeverOpenThem()
    {
        const string text = "/* a\n b */ c\n// x /*\ny";
        var (tokens, states) = LexLines(text);
        Assert.That(Describe(text, tokens), Is.EqualTo(Items("BlockComment:/* a\n", "BlockComment: b */", "Identifier:c", "LineComment:// x /*\n", "Identifier:y")));
        Assert.That(tokens[0].Traits, Is.EqualTo(MongoTokenTraits.Unterminated));
        Assert.That(tokens[1].Traits, Is.EqualTo(MongoTokenTraits.Continuation));
        Assert.That(tokens.Count(t => t.IsComment), Is.EqualTo(3));
        Assert.That(states.Select(s => s.InBlockComment), Is.EqualTo(Items(true, false, false, false)));
    }

    [Test]
    public void SlashIsRegexOnlyWhereAnOperandIsExpected()
    {
        const string text = "a = b / c / 2;\nr = /re[/]x/gi.test(s)\nreturn /r/m\n(a) / 2";
        Assert.That(Describe(text, Lex(text)), Is.EqualTo(Items(
            "Identifier:a", "Operator:=", "Identifier:b", "Operator:/", "Identifier:c", "Operator:/", "Number:2", "Punctuation:;",
            "Identifier:r", "Operator:=", "Regex:/re[/]x/gi", "Punctuation:.", "Identifier:test", "Punctuation:(", "Identifier:s", "Punctuation:)",
            "Identifier:return", "Regex:/r/m",
            "Punctuation:(", "Identifier:a", "Punctuation:)", "Operator:/", "Number:2")));
        Assert.That(Describe("{\"a\": /x/}", Lex("{\"a\": /x/}", MongoLexerMode.Json)), Does.Not.Contain("Regex:/x/"), "JSON never has regex literals.");
        // Compatibility with the previous highlighter: an arrow body starting with a regex is lexed as division.
        Assert.That(Lex("x => /y/").Count(t => t.Kind == MongoTokenKind.Regex), Is.Zero);
        Assert.That(Lex("/y/ + 1")[0].Kind, Is.EqualTo(MongoTokenKind.Regex), "default state is the start of a document");
    }

    [Test]
    public void NegativeNumbersOnlyWhereAnOperandIsExpected()
    {
        const string text = "[-1, a -1, 1e-5, 0x1F, 1_000, -x]";
        Assert.That(Describe(text, Lex(text)), Is.EqualTo(Items(
            "Punctuation:[", "Number:-1", "Punctuation:,", "Identifier:a", "Operator:-", "Number:1", "Punctuation:,", "Number:1e-5", "Punctuation:,",
            "Number:0", "Identifier:x1F", "Punctuation:,", "Number:1_000", "Punctuation:,", "Operator:-", "Identifier:x", "Punctuation:]")));
    }

    [Test]
    public void OffsetsAreUtf16CodeUnitsForAccentsCjkAndSurrogatePairs()
    {
        var text = "{ A" + CCedilla + ATilde + "o: \"S" + ATilde + "o " + Emoji + "\", " + Name + ": 1 }\nx";
        var tokens = Lex(text);
        Assert.That(tokens.Select(t => (t.Start, t.Length, t.Kind)), Is.EqualTo(Items(
            (0, 1, MongoTokenKind.Punctuation), (2, 4, MongoTokenKind.Identifier), (6, 1, MongoTokenKind.Punctuation), (8, 8, MongoTokenKind.String),
            (16, 1, MongoTokenKind.Punctuation), (18, 2, MongoTokenKind.Identifier), (20, 1, MongoTokenKind.Punctuation), (22, 1, MongoTokenKind.Number),
            (24, 1, MongoTokenKind.Punctuation), (26, 1, MongoTokenKind.Identifier))));
        Assert.That(tokens[1].Has(MongoTokenTraits.FollowedByColon) && tokens[5].Has(MongoTokenTraits.FollowedByColon), Is.True);
        var emoji = Lex("e: " + Emoji + "x");
        Assert.That(emoji.Select(t => (t.Start, t.Length)), Is.EqualTo(Items((0, 1), (1, 1), (3, 1), (4, 1), (5, 1))), "surrogates outside literals never shift later offsets");
    }

    [Test]
    public void CrlfStaysInsideTheLineAndLookaheadNeverCrossesLines()
    {
        const string text = "a\r\n\"k\" :\r\nx\r\n: 1 // c\r\ny";
        var tokens = Lex(text);
        Assert.That(tokens.Select(t => (t.Start, t.Length, t.Kind, t.Traits)), Is.EqualTo(Items(
            (0, 1, MongoTokenKind.Identifier, MongoTokenTraits.None), (3, 3, MongoTokenKind.String, MongoTokenTraits.FollowedByColon),
            (7, 1, MongoTokenKind.Punctuation, MongoTokenTraits.None), (10, 1, MongoTokenKind.Identifier, MongoTokenTraits.None),
            (13, 1, MongoTokenKind.Punctuation, MongoTokenTraits.None), (15, 1, MongoTokenKind.Number, MongoTokenTraits.None),
            (17, 6, MongoTokenKind.LineComment, MongoTokenTraits.None), (23, 1, MongoTokenKind.Identifier, MongoTokenTraits.None))));
    }

    [Test]
    public void EqualLineStatesProduceEqualTokensSoLinesCanBeReused()
    {
        var afterCall = EndState("db.Projects.find({ a: 1 })\n");
        var afterOtherCall = EndState("print(total)\n");
        var afterAssignment = EndState("const r =\n");
        Assert.That(afterCall, Is.EqualTo(afterOtherCall));
        Assert.That(afterCall, Is.Not.EqualTo(afterAssignment));
        const string line = "/x/ - 1";
        Assert.That(LexLine(line, afterCall), Is.EqualTo(LexLine(line, afterOtherCall)));
        Assert.That(LexLine(line, afterCall)[0].Kind, Is.EqualTo(MongoTokenKind.Operator));
        Assert.That(LexLine(line, afterAssignment)[0].Kind, Is.EqualTo(MongoTokenKind.Regex));
    }

    [TestCase(3), TestCase(5), TestCase(7), TestCase(11), TestCase(13)]
    public void RandomTextsAreCoveredInOrderAndResumeFromAnyLineBoundary(int seed)
    {
        var alphabet = "{}[]()\"'`/\\*:,;.$-+=!<>0123456789eExab_ \n\r\t" + CCedilla + Name + Emoji;
        var random = new HighlightingGoldenCorpus.SplitMix64((ulong)seed);
        var text = new string(Enumerable.Range(0, 4096).Select(_ => alphabet[random.Next(alphabet.Length)]).ToArray());
        foreach (var mode in new[] { MongoLexerMode.Script, MongoLexerMode.Json })
        {
            var tokens = Lex(text, mode);
            var covered = new bool[text.Length];
            for (var index = 0; index < tokens.Count; index++)
            {
                var token = tokens[index];
                Assert.That(token.Length, Is.Positive, Describe(seed, token));
                Assert.That(token.End, Is.LessThanOrEqualTo(text.Length), Describe(seed, token));
                if (index > 0) Assert.That(token.Start, Is.GreaterThanOrEqualTo(tokens[index - 1].End), Describe(seed, token));
                for (var position = token.Start; position < token.End; position++) covered[position] = true;
            }
            Assert.That(Enumerable.Range(0, text.Length).Where(i => !covered[i]).All(i => char.IsWhiteSpace(text[i])), Is.True, "seed " + seed);

            var boundaries = Enumerable.Range(0, text.Length).Where(i => text[i] == '\n').Select(i => i + 1).ToArray();
            var split = boundaries[random.Next(boundaries.Length)];
            var prefix = new List<MongoToken>();
            var state = MongoLexer.Tokenize(text.AsSpan(0, split), prefix, mode: mode);
            var suffix = new List<MongoToken>();
            MongoLexer.Tokenize(text.AsSpan(split), suffix, state, mode);
            Assert.That(prefix.Concat(suffix.Select(t => t with { Start = t.Start + split })), Is.EqualTo(tokens), $"seed {seed}, split {split}");
        }
    }

    [Test]
    public void CancellationStopsTheLexer()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var text = new string('x', 10_000);
        Assert.Throws<OperationCanceledException>(() => MongoLexer.Tokenize(text, new List<MongoToken>(), cancellationToken: cancelled.Token));
    }

    private static string C(int codeUnit) => ((char)codeUnit).ToString();

    private static T[] Items<T>(params T[] items) => items;

    private static List<MongoToken> Lex(string text, MongoLexerMode mode = MongoLexerMode.Script)
    {
        var tokens = new List<MongoToken>();
        MongoLexer.Tokenize(text, tokens, mode: mode);
        return tokens;
    }

    private static (List<MongoToken> Tokens, List<MongoLexerState> States) LexLines(string text)
    {
        var tokens = new List<MongoToken>();
        var states = new List<MongoLexerState>();
        var state = default(MongoLexerState);
        for (var start = 0; start < text.Length;)
        {
            var length = MongoLexer.LineLength(text, start);
            var lexer = new MongoLexer(text.AsSpan(start, length), state, offset: start);
            while (lexer.TryRead(out var token)) tokens.Add(token);
            state = lexer.State; states.Add(state);
            start += length;
        }
        return (tokens, states);
    }

    private static List<MongoToken> LexLine(string line, MongoLexerState state)
    {
        var tokens = new List<MongoToken>();
        var lexer = new MongoLexer(line, state);
        while (lexer.TryRead(out var token)) tokens.Add(token);
        return tokens;
    }

    private static MongoLexerState EndState(string text) => MongoLexer.Tokenize(text, new List<MongoToken>());

    private static string[] Describe(string text, IEnumerable<MongoToken> tokens) =>
        tokens.Select(t => t.Kind + ":" + text.Substring(t.Start, t.Length)).ToArray();

    private static string Describe(int seed, MongoToken token) =>
        string.Create(CultureInfo.InvariantCulture, $"seed {seed}: {token}");
}
