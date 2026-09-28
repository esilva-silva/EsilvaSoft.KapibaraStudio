namespace EsilvaSoft.KapibaraStudio.Core;

public sealed record ConsoleWriteConfirmation(ConnectionProfile Profile, string Database, string Collection, string Method, string? FilterJson = null)
{
    public string Context => $"{Profile.Name} › {Database} › {Collection}\n{Profile.RoutingLabel}\nOperação: {Method}" + (FilterJson is null ? "" : "\nFiltro: " + FilterJson);
}
