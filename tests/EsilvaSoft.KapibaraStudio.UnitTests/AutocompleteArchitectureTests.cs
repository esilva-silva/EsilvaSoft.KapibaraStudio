using EsilvaSoft.KapibaraStudio.LocalAi.Core;
using System.Reflection;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.SchemaLearning;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Context;
using EsilvaSoft.KapibaraStudio.Infrastructure;
using EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;
using Microsoft.Extensions.DependencyInjection;
using AiCtx = EsilvaSoft.KapibaraStudio.Application.AiContext;
using AiCtxExperimental = EsilvaSoft.KapibaraStudio.Application.AiContext.Experimental;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class AutocompleteArchitectureTests
{
    [Test]
    public void ApplicationDependsOnlyOnTheBaseLibraryAndCore()
    {
        var references = typeof(LanguageDefinition).Assembly.GetReferencedAssemblies().Select(reference => reference.Name!).ToArray();
        Assert.That(references.Where(name => !name.StartsWith("System", StringComparison.Ordinal) && name is not ("netstandard" or "mscorlib" or "EsilvaSoft.KapibaraStudio.Core")), Is.Empty);
    }

    [Test]
    public void LanguageLayerNeverReferencesTheAiRuntime()
    {
        // O runtime de IA cresceu na Fase L (contadores de token, cache de blocos, oráculo de fronteira) e no lote A31c
        // (contrato de contexto). O núcleo determinístico continua não podendo nomear nada disso.
        Type[] forbidden = [typeof(ILocalAiModelService), typeof(ILocalModelRuntime), typeof(ITokenizer), typeof(IAutocompleteService),
            typeof(ITokenCounter), typeof(TokenizedBlockCache), typeof(ITokenBoundaryOracle),
            typeof(AiCtx.IAiContextContract), typeof(AiCtx.AiPromptRequest), typeof(AiCtx.AiPromptResult)];
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var offenders = typeof(LanguageDefinition).Assembly.GetTypes()
            .Where(type => type.Namespace?.StartsWith(LanguageRoot, StringComparison.Ordinal) == true)
            .SelectMany(type => type.GetFields(all).Select(field => (type, field.FieldType))
                .Concat(type.GetProperties(all).Select(property => (type, property.PropertyType)))
                .Concat(type.GetMethods(all).SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType).Append(method.ReturnType)).Select(used => (type, used)))
                .Concat(type.GetConstructors(all).SelectMany(constructor => constructor.GetParameters()).Select(parameter => (type, parameter.ParameterType))))
            .Where(pair => forbidden.Contains(pair.Item2))
            .Select(pair => pair.type.FullName + " → " + pair.Item2.Name)
            .ToArray();
        Assert.That(offenders, Is.Empty);
    }
    /// <summary>Raiz do núcleo determinístico; os subespaços .Context/.Completion/.Syntax/.Text contam como núcleo.</summary>
    private const string LanguageRoot = "EsilvaSoft.KapibaraStudio.Autocomplete.Core";

    [Test]
    public void LanguageCoreCoversEverySubNamespace()
    {
        // Guarda do próprio guarda: se o núcleo deixasse de ter subespaços, o filtro por prefixo varreria nada e os
        // testes de isolamento passariam vazios para sempre.
        var namespaces = typeof(LanguageDefinition).Assembly.GetTypes()
            .Select(type => type.Namespace).Where(name => name?.StartsWith(LanguageRoot, StringComparison.Ordinal) == true)
            .Distinct().ToArray();
        Assert.That(namespaces, Does.Contain(LanguageRoot + ".Context").And.Contain(LanguageRoot + ".Completion")
            .And.Contain(LanguageRoot + ".Syntax").And.Contain(LanguageRoot + ".Text").And.Contain(LanguageRoot + ".Facts"));
    }

    [Test]
    public void LanguageCoreNeverReferencesInfrastructureOrRuntimeAssemblies()
    {
        // A permissão genérica a System* deixaria passar um runtime gerenciado futuro; esta negativa é explícita por
        // nome de assembly e não depende de o tipo proibido já existir no código de hoje.
        string[] forbidden = ["Microsoft.ML", "MongoDB", "LiteDB", "Avalonia", "Jint", "Acornima",
            "EsilvaSoft.KapibaraStudio.Application", "EsilvaSoft.KapibaraStudio.Infrastructure",
            "EsilvaSoft.KapibaraStudio.Desktop", "EsilvaSoft.KapibaraStudio.LocalAi"];
        var references = typeof(LanguageDefinition).Assembly.GetReferencedAssemblies().Select(reference => reference.Name!);
        Assert.That(references.Where(name => forbidden.Any(bad => name.Contains(bad, StringComparison.Ordinal))), Is.Empty);
    }

    [Test]
    public void LanguageCoreNeverTouchesTheNetwork()
    {
        // Funcionar offline é requisito do autocomplete determinístico, e "não começa com System" não barra System.Net.
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        static bool IsNetwork(Type type) => type.Namespace?.StartsWith("System.Net", StringComparison.Ordinal) == true;
        var offenders = typeof(LanguageDefinition).Assembly.GetTypes()
            .SelectMany(type => type.GetFields(all).Select(field => (type, field.FieldType))
                .Concat(type.GetProperties(all).Select(property => (type, property.PropertyType)))
                .Concat(type.GetMethods(all).SelectMany(method => method.GetParameters()
                    .Select(parameter => parameter.ParameterType).Append(method.ReturnType)).Select(used => (type, used))))
            .Where(pair => IsNetwork(pair.Item2))
            .Select(pair => pair.type.FullName + " -> " + pair.Item2.FullName).ToArray();
        Assert.That(offenders, Is.Empty);
    }

    [Test]
    public void TheTextOnlyShapeWalkerOverloadStaysAvailable()
    {
        // A sobrecarga por texto é contrato público; a sobrecarga nova por tokens foi acrescentada ao lado dela.
        var overload = typeof(ShapeWalker).GetMethod(nameof(ShapeWalker.Walk), BindingFlags.Public | BindingFlags.Static,
            [typeof(string), typeof(int), typeof(CompletionCursorRole), typeof(LanguageDefinition), typeof(CancellationToken)]);
        Assert.That(overload, Is.Not.Null);
    }

    // ------------------------------------------------------------------------------------------------------------
    // Fechamento da meta Fase L + Fase 3: regras que os lotes anteriores deixaram registradas como convenção em
    // relatório. Aqui elas viram teste, para que a próxima adição descuidada falhe na suíte e não numa revisão manual.
    // ------------------------------------------------------------------------------------------------------------

    private const string AiContextRoot = "EsilvaSoft.KapibaraStudio.Application.AiContext";
    private const string SchemaLearningRoot = "EsilvaSoft.KapibaraStudio.Application.SchemaLearning";
    private const BindingFlags AllMembers = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    /// <summary>Todo tipo que um tipo nomeia em campos, propriedades, métodos e construtores declarados por ele.</summary>
    private static IEnumerable<(Type Owner, Type Used)> MemberTypes(Type type)
        => type.GetFields(AllMembers).Select(field => (type, field.FieldType))
            .Concat(type.GetProperties(AllMembers).Select(property => (type, property.PropertyType)))
            .Concat(type.GetMethods(AllMembers).SelectMany(method => method.GetParameters()
                .Select(parameter => parameter.ParameterType).Append(method.ReturnType)).Select(used => (type, used)))
            .Concat(type.GetConstructors(AllMembers).SelectMany(constructor => constructor.GetParameters())
                .Select(parameter => (type, parameter.ParameterType)));

    /// <summary>Desembrulha ref/array/genérico para comparar o tipo realmente nomeado.</summary>
    private static IEnumerable<Type> Unwrap(Type type)
    {
        var core = type.IsByRef || type.IsArray || type.IsPointer ? type.GetElementType() ?? type : type;
        yield return core;
        if (!core.IsGenericType) yield break;
        foreach (var argument in core.GetGenericArguments())
            foreach (var nested in Unwrap(argument))
                yield return nested;
    }

    /// <summary>
    /// Regra 2 da meta. O núcleo de IA local descreve modelos, tokenizadores e orçamento de contexto; ele não pode
    /// aprender semântica de MongoDB. Se um dia precisar, a dependência tem de ser injetada de fora, não nomeada aqui
    /// — caso contrário o par LocalAi.Core ↔ Autocomplete.Core vira um ciclo de conceito ainda que não de assembly.
    /// </summary>
    [Test]
    public void LocalAiCoreNeverReferencesMongoSemantics()
    {
        string[] forbiddenAssemblies = ["EsilvaSoft.KapibaraStudio.Autocomplete.Core", "EsilvaSoft.KapibaraStudio.Application",
            "EsilvaSoft.KapibaraStudio.Infrastructure", "EsilvaSoft.KapibaraStudio.Desktop", "MongoDB", "LiteDB", "Avalonia", "Jint"];
        var references = typeof(ITokenCounter).Assembly.GetReferencedAssemblies().Select(reference => reference.Name!).ToArray();

        // Nenhum membro declarado pode nomear um tipo do núcleo de linguagem, mesmo que alguém acrescente a referência.
        var byMember = typeof(ITokenCounter).Assembly.GetTypes()
            .SelectMany(MemberTypes)
            .SelectMany(pair => Unwrap(pair.Used).Select(used => (pair.Owner, used)))
            .Where(pair => pair.used.Namespace?.StartsWith(LanguageRoot, StringComparison.Ordinal) == true)
            .Select(pair => pair.Owner.FullName + " -> " + pair.used.FullName).Distinct(StringComparer.Ordinal).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(references.Where(name => forbiddenAssemblies.Any(bad => name.Contains(bad, StringComparison.Ordinal))), Is.Empty);
            Assert.That(byMember, Is.Empty, "LocalAi.Core nomeando tipo do núcleo de linguagem: " + string.Join(", ", byMember));
        });
    }

    /// <summary>
    /// Regra 3 da meta (DEC-A31C-CONTEXTCONTRACT). O contrato de contexto é a costura entre a aba capturada e o
    /// prompt em que o modelo foi treinado: ele conhece os dois lados e por isso só pode existir na camada que já
    /// conhece os dois. Se um dia o tipo escorregar para <c>Autocomplete.Core</c>, o núcleo determinístico passa a
    /// depender do formato de um modelo; se escorregar para <c>LocalAi.Core</c>, o runtime passa a depender da
    /// captura de aba.
    /// </summary>
    [Test]
    public void AiContextContractIsComposedOnlyInApplication()
    {
        Type[] contracts = [typeof(AiCtx.IAiContextContract), typeof(AiCtx.EditorContextV1Contract),
            typeof(AiCtx.AiContextContractResolver), typeof(AiCtxExperimental.FewShotContextContract),
            typeof(AiCtxExperimental.JsonContextContract), typeof(AiCtxExperimental.MinimalContextContract),
            typeof(AiCtxExperimental.SectionsContextContract)];
        var application = typeof(AutocompleteContextBuilder).Assembly;
        Assembly[] lowerLayers = [typeof(LanguageDefinition).Assembly, typeof(ITokenCounter).Assembly, typeof(LocalModelCatalog).Assembly];

        var declaredElsewhere = contracts.Where(contract => contract.Assembly != application)
            .Select(contract => contract.FullName + " em " + contract.Assembly.GetName().Name).ToArray();
        var referenced = lowerLayers.SelectMany(assembly => assembly.GetTypes())
            .SelectMany(MemberTypes)
            .SelectMany(pair => Unwrap(pair.Used).Select(used => (pair.Owner, used)))
            .Where(pair => pair.used.Namespace?.StartsWith(AiContextRoot, StringComparison.Ordinal) == true)
            .Select(pair => pair.Owner.Assembly.GetName().Name + "/" + pair.Owner.Name + " -> " + pair.used.Name)
            .Distinct(StringComparer.Ordinal).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(contracts.Select(contract => contract.Namespace), Is.All.StartsWith(AiContextRoot));
            Assert.That(declaredElsewhere, Is.Empty, "Contrato de contexto fora de Application: " + string.Join(", ", declaredElsewhere));
            Assert.That(referenced, Is.Empty, "Camada abaixo de Application nomeando o contrato: " + string.Join(", ", referenced));
            // Os dois núcleos ficam abaixo de Application na cadeia e não podem sequer referenciar o assembly;
            // Infrastructure.LocalAi fica acima e pode — o que ele não pode é nomear um contrato, verificado acima.
            Assert.That(new[] { typeof(LanguageDefinition).Assembly, typeof(ITokenCounter).Assembly }
                .SelectMany(assembly => assembly.GetReferencedAssemblies()).Select(reference => reference.Name),
                Has.None.EqualTo(application.GetName().Name));
        });
    }

    /// <summary>
    /// Regra 4 da meta. Verificação positiva a partir da raiz de composição real (as duas chamadas que
    /// <c>App.axaml.cs</c> faz): o único contrato que esta build serializa é <c>editor-context-v1</c>, e nenhum
    /// descritor de DI carrega um contrato — experimental ou não. Os descritores são inspecionados antes de construir
    /// o provedor: assim o teste não precisa abrir o LiteDB do workspace para responder a uma pergunta que é sobre
    /// registro, não sobre resolução.
    /// </summary>
    [Test]
    public void OnlyEditorContextV1IsRegisteredInProduction()
    {
        var services = new ServiceCollection();
        services.AddKapibaraStudioInfrastructure(SyntheticPaths.Combine("architecture", "workspace.db"));
        services.AddKapibaraStudioLocalAiInfrastructure();

        var registeredContracts = services
            .SelectMany(descriptor => new[] { descriptor.ServiceType, descriptor.ImplementationType, descriptor.ImplementationInstance?.GetType() })
            .Where(type => type is not null).Select(type => type!)
            .Where(type => typeof(AiCtx.IAiContextContract).IsAssignableFrom(type)
                || type.Namespace?.StartsWith(AiContextRoot, StringComparison.Ordinal) == true)
            .Select(type => type.FullName!).Distinct(StringComparer.Ordinal).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(services, Is.Not.Empty, "A raiz de composição precisa estar registrando algo.");
            Assert.That(registeredContracts, Is.Empty, "Contrato de contexto registrado em DI: " + string.Join(", ", registeredContracts));
            Assert.That(AiCtx.AiContextContractResolver.Implemented.Select(contract => contract.GetType()),
                Is.EqualTo(new[] { typeof(AiCtx.EditorContextV1Contract) }));
            Assert.That(AiCtx.AiContextContractResolver.Implemented.Select(contract => contract.ContractId),
                Is.EqualTo(new[] { LocalModelContextContracts.EditorContextV1 }));
            Assert.That(LocalModelContextContracts.Supported, Is.EqualTo(new[] { LocalModelContextContracts.EditorContextV1 }));
        });
    }

    /// <summary>
    /// Regra 8 da meta, materializando PEND-K11-L. <c>LearnedSchemaTrustTests</c> e os testes de L11 já cobrem tipo a
    /// tipo; esta é a rede de segurança para o namespace inteiro, inclusive para um tipo que ainda não existe. A chave
    /// durável de um schema aprendido é perfil + banco + coleção (DEC-L-KEY) e nada em
    /// <c>Application/SchemaLearning</c> pode carregar URI, credencial, host resolvido, digital de conexão ou nome
    /// amigável de perfil — esse material nunca deve chegar ao arquivo local junto com estrutura aprendida.
    /// </summary>
    [Test]
    public void LearnedSchemaKeyCarriesNoConnectionSecret()
    {
        string[] forbiddenFragments = ["ConnectionString", "Uri", "Url", "Credential", "Password", "Secret",
            "Fingerprint", "ConnectionIdentity", "ResolvedHost", "TargetHost", "Endpoint", "ProfileName",
            "DisplayName", "FriendlyName", "UserName", "Login", "AuthMechanism", "Certificate", "Vault"];
        string[] forbiddenTypeNames = ["ConnectionProfile", "ConnectionProfileDraft", "MongoUrl", "MongoClientSettings",
            "NetworkCredential", "Uri", "X509Certificate2"];

        var types = typeof(LearnedSchemaKey).Assembly.GetTypes()
            .Where(type => type.Namespace?.StartsWith(SchemaLearningRoot, StringComparison.Ordinal) == true).ToArray();

        static string Normalize(string name) => name.Replace("<", "", StringComparison.Ordinal)
            .Replace(">k__BackingField", "", StringComparison.Ordinal).Replace("_", "", StringComparison.Ordinal);

        var byName = types.SelectMany(type => type.GetFields(AllMembers).Select(field => (type, member: field.Name))
                .Concat(type.GetProperties(AllMembers).Select(property => (type, member: property.Name))))
            .SelectMany(pair => forbiddenFragments
                .Where(fragment => Normalize(pair.member).Contains(fragment, StringComparison.OrdinalIgnoreCase))
                .Select(fragment => pair.type.Name + "." + pair.member + " (" + fragment + ")"))
            .Distinct(StringComparer.Ordinal).ToArray();

        var byType = types.SelectMany(type => type.GetFields(AllMembers).Select(field => (type, used: field.FieldType))
                .Concat(type.GetProperties(AllMembers).Select(property => (type, used: property.PropertyType))))
            .SelectMany(pair => Unwrap(pair.used).Select(used => (pair.type, used)))
            .Where(pair => forbiddenTypeNames.Contains(pair.used.Name, StringComparer.Ordinal))
            .Select(pair => pair.type.Name + " -> " + pair.used.FullName).Distinct(StringComparer.Ordinal).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(types, Has.Length.GreaterThan(15), "O namespace de aprendizado precisa estar sendo varrido.");
            Assert.That(byName, Is.Empty, "Membro capaz de carregar segredo de conexão: " + string.Join(", ", byName));
            Assert.That(byType, Is.Empty, "Tipo capaz de carregar segredo de conexão: " + string.Join(", ", byType));
            // A chave durável continua sendo exatamente perfil + banco + coleção.
            string[] expectedKeyMembers = ["ProfileId", "Database", "Collection", "IsComplete"];
            Assert.That(typeof(LearnedSchemaKey).GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Select(property => property.Name), Is.EquivalentTo(expectedKeyMembers));
        });
    }

}
