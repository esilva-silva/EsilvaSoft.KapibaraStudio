using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Infrastructure;

public sealed partial class LiteDbConnectionProfileRepository
{
    public Task<LegacyCredentialInventory> ReadAsync(CancellationToken cancellationToken = default) =>
        RunAsync(() =>
        {
            // RunAsync holds _gate across both reads, including the cached vault snapshot updated by SaveEnvironments.
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var findings = new List<LegacyCredentialFinding>();
                foreach (var profile in _database.GetCollection<ConnectionProfileDocument>(CollectionName).FindAll())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ClassifyProfile(profile, findings);
                }

                var vault = LoadEnvironments();
                cancellationToken.ThrowIfCancellationRequested();
                var valueCount = checked(vault.Environments.Sum(environment => environment.Values.Count));
                return new LegacyCredentialInventory(findings.AsReadOnly(), valueCount);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                // LiteDB and JSON parser exceptions may contain snippets of persisted credentials.
                throw new InvalidDataException("Inventário de credenciais legadas ilegível.");
            }
        }, cancellationToken);

    private static void ClassifyProfile(ConnectionProfileDocument profile, List<LegacyCredentialFinding> findings)
    {
        var uri = profile.ConnectionString;
        if (uri.Contains("${", StringComparison.Ordinal) || uri.Contains("ENV.get(", StringComparison.Ordinal))
            findings.Add(new LegacyCredentialFinding(profile.Id, LegacyCredentialCategory.DynamicUriReference));

        var schemeEnd = uri.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd < 0)
        {
            findings.Add(new LegacyCredentialFinding(profile.Id, LegacyCredentialCategory.UnrecognizedUriUserInfo));
            return;
        }

        var authorityStart = schemeEnd + 3;
        var authorityEnd = uri.IndexOfAny(['/', '?', '#'], authorityStart);
        if (authorityEnd < 0) authorityEnd = uri.Length;
        var authority = uri.AsSpan(authorityStart, authorityEnd - authorityStart);
        var userInfoEnd = authority.LastIndexOf('@');
        if (userInfoEnd < 0)
        {
            // An unescaped URI delimiter in userinfo can hide the '@' beyond the apparent authority.
            if (authorityEnd < uri.Length && authority.Contains(':'))
            {
                var remainder = uri.AsSpan(authorityEnd + 1);
                var queryOrFragment = remainder.IndexOfAny('?', '#');
                if (queryOrFragment >= 0) remainder = remainder[..queryOrFragment];
                if (remainder.Contains('@'))
                    findings.Add(new LegacyCredentialFinding(profile.Id, LegacyCredentialCategory.UnrecognizedUriUserInfo));
            }
            return;
        }

        var userInfo = authority[..userInfoEnd];
        var separator = userInfo.IndexOf(':');
        if (separator >= 0 && separator + 1 < userInfo.Length)
        {
            var password = userInfo[(separator + 1)..];
            if (password.Contains("${", StringComparison.Ordinal) || password.Contains("ENV.get(", StringComparison.Ordinal))
            {
                if (!IsOnlyDynamicPassword(password))
                    findings.Add(new LegacyCredentialFinding(profile.Id, LegacyCredentialCategory.UnrecognizedUriUserInfo));
            }
            else
                findings.Add(new LegacyCredentialFinding(profile.Id, LegacyCredentialCategory.InlineUriPassword));
        }
        else if (separator < 0)
            findings.Add(new LegacyCredentialFinding(profile.Id, LegacyCredentialCategory.UnrecognizedUriUserInfo));
    }

    private static bool IsOnlyDynamicPassword(ReadOnlySpan<char> password)
    {
        if (!password.StartsWith("${", StringComparison.Ordinal) || !password.EndsWith("}", StringComparison.Ordinal))
            return false;

        var expression = password[2..^1];
        if (expression.StartsWith("ENV.get(", StringComparison.Ordinal) && expression.EndsWith(")", StringComparison.Ordinal))
        {
            var argument = expression[8..^1].Trim();
            if (argument.Length < 2 || argument[0] is not ('"' or '\'') || argument[^1] != argument[0])
                return false;
            foreach (var character in argument[1..^1])
                if (character is '"' or '\'' or '{' or '}') return false;
            return true;
        }

        if (expression.Length == 0 || !IsAsciiIdentifierStart(expression[0])) return false;
        foreach (var character in expression[1..])
            if (!IsAsciiIdentifierStart(character) && !(character is >= '0' and <= '9')) return false;
        return true;
    }

    private static bool IsAsciiIdentifierStart(char character) =>
        character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or '_';
}
