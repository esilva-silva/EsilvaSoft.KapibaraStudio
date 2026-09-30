using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

/// <summary>Constants and helpers shared by the identifier-mode domain tests and their workspace-integration counterpart.</summary>
internal static class IdentifierModeTestFixture
{
    public const string Hex = "66e3bd5b3bc3f54c840d73ac";
    // Independent fixture: the 12 ObjectId bytes followed by 4 zero bytes, grouped 8-4-4-4-12 by hand.
    public const string Equivalent = "66e3bd5b-3bc3-f54c-840d-73ac00000000";
    public const string Sample = "00112233-4455-6677-8899-aabbccddeeff";
    public static readonly IdentifierRepresentationMode[] ModeNames = [IdentifierRepresentationMode.Standard, IdentifierRepresentationMode.ObjectId, IdentifierRepresentationMode.UuidV4];

    public static string Binary(string hex, string subType) =>
        "{\"$binary\":{\"base64\":\"" + Convert.ToBase64String(Convert.FromHexString(hex)) + "\",\"subType\":\"" + subType + "\"}}";

    public static readonly string Mixed = "{\"_id\":{\"$oid\":\"" + Hex + "\"},\"uuid\":" + Binary("00112233445566778899aabbccddeeff", "04")
        + ",\"legacy\":" + Binary("33221100554477668899aabbccddeeff", "03") + ",\"texto\":\"" + Hex + "\",\"textoUuid\":\"" + Sample + "\"}";

    public static IdentifierDisplayOptions Options(IdentifierRepresentationMode mode, UuidRepresentation uuid = UuidRepresentation.Standard) => new(mode, uuid);
}
