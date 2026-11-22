using Daedalus.Rotation.Transform;
using Xunit;

namespace Daedalus.Tests.Rotation.Transform;

/// <summary>
/// Transformed by a deep-dungeon pomander: the job's actions are refused and the bar holds one action (Pilgrim's
/// Traverse, 2026-11-21: two bombs stood in the pack with Big Burst unpressed). Each rejection test changes one fact
/// of a frame in which the action should be pressed.
/// </summary>
public sealed class TransformPolicyTests
{
    private static TransformAction BigBurst() => TransformPolicy.ActionFor(TransformForm.Bomb)!.Value;

    private static TransformFacts InThePack() => new(
        InCombat: true, EnemyAlive: true, DistanceToEnemy: 4f, EnemyHitbox: 2f, IsMoving: false, Ready: true);

    [Theory]
    [InlineData(565u, true)]   // Palace of the Dead, Eureka Orthos
    [InlineData(4708u, true)]  // Pilgrim's Traverse
    [InlineData(4586u, false)] // "Away with the Fae": no action to press
    [InlineData(0u, false)]
    public void OnlyTransfigurationIsATransformation(uint statusId, bool expected)
        => Assert.Equal(expected, TransformPolicy.IsTransformStatus(statusId));

    [Theory]
    [InlineData(42, TransformForm.Manticore)]
    [InlineData(43, TransformForm.Succubus)]
    [InlineData(49, TransformForm.Kuribu)]
    [InlineData(54, TransformForm.Mudball)]
    [InlineData(55, TransformForm.Bomb)]
    [InlineData(244, TransformForm.Dreadnaught)]
    [InlineData(0x0137, TransformForm.Bomb)] // only the low byte names the form
    [InlineData(7, TransformForm.Unknown)]
    public void TheStatusParamNamesTheForm(int param, TransformForm expected)
        => Assert.Equal(expected, TransformPolicy.FormOf((ushort)param));

    [Theory]
    [InlineData(TransformForm.Manticore, 6273u, TransformAim.Enemy)]
    [InlineData(TransformForm.Succubus, 6274u, TransformAim.Enemy)]
    [InlineData(TransformForm.Kuribu, 6871u, TransformAim.Ground)]
    [InlineData(TransformForm.Dreadnaught, 32781u, TransformAim.Enemy)]
    [InlineData(TransformForm.Mudball, 44628u, TransformAim.Forward)]
    [InlineData(TransformForm.Bomb, 44629u, TransformAim.Self)]
    public void EachFormHasItsOneAction(TransformForm form, uint actionId, TransformAim aim)
    {
        var action = TransformPolicy.ActionFor(form);
        Assert.NotNull(action);
        Assert.Equal(actionId, action.Value.ActionId);
        Assert.Equal(aim, action.Value.Aim);
    }

    [Theory]
    [InlineData(TransformForm.None)]
    [InlineData(TransformForm.Unknown)]
    public void NoFormOrAnUnknownOne_HasNothingToPress(TransformForm form)
        => Assert.Null(TransformPolicy.ActionFor(form));

    [Fact]
    public void ABombInThePack_Bursts()
        => Assert.Null(TransformPolicy.Reject(BigBurst(), InThePack()));

    [Fact]
    public void OutOfCombat_Waits()
        => Assert.Equal("not in combat", TransformPolicy.Reject(BigBurst(), InThePack() with { InCombat = false }));

    [Fact]
    public void NoLivingEnemy_Waits()
        => Assert.Equal("no enemy", TransformPolicy.Reject(BigBurst(), InThePack() with { EnemyAlive = false }));

    [Fact]
    public void Moving_HoldsTheCast()
        => Assert.Equal("needs a hard cast (moving)", TransformPolicy.Reject(BigBurst(), InThePack() with { IsMoving = true }));

    [Fact]
    public void OnRecast_Waits()
        => Assert.Equal("on recast", TransformPolicy.Reject(BigBurst(), InThePack() with { Ready = false }));

    [Theory]
    [InlineData(17f, 2f, true)]   // 15y blast reaches the edge of a 2y hitbox
    [InlineData(17.5f, 2f, false)]
    [InlineData(15.5f, 0f, false)]
    public void TheBlastReachesTheEnemysEdge(float distance, float hitbox, bool pressed)
    {
        var why = TransformPolicy.Reject(BigBurst(), InThePack() with { DistanceToEnemy = distance, EnemyHitbox = hitbox });
        Assert.Equal(pressed ? null : "enemy out of reach", why);
    }

    [Theory]
    [InlineData(TransformForm.Mudball)] // the dash
    [InlineData(TransformForm.Kuribu)]  // the placed circle
    public void ADashOrAPlacedCircle_IsMeasuredToTheEnemysCentre(TransformForm form)
    {
        var action = TransformPolicy.ActionFor(form)!.Value;
        var standing = InThePack() with { EnemyHitbox = 5f };
        Assert.Null(TransformPolicy.Reject(action, standing with { DistanceToEnemy = 25f }));
        Assert.Equal("enemy out of reach", TransformPolicy.Reject(action, standing with { DistanceToEnemy = 27f }));
    }

    [Fact]
    public void AnEnemyAimedActionsRange_IsLeftToTheScheduler()
    {
        var pummel = TransformPolicy.ActionFor(TransformForm.Manticore)!.Value;
        Assert.Null(TransformPolicy.Reject(pummel, InThePack() with { DistanceToEnemy = 40f }));
    }

    [Fact]
    public void AnInstantAction_IsPressedOnTheMove()
    {
        var pummel = TransformPolicy.ActionFor(TransformForm.Manticore)!.Value;
        Assert.Null(TransformPolicy.Reject(pummel, InThePack() with { IsMoving = true }));
    }
}
