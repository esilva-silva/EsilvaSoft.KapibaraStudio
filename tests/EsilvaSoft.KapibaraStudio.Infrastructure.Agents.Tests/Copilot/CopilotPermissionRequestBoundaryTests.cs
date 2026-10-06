using EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Copilot;
using GitHub.Copilot;
using GitHub.Copilot.Rpc;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Tests.Copilot;

#pragma warning disable GHCP001 // Pinned SDK 1.0.14 exposes permission decision variants only through its experimental RPC contract.
/// <summary>Every public request variant of SDK 1.0.14 is explicit; no native permission can inherit product approval.</summary>
[TestFixture, Category("Unit")]
public sealed class CopilotPermissionRequestBoundaryTests
{
    private const string ProductTool = "get_workspace_context";
    private static readonly Type[] KnownRequestTypes =
    [
        typeof(PermissionRequestShell), typeof(PermissionRequestWrite), typeof(PermissionRequestRead),
        typeof(PermissionRequestMcp), typeof(PermissionRequestUrl), typeof(PermissionRequestMemory),
        typeof(PermissionRequestCustomTool), typeof(PermissionRequestHook), typeof(PermissionRequestExtensionManagement),
        typeof(PermissionRequestFactory), typeof(PermissionRequestExtensionPermissionAccess),
        typeof(PermissionRequestExtensionEnvAccess),
    ];

    [Test]
    public void ExplicitMatrixIncludesEveryPublicConcreteSdkPermissionRequestType()
    {
        var exported = typeof(PermissionRequest).Assembly.GetExportedTypes().Where(type =>
            type != typeof(PermissionRequest) && !type.IsAbstract && typeof(PermissionRequest).IsAssignableFrom(type));
        Assert.That(exported, Is.EquivalentTo(KnownRequestTypes),
            "An SDK update introducing a request kind requires an explicit safety fixture, never automatic approval.");
        Assert.That(NativeRequests().Select(test => test.Arguments[0]!.GetType()),
            Is.EquivalentTo(KnownRequestTypes.Where(type => type != typeof(PermissionRequestCustomTool))),
            "Every non-custom variant must be exercised by a concrete denial case.");
    }

    [TestCaseSource(nameof(NativeRequests))]
    public async Task NativeRequestKindsAlwaysRejectEvenWhenNamesAndSessionApprovalLookAllowed(PermissionRequest request)
    {
        var allowed = new HashSet<string>([ProductTool, "bash", "read", "write", "mcp", "url", "memory", "hook",
            "extension-management", "factory", "extension-permission-access", "extension-env-access"], StringComparer.Ordinal);

        var decision = await CopilotSubscriptionAgentSession.DecidePermissionAsync(request, allowed);

        Assert.Multiple(() =>
        {
            Assert.That(decision, Is.InstanceOf<PermissionDecisionReject>());
            Assert.That(decision.Kind, Is.EqualTo("reject"));
            Assert.That(((PermissionDecisionReject)decision).Feedback, Does.Not.Contain("native-request-canary"));
        });
    }

    [TestCase("unplanned-tool")]
    [TestCase("list_connections")]
    [TestCase("Get_Workspace_Context")]
    [TestCase("get_workspace_context ")]
    [TestCase("")]
    [TestCase(null)]
    public async Task CustomRequestOutsideExactPlanRejectsEvenWhenRuntimeRequestsSkipPermission(string? toolName)
    {
        var request = new PermissionRequestCustomTool
        {
            ToolName = toolName!, // Deliberately cover a malformed DTO as well as unplanned names.
            ToolDescription = "native-request-canary",
            SkipPermission = true,
        };

        var decision = await CopilotSubscriptionAgentSession.DecidePermissionAsync(request,
            new HashSet<string>([ProductTool], StringComparer.Ordinal));

        Assert.Multiple(() =>
        {
            Assert.That(decision, Is.InstanceOf<PermissionDecisionReject>());
            Assert.That(decision.Kind, Is.EqualTo("reject"));
            Assert.That(((PermissionDecisionReject)decision).Feedback, Does.Not.Contain("native-request-canary"));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task PlannedCustomRequestGetsOnlyApproveOnce(bool skipPermission)
    {
        var request = new PermissionRequestCustomTool
        {
            ToolName = ProductTool, ToolDescription = "Synthetic product request", SkipPermission = skipPermission,
        };
        var decision = await CopilotSubscriptionAgentSession.DecidePermissionAsync(request,
            new HashSet<string>([ProductTool], StringComparer.Ordinal));

        Assert.That(decision, Is.InstanceOf<PermissionDecisionApproveOnce>(),
            "The adapter gate must not turn a planned product call into session, location or permanent approval.");
    }

    private static IEnumerable<TestCaseData> NativeRequests()
    {
        PermissionRequest[] requests =
        [
            new PermissionRequestShell
            {
                CanOfferSessionApproval = true, Commands = [], FullCommandText = "native-request-canary",
                HasWriteFileRedirection = true, Intention = "synthetic native shell", PossiblePaths = [], PossibleUrls = [],
            },
            new PermissionRequestWrite
            {
                CanOfferSessionApproval = true, Diff = "native-request-canary", FileName = "synthetic.js",
                Intention = "synthetic native write",
            },
            new PermissionRequestRead { Intention = "synthetic native read", Path = "native-request-canary.js" },
            new PermissionRequestMcp
            {
                ReadOnly = true, ServerName = "synthetic-server", ToolName = ProductTool, ToolTitle = "native-request-canary",
            },
            new PermissionRequestUrl { Intention = "synthetic network", Url = "https://native-request-canary.invalid" },
            new PermissionRequestMemory { Fact = "native-request-canary" },
            new PermissionRequestHook { ToolName = ProductTool, HookMessage = "native-request-canary" },
            new PermissionRequestExtensionManagement { Operation = "install", ExtensionName = "native-request-canary" },
            new PermissionRequestFactory
            {
                ApprovalKey = "native-request-canary", CanPersistApproval = true, Description = "synthetic factory",
                Name = ProductTool, Operation = FactoryPermissionOperation.Run, Phases = [],
            },
            new PermissionRequestExtensionPermissionAccess
            {
                Capabilities = ["read", "write"], ExtensionName = "native-request-canary",
            },
            new PermissionRequestExtensionEnvAccess
            {
                EnvironmentVariables = ["GITHUB_TOKEN"], ExtensionName = "native-request-canary",
            },
        ];
        foreach (var request in requests)
            yield return new TestCaseData(request).SetName("PermissionBoundaryRejects_" + request.GetType().Name);
    }
}
#pragma warning restore GHCP001
