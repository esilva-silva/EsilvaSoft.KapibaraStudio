using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture, Category("Unit")]
public sealed class AgentMcpSessionRegistryTests
{
    private static AgentTurnPlan Plan(Guid allowedConnection) => new(
        AgentOperationMode.Agent, [], [], [], ["list_connections"], AgentProposalHandling.Disabled, true,
        AgentConfirmationCategories.None)
    {
        AllowedConnectionIds = [allowedConnection],
    };

    [Test]
    public void ScopeStartsClosedToToolsThenExposesOnlyTheCurrentPlanAndConnectionSet()
    {
        var registry = new AgentMcpSessionRegistry();
        var channelId = Guid.NewGuid();
        var principalId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        var allowedConnection = Guid.NewGuid();
        var deniedConnection = Guid.NewGuid();
        var scope = registry.Register(channelId, principalId, "claude-code", conversationId);

        Assert.Multiple(() =>
        {
            Assert.That(scope.Exposes("list_connections"), Is.False);
            Assert.That(scope.Exposes(AgentToolRegistry.ApproveToolName), Is.False);
            Assert.That(registry.ChannelIds, Does.Contain(channelId));
        });

        var permissions = AgentProviderPermissions.Default("claude-code");
        Assert.That(registry.UpdateTurn(principalId, Plan(allowedConnection), permissions), Is.True);
        scope = registry.FindByPrincipal(principalId)!;
        Assert.Multiple(() =>
        {
            Assert.That(scope.Exposes("list_connections"), Is.True);
            Assert.That(scope.Exposes(AgentToolRegistry.ApproveToolName), Is.True);
            Assert.That(scope.Exposes("mongo_find"), Is.False);
            Assert.That(scope.AllowsConnection(allowedConnection), Is.True);
            Assert.That(scope.AllowsConnection(deniedConnection), Is.False);
            Assert.That(scope.AllowsConnection(Guid.Empty), Is.False);
        });
    }

    [Test]
    public void PermissionChangeRevokesTurnApprovalsAndCloseKeepsFailClosedTombstone()
    {
        var registry = new AgentMcpSessionRegistry();
        var channelId = Guid.NewGuid();
        var principalId = Guid.NewGuid();
        var allowedConnection = Guid.NewGuid();
        var permissions = AgentProviderPermissions.Default("claude-code");
        registry.Register(channelId, principalId, "claude-code", Guid.NewGuid());
        Assert.That(registry.UpdateTurn(principalId, Plan(allowedConnection), permissions), Is.True);

        var first = registry.FindByPrincipal(principalId)!;
        first.SessionApprovals!.TryAdd("approved-once", 0);
        Assert.That(registry.UpdateTurn(principalId, Plan(allowedConnection), permissions), Is.True);
        var samePermissions = registry.FindByPrincipal(principalId)!;
        Assert.That(samePermissions.SessionApprovals, Is.SameAs(first.SessionApprovals));
        Assert.That(samePermissions.Generation, Is.EqualTo(first.Generation + 1));

        var changedPermissions = permissions with { KeepHistory = false };
        Assert.That(registry.UpdateTurn(principalId, Plan(allowedConnection), changedPermissions), Is.True);
        var narrowed = registry.FindByPrincipal(principalId)!;
        Assert.That(narrowed.SessionApprovals, Is.Not.SameAs(first.SessionApprovals));
        Assert.That(narrowed.SessionApprovals, Is.Empty);

        Assert.That(registry.Close(principalId), Is.True);
        var closed = registry.FindByChannel(channelId)!;
        Assert.Multiple(() =>
        {
            Assert.That(closed.Closed, Is.True);
            Assert.That(closed.Plan, Is.Null);
            Assert.That(closed.Permissions, Is.Null);
            Assert.That(closed.Exposes("list_connections"), Is.False);
            Assert.That(closed.AllowsConnection(allowedConnection), Is.False);
            Assert.That(registry.ChannelIds, Does.Contain(channelId), "O tombstone continua listado até a revogação durável.");
            Assert.That(registry.UpdateTurn(principalId, Plan(allowedConnection), permissions), Is.False);
        });

        Assert.That(registry.Remove(principalId), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(registry.FindByPrincipal(principalId), Is.Null);
            Assert.That(registry.FindByChannel(channelId), Is.Null);
            Assert.That(registry.ChannelIds, Does.Not.Contain(channelId));
        });
    }

    [Test]
    public void DuplicateRegistrationDoesNotLeaveAnOrphanChannelIndex()
    {
        var registry = new AgentMcpSessionRegistry();
        var channelId = Guid.NewGuid();
        var otherChannelId = Guid.NewGuid();
        var principalId = Guid.NewGuid();
        registry.Register(channelId, principalId, "claude-code", Guid.NewGuid());

        Assert.Throws<InvalidOperationException>(() => registry.Register(otherChannelId, principalId, "claude-code", Guid.NewGuid()));
        Assert.Multiple(() =>
        {
            Assert.That(registry.ChannelIds, Is.EquivalentTo([channelId]));
            Assert.That(registry.FindByChannel(otherChannelId), Is.Null);
            Assert.That(registry.FindByPrincipal(principalId)?.ChannelId, Is.EqualTo(channelId));
        });
    }
}
