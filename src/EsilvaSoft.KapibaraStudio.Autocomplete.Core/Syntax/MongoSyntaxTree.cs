using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Text;

namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core.Syntax;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1720", Justification = "Object is a syntax category, not a type name in an API.")]
public enum MongoSyntaxNodeKind : byte
{
    Document, Statement, Token, Object, Array, Group, OpaqueStatement
}

public enum MongoSyntaxDiagnosticKind : byte
{
    MissingClose, SkippedTokens, Unterminated, DottedKeyRecovery
}

public readonly record struct MongoSyntaxDiagnostic(
    MongoSyntaxDiagnosticKind Kind, TextSpan Span, string Message);

/// <summary>A compact, immutable, tolerant syntax node. Child spans are always within the parent span.</summary>
public sealed class MongoSyntaxNode
{
    public MongoSyntaxNode(MongoSyntaxNodeKind kind, TextSpan span, IReadOnlyList<MongoSyntaxNode> children = null!, MongoToken? token = null)
    {
        Kind = kind;
        Span = span;
        Children = children is null ? [] : children.ToArray();
        Token = token;
    }

    public MongoSyntaxNodeKind Kind { get; }
    public TextSpan Span { get; }
    public IReadOnlyList<MongoSyntaxNode> Children { get; }
    public MongoToken? Token { get; }

    public IEnumerable<MongoSyntaxNode> Descendants()
    {
        foreach (var child in Children)
        {
            yield return child;
            foreach (var descendant in child.Descendants()) yield return descendant;
        }
    }
}

public sealed class MongoSyntaxTree
{
    public MongoSyntaxTree(TextSnapshotVersion version, MongoSyntaxNode root,
        IReadOnlyList<MongoToken> tokens, IReadOnlyList<MongoSyntaxDiagnostic> diagnostics, int reusedStatementCount = 0)
    {
        Version = version;
        Root = root;
        Tokens = tokens.ToArray();
        Diagnostics = diagnostics.ToArray();
        ReusedStatementCount = reusedStatementCount;
    }

    public TextSnapshotVersion Version { get; }
    public MongoSyntaxNode Root { get; }
    public IReadOnlyList<MongoToken> Tokens { get; }
    public IReadOnlyList<MongoSyntaxDiagnostic> Diagnostics { get; }
    /// <summary>Number of statement nodes reused from the preceding version by incremental parsing.</summary>
    public int ReusedStatementCount { get; }
}
