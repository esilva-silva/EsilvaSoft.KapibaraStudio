using System.Reflection;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Infrastructure;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

/// <summary>
/// P7-L06-WIRING, AC-04: the native chat consumes the runtime through Application/Core ports only. Provider SDK
/// types (OpenAI, Anthropic, ModelContextProtocol) and <c>Infrastructure.Agents</c> itself must be nameable only by
/// the Desktop composition root (<see cref="EsilvaSoft.KapibaraStudio.Desktop.App"/>) — never by a ViewModel, a View
/// (their code-behind lives in the very same root namespace as <c>App</c>, so this cannot be a namespace check) nor
/// by Application/Core, which stay provider-neutral by construction.
/// </summary>
[TestFixture, Category("Unit")]
public sealed class AgentArchitectureTests
{
    /// <summary>Assembly-name prefixes that only the composition root may name.</summary>
    private static readonly string[] ProviderSdkAssemblyNames =
        ["EsilvaSoft.KapibaraStudio.Infrastructure.Agents", "OpenAI", "Anthropic", "ModelContextProtocol", "GitHub.Copilot"];

    private const BindingFlags AllMembers =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    [Test]
    public void OnlyTheCompositionRootTypeNamesProviderSdkOrInfrastructureAgentsTypes()
    {
        // Every declared member (field/property/parameter/return type/constructor parameter) of every Desktop type
        // other than App itself (and whatever the compiler nests inside it, e.g. lambda closures) must not name a
        // type from Infrastructure.Agents, OpenAI, Anthropic or ModelContextProtocol.
        var desktopAssembly = typeof(EsilvaSoft.KapibaraStudio.Desktop.App).Assembly;
        var offenders = desktopAssembly.GetTypes()
            .Where(type => type != typeof(EsilvaSoft.KapibaraStudio.Desktop.App) &&
                type.DeclaringType != typeof(EsilvaSoft.KapibaraStudio.Desktop.App))
            .SelectMany(MemberTypes)
            .SelectMany(pair => Unwrap(pair.Used).Select(used => (pair.Owner, used)))
            .Where(pair => IsProviderSdkAssembly(pair.used.Assembly.GetName().Name))
            .Select(pair => pair.Owner.FullName + " -> " + pair.used.FullName)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.Multiple(() =>
        {
            // Guard of the guard: if this ever came back empty because Desktop stopped compiling in any type, the
            // scan below would trivially pass with nothing checked.
            Assert.That(desktopAssembly.GetTypes(), Has.Length.GreaterThan(20));
            Assert.That(offenders, Is.Empty,
                "Tipo fora do composition root nomeando SDK de provider ou Infrastructure.Agents: " + string.Join(", ", offenders));
        });
    }

    [Test]
    public void ApplicationAndCoreNeverReferenceProviderSdksOrInfrastructureAgents()
    {
        Assembly[] portsAndDomain = [typeof(IAgentRuntime).Assembly, typeof(AgentProviderDescriptor).Assembly];
        var offenders = portsAndDomain.SelectMany(assembly => assembly.GetReferencedAssemblies())
            .Select(reference => reference.Name!)
            .Where(IsProviderSdkAssembly)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.That(offenders, Is.Empty, "Application/Core referenciando SDK de provider: " + string.Join(", ", offenders));
    }

    [Test]
    public void InfrastructureBaseNeverReferencesProviderSdksOrInfrastructureAgents()
    {
        // Infrastructure (base) hosts the vault/registry/MongoDB adapters; the provider SDKs stay confined to the
        // new Infrastructure.Agents project (02-arquitetura.md), reached only from the Desktop composition root.
        var references = typeof(AgentCredentialProvider).Assembly.GetReferencedAssemblies().Select(reference => reference.Name!);
        Assert.That(references.Where(IsProviderSdkAssembly), Is.Empty,
            "Infrastructure (base) referenciando SDK de provider ou Infrastructure.Agents.");
    }

    private static bool IsProviderSdkAssembly(string? name) =>
        name is not null && ProviderSdkAssemblyNames.Any(forbidden =>
            name.Equals(forbidden, StringComparison.Ordinal) || name.StartsWith(forbidden + ".", StringComparison.Ordinal));

    /// <summary>Every declared field/property/method-parameter/return-type/constructor-parameter of a type.</summary>
    private static IEnumerable<(Type Owner, Type Used)> MemberTypes(Type type) =>
        type.GetFields(AllMembers).Select(field => (type, field.FieldType))
            .Concat(type.GetProperties(AllMembers).Select(property => (type, property.PropertyType)))
            .Concat(type.GetMethods(AllMembers).SelectMany(method => method.GetParameters()
                .Select(parameter => parameter.ParameterType).Append(method.ReturnType)).Select(used => (type, used)))
            .Concat(type.GetConstructors(AllMembers).SelectMany(constructor => constructor.GetParameters())
                .Select(parameter => (type, parameter.ParameterType)));

    /// <summary>Unwraps ref/array/pointer/generic arguments to reach the types actually named.</summary>
    private static IEnumerable<Type> Unwrap(Type type)
    {
        var core = type.IsByRef || type.IsArray || type.IsPointer ? type.GetElementType() ?? type : type;
        yield return core;
        if (!core.IsGenericType) yield break;
        foreach (var argument in core.GetGenericArguments())
            foreach (var nested in Unwrap(argument))
                yield return nested;
    }

}
