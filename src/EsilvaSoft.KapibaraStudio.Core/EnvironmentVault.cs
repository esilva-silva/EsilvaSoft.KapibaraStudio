namespace EsilvaSoft.KapibaraStudio.Core;

public sealed record EnvironmentVault(int Version, string ActiveEnvironment, EnvironmentDefinition[] Environments)
{
    public static EnvironmentVault CreateDefault() => new(1, "Development",
        [new("Development", []), new("Staging", []), new("Production", [])]);

    public void Validate()
    {
        if (Version != 1 || Environments is null || Environments.Length == 0)
            throw new InvalidDataException("Cofre de ambientes inválido ou versão não suportada.");
        if (Environments.Any(e => e is null || string.IsNullOrWhiteSpace(e.Name) || e.Name.Length > 60 || e.Values is null)
            || Environments.Select(e => e.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Environments.Length
            || !Environments.Any(e => e.Name == ActiveEnvironment))
            throw new InvalidDataException("Informe ambientes com nomes únicos e um ambiente ativo válido.");
        if (Environments.Any(e => e.Values.Any(v => string.IsNullOrWhiteSpace(v.Key) || v.Value is null)))
            throw new InvalidDataException("As chaves devem ter nome e os valores devem ser textos.");
    }

    public EnvironmentSnapshot Capture()
    {
        Validate();
        return new(ActiveEnvironment, Environments.Single(e => e.Name == ActiveEnvironment).Values);
    }
}
