using MongoDB.Driver;
using MongoDB.Driver.Core.Configuration;

namespace EsilvaSoft.KapibaraStudio.Infrastructure;

public static class ConnectionRouting
{
    public static Task<string> ApplyAsync(string connectionString, string? targetHost, CancellationToken cancellationToken = default) =>
        ApplyAsync(connectionString, targetHost, static (url, token) => url.ResolveAsync(token), cancellationToken);

    internal static async Task<string> ApplyAsync(string connectionString, string? targetHost,
        Func<MongoUrl, CancellationToken, Task<MongoUrl>> resolve, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(targetHost)) return connectionString;
        var url = new MongoUrl(connectionString);
        if (url.Scheme == ConnectionStringScheme.MongoDBPlusSrv)
            connectionString = (await resolve(url, cancellationToken).ConfigureAwait(false)).ToString();
        return Apply(connectionString, targetHost);
    }

    public static string Apply(string connectionString, string? targetHost)
    {
        if (string.IsNullOrWhiteSpace(targetHost)) return connectionString;
        var builder = new MongoUrlBuilder(connectionString);
        if (builder.Scheme == ConnectionStringScheme.MongoDBPlusSrv)
            throw new InvalidOperationException("Resolva a descoberta SRV antes de aplicar o destino explícito.");
        if (builder.LoadBalanced == true)
            throw new InvalidOperationException("Conexões loadBalanced não permitem seleção direta de um membro.");
        builder.Server = MongoServerAddress.Parse(targetHost);
        builder.DirectConnection = true;
        builder.ReadPreference = ReadPreference.Nearest;
        return builder.ToString();
    }
}
