using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.KapiLab.Core;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Tests;

[TestFixture]
public sealed class AgentPlanSnapshotTests
{
    [Test]
    public void EvaluateUsesProductPolicyAndMarksSyntheticEvidenceWithoutInvokingTools()
    {
        var permissions = AgentProviderPermissions.Default("local") with
        {
            EnabledReadTools = ["list_connections", "get_query_results", "get_cached_schema"],
            DataSending = new AgentDataSendingPermissions { MongoDocuments = true, InferredSchema = false },
        };
        var input = Input(new AgentPlanSnapshot.PlanCaseInput("agent", AgentOperationMode.Agent,
            permissions, HasWorkspaceFolder: true, ProductToolsAvailable: true, NativeToolsAvailable: true,
            RequireExternalDestinationConsent: false));

        var result = AgentPlanSnapshot.Evaluate(input);
        var item = result.Cases.Single();

        Assert.Multiple(() =>
        {
            Assert.That(result.Schema, Is.EqualTo(AgentPlanSnapshot.OutputSchema));
            Assert.That(result.EvidenceKind, Is.EqualTo("synthetic-policy-input"));
            Assert.That(result.Synthetic, Is.True);
            Assert.That(result.ToolInvocationPerformed, Is.False);
            Assert.That(item.ProductTools, Does.Contain("list_connections"));
            Assert.That(item.ProductTools, Does.Contain("get_query_results"));
            Assert.That(item.ProductTools, Does.Not.Contain("get_cached_schema"));
            Assert.That(item.ProductTools, Does.Not.Contain("mongo_find"));
            Assert.That(item.ProposalHandling, Is.EqualTo("ReviewRequired"));
        });
    }

    [Test]
    public void EvaluatePreservesConsentBlockAndDoesNotTreatSyntheticConsentAsToolAuthorization()
    {
        var input = Input(new AgentPlanSnapshot.PlanCaseInput("no-consent", AgentOperationMode.Agent,
            AgentProviderPermissions.Default("copilot"), HasWorkspaceFolder: true,
            ProductToolsAvailable: true, NativeToolsAvailable: true));

        var item = AgentPlanSnapshot.Evaluate(input).Cases.Single();

        Assert.Multiple(() =>
        {
            Assert.That(item.BlockReason, Is.EqualTo("ConsentMissing"));
            Assert.That(item.ProductTools, Is.Empty);
            Assert.That(item.NativeTools, Is.Empty);
        });
    }

    [Test]
    public void InputRequiresVersionValidPermissionsAndUniqueSafeIds()
    {
        var valid = Input(new AgentPlanSnapshot.PlanCaseInput("case-1", AgentOperationMode.Planning,
            AgentProviderPermissions.Default("local"), false, false, false));
        Assert.That(AgentPlanSnapshot.ParseInput(AgentPlanSnapshot.Serialize(valid)), Is.Not.Null);
        Assert.That(AgentPlanSnapshot.ParseInput(AgentPlanSnapshot.Serialize(valid with { Schema = "unknown" })), Is.Null);
        Assert.That(AgentPlanSnapshot.ParseInput(AgentPlanSnapshot.Serialize(valid with
        {
            Cases = [valid.Cases[0], valid.Cases[0] with { Mode = AgentOperationMode.Automatic }],
        })), Is.Null);
        Assert.That(AgentPlanSnapshot.ParseInput(AgentPlanSnapshot.Serialize(valid with
        {
            Cases = [valid.Cases[0] with { Permissions = AgentProviderPermissions.Default("local") with { ProviderId = " " } }],
        })), Is.Null);
    }

    [Test]
    public void InputRejectsCaseVariantDuplicateJsonProperties()
    {
        const string json = "{\"schema\":\"kapilab-agent-plan-cases-v1\",\"Schema\":\"kapilab-agent-plan-cases-v1\",\"cases\":[]}";

        Assert.That(() => AgentPlanSnapshot.ParseInput(json), Throws.TypeOf<InvalidDataException>());
    }

    private static AgentPlanSnapshot.PlanInput Input(params AgentPlanSnapshot.PlanCaseInput[] cases) =>
        new(AgentPlanSnapshot.InputSchema, cases);
}
