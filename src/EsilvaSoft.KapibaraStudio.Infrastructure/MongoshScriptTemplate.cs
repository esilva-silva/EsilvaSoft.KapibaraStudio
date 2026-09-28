using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Core;
using MongoDB.Bson;

namespace EsilvaSoft.KapibaraStudio.Infrastructure;

/// <summary>Wraps a user script with the ENV/UUID/Date helpers and the input/result contract mongosh executes.</summary>
public static class MongoshScriptTemplate
{
    public const string ResultPrefix = "__SLOPDATAADMIN_RESULT__";

    internal const string DateHelpers = """
        const Date = new Proxy(globalThis.Date, {
          apply: (target, receiver, args) => args.length === 0 ? target() : new target(...__slopDateArguments(args)),
          construct: (target, args) => new target(...__slopDateArguments(args))
        });
        function __slopDateArguments(args) {
          if (args.length !== 1 || typeof args[0] !== "string" || !/^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}$/.test(args[0])) return args;
          const iso = args[0].replace(" ", "T") + "Z";
          const date = new globalThis.Date(iso);
          if (!Number.isFinite(date.getTime()) || date.toISOString() !== iso) throw new Error("Data inválida: " + args[0]);
          return [date.getTime()];
        }
        """;

    /// <summary>
    /// mongosh cannot call the IDE codec, so CGUUID/JUUID/GUUID are defined over its native UUID and BinData.
    /// Unit tests execute this text and compare every byte with <see cref="UuidCodec"/>.
    /// </summary>
    internal const string UuidHelpers = """
        const __slopUuidHex = (name, value) => {
          if (typeof value !== "string" || !/^(?:[0-9a-fA-F]{32}|[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})$/.test(value))
            throw new Error(name + "(...) exige um UUID com 32 dígitos hexadecimais, com ou sem hífens.");
          return value.replace(/-/g, "").toLowerCase();
        };
        const __slopLegacyUuid = (name, value, groups) => {
          const bytes = __slopUuidHex(name, value).match(/../g).map((pair) => parseInt(pair, 16));
          let offset = 0;
          for (const size of groups) { bytes.splice(offset, size, ...bytes.slice(offset, offset + size).reverse()); offset += size; }
          const alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
          let base64 = "";
          for (let i = 0; i < 16; i += 3) {
            const chunk = (bytes[i] << 16) | ((bytes[i + 1] ?? 0) << 8) | (bytes[i + 2] ?? 0);
            base64 += alphabet[(chunk >> 18) & 63] + alphabet[(chunk >> 12) & 63] + (i + 1 < 16 ? alphabet[(chunk >> 6) & 63] : "=") + (i + 2 < 16 ? alphabet[chunk & 63] : "=");
          }
          return BinData(3, base64);
        };
        function CGUUID(value) { return __slopLegacyUuid("CGUUID", value, [4, 2, 2]); }
        function JUUID(value) { return __slopLegacyUuid("JUUID", value, [8, 8]); }
        function GUUID(value) { return UUID(__slopUuidHex("GUUID", value)); }
        """;

    public static string BuildScript(string inputJson, string userScript, string? database = null)
    {
        var literal = JsonSerializer.Serialize(inputJson);
        var databaseSelection = string.IsNullOrWhiteSpace(database) ? "" : $"db = db.getSiblingDB({JsonSerializer.Serialize(database)});";
        return $$"""
            const __slopUri = process.env.SLOP_CONNECTION_URI;
            delete process.env.SLOP_CONNECTION_URI;
            const __slopEnvironment = JSON.parse(process.env.SLOP_ENVIRONMENT_VALUES || "{}");
            delete process.env.SLOP_ENVIRONMENT_VALUES;
            const ENV = Object.freeze({ get: (key) => {
              if (!Object.prototype.hasOwnProperty.call(__slopEnvironment, key)) throw new Error("Chave de ambiente não definida: " + key);
              return __slopEnvironment[key];
            } });
            {{UuidHelpers}}
            {{DateHelpers}}
            if (__slopUri) db = connect(__slopUri);
            {{databaseSelection}}
            const __slopInputJson = {{literal}};
            const slop = Object.freeze({
              input: Object.freeze({ query: EJSON.parse(__slopInputJson), parameters: EJSON.parse(__slopInputJson) }),
              results: Object.freeze({
                emit: (document) => print("{{ResultPrefix}}" + EJSON.stringify(document, null, 0)),
                stream: async (cursor, maximum = 1000) => {
                  let emitted = 0;
                  while (await cursor.hasNext() && emitted < maximum) {
                    print("{{ResultPrefix}}" + EJSON.stringify(await cursor.next(), null, 0));
                    emitted += 1;
                  }
                  return emitted;
                }
              })
            });

            // --- início do script do usuário ---
            {{userScript}}
            // --- fim do script do usuário ---
            """;
    }

    internal static string ValidateInput(string? inputJson)
    {
        var input = string.IsNullOrWhiteSpace(inputJson) ? "{}" : inputJson;

        try
        {
            input = IdentifierRepresentationService.RewriteConstructors(input);
            _ = BsonDocument.Parse(input);
            return input;
        }
        catch (FormatException exception)
        {
            throw new ArgumentException($"A entrada do script não contém Extended JSON válido: {exception.Message}", nameof(inputJson), exception);
        }
    }
}
