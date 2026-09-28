using System.Text.Json.Serialization;

namespace EsilvaSoft.KapibaraStudio.Core;

/// <summary>
/// Default identifier of the IDE. Standard accepts and preserves both ObjectId and UUID; it is not a synonym of UUID v4.
/// The UUID byte order stays in the separate <see cref="UuidRepresentation"/>.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<IdentifierRepresentationMode>))]
public enum IdentifierRepresentationMode { Standard, ObjectId, UuidV4 }
