using System.Collections.ObjectModel;

namespace EsilvaSoft.SlopStudio.Core;

/// <summary>Immutable tool metadata only. It does not contain or imply an executable handler.</summary>
public sealed class AgentToolDescriptor
{
    public AgentToolDescriptor(string name, int version, AgentToolRisk risk, IEnumerable<AgentPermission> requiredPermissions)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 96 || name.Any(static c => !(char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '_')))
            throw new ArgumentException("O nome da tool é inválido.", nameof(name));
        ArgumentOutOfRangeException.ThrowIfLessThan(version, 1);
        if (!Enum.IsDefined(risk)) throw new ArgumentOutOfRangeException(nameof(risk));
        ArgumentNullException.ThrowIfNull(requiredPermissions);
        var permissions = requiredPermissions.ToArray();
        if (permissions.Length == 0 || permissions.Any(static permission => !Enum.IsDefined(permission)) || permissions.Distinct().Count() != permissions.Length ||
            permissions.Any(permission => !AgentPermissionRiskCompatibility.IsCompatible(permission, risk)))
            throw new ArgumentException("A tool precisa declarar permissões canônicas distintas.", nameof(requiredPermissions));
        Name = name;
        Version = version;
        Risk = risk;
        RequiredPermissions = new ReadOnlyCollection<AgentPermission>(permissions);
    }

    public string Name { get; }
    public int Version { get; }
    public AgentToolRisk Risk { get; }
    public IReadOnlyList<AgentPermission> RequiredPermissions { get; }
}
