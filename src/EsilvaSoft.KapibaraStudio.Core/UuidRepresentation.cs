using System.Text.Json.Serialization;

namespace EsilvaSoft.KapibaraStudio.Core;

/// <summary>How the IDE writes and reads 16-byte UUID binaries. The choice never rewrites stored bytes.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<UuidRepresentation>))]
public enum UuidRepresentation { Standard, CSharpLegacy, JavaLegacy, GoStandard }
