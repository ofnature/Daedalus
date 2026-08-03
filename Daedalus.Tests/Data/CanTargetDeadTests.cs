using Daedalus.Data;
using Xunit;

namespace Daedalus.Tests.Data;

/// <summary>
/// The scheduler's dead-target gate (added 2026-07-28 against "Invalid target." toasts) rejects
/// dead targets universally — which silently killed every raise in the plugin, because a raise's
/// entire purpose is a dead target. Field 2026-08-02: "Egeiro: Target dead" in the GCD chain
/// while a party member lay dead through the last 20% of a boss.
/// </summary>
public sealed class CanTargetDeadTests
{
    [Fact]
    public void EveryHealerRaise_MayTargetTheDead()
    {
        Assert.True(RoleActions.Raise.CanTargetDead);
        Assert.True(RoleActions.Egeiro.CanTargetDead);
        Assert.True(RoleActions.Ascend.CanTargetDead);
        Assert.True(RoleActions.Resurrection.CanTargetDead);
        Assert.True(RDMActions.Verraise.CanTargetDead);
    }

    /// <summary>The exemption must stay narrow — for everything else the gate is correct.</summary>
    [Fact]
    public void OrdinaryActions_KeepTheDeadTargetGate()
    {
        Assert.False(RoleActions.Esuna.CanTargetDead);
        Assert.False(RoleActions.Swiftcast.CanTargetDead);
    }
}
