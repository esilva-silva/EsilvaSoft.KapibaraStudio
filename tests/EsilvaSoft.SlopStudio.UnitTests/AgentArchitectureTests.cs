using System.Reflection;
using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Core.Agents;
using EsilvaSoft.SlopStudio.Infrastructure;

namespace EsilvaSoft.SlopStudio.UnitTests;

/// <summary>
/// P7-L06-WIRING, AC-04: the native chat consumes the runtime through Application/Core ports only. Provider SDK
/// types (OpenAI, Anthropic, ModelContextProtocol) and <c>Infrastructure.Agents</c> itself must be nameable only by
/// the Desktop composition root (<see cref="EsilvaSoft.SlopStudio.Desktop.App"/>) — never by a ViewModel, a View
/// (their code-behind lives in the very same root namespace as <c>App</c>, so this cannot be a namespace check) nor
/// by Application/Core, which stay provider-neutral by construction.
/// </summary>
[TestFixture]
public sealed class AgentArchitectureTests
{
    /// <summary>Assembly-name prefixes that only the composition root may name.</summary>
    private static readonly string[] ProviderSdkAssemblyNames =
        ["EsilvaSoft.SlopStudio.Infrastructure.Agents", "OpenAI", "Anthropic", "ModelContextProtocol"];

    private const BindingFlags AllMembers =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    [Test]
    public void OnlyTheCompositionRootTypeNamesProviderSdkOrInfrastructureAgentsTypes()
    {
        // Every declared member (field/property/parameter/return type/constructor parameter) of every Desktop type
        // other than App itself (and whatever the compiler nests inside it, e.g. lambda closures) must not name a
        // type from Infrastructure.Agents, OpenAI, Anthropic or ModelContextProtocol.
        var desktopAssembly = typeof(EsilvaSoft.SlopStudio.Desktop.App).Assembly;
        var offenders = desktopAssembly.GetTypes()
            .Where(type => type != typeof(EsilvaSoft.SlopStudio.Desktop.App) &&
                type.DeclaringType != typeof(EsilvaSoft.SlopStudio.Desktop.App))
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
    public void DesktopProductionSourceOnlyMentionsProviderSdksInsideAppAxamlCs()
    {
        // Closes the gap reflection cannot see: a method-body-only call (e.g. calling an extension method inside a
        // type whose own signatures never name the SDK). Comment-only lines are skipped so this stays a check of
        // real syntax, not of prose that explains the composition root's job (as AgentChatPorts.cs does).
        string[] forbiddenTokens =
        [
            "Infrastructure.Agents", "OpenAiAgentProvider", "ClaudeAgentProvider",
            "AddSlopStudioOpenAiAgentProvider", "AddSlopStudioClaudeAgentProvider",
            "using OpenAI", "using Anthropic", "using ModelContextProtocol",
        ];
        var offenders = DesktopProductionSourceFiles()
            .Where(file => !Path.GetFileName(file).Equals("App.axaml.cs", StringComparison.Ordinal))
            .SelectMany(file => File.ReadLines(file).Select((line, index) => (file, line, number: index + 1)))
            .Where(entry => !entry.line.TrimStart().StartsWith("//", StringComparison.Ordinal))
            .SelectMany(entry => forbiddenTokens.Where(token => entry.line.Contains(token, StringComparison.Ordinal))
                .Select(token => Path.GetFileName(entry.file) + ":" + entry.number + " (" + token + ")"))
            .ToArray();

        Assert.That(offenders, Is.Empty, "Código fora do composition root mencionando SDK de provider: " + string.Join(", ", offenders));
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

    private static string[] DesktopProductionSourceFiles()
    {
        var root = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "EsilvaSoft.SlopStudio.slnx"))) root = root.Parent;
        Assert.That(root, Is.Not.Null, "Raiz do repositório não encontrada.");
        var directory = Path.Combine(root!.FullName, "src", "EsilvaSoft.SlopStudio.Desktop");
        var separator = Path.DirectorySeparatorChar;
        var files = Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains(separator + "obj" + separator, StringComparison.Ordinal)
                && !file.Contains(separator + "bin" + separator, StringComparison.Ordinal))
            .ToArray();
        Assert.That(files, Is.Not.Empty, "Nenhum fonte de produção do Desktop encontrado; o teste varreria o vazio.");
        return files;
    }
}
