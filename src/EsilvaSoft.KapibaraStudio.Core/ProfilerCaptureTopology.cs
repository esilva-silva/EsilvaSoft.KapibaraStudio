using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EsilvaSoft.KapibaraStudio.Core;

public static class ProfilerCaptureTopology
{
    public static string GetNodeIdentity(string helloJson)
    {
        if (ProfilerSettings.IsMongos(helloJson))
            throw new NotSupportedException("A coleta com restauração requer conexão a um mongod.");
        using var document = JsonDocument.Parse(helloJson);
        var root = document.RootElement;
        if (!root.TryGetProperty("topologyVersion", out var version)
            || version.ValueKind != JsonValueKind.Object
            || !version.TryGetProperty("processId", out var processId)
            || processId.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            throw new NotSupportedException("O servidor não expõe identidade de processo estável para recuperação segura.");
        var name = root.TryGetProperty("me", out var me) && me.ValueKind == JsonValueKind.String
            ? me.GetString() : string.Empty;
        var value = name + ":" + processId.GetRawText();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }
}
