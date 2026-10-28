using System.Linq;
using Dalamud.Game.ClientState.Objects.Types;
using Moq;
using Daedalus.Data;
using Daedalus.Rotation.AstraeaCore.Modules;
using Daedalus.Rotation.Common.Scheduling;
using Daedalus.Services.Action;
using Daedalus.Tests.Mocks;
using Daedalus.Tests.Rotation.Common.Scheduling;
using Xunit;

namespace Daedalus.Tests.Rotation.AstraeaCore.Modules;

/// <summary>
/// The Astrologian's "wait for Lightspeed" before a hardcast Ascend. Lightspeed is an oGCD and oGCD dispatch is
/// combat-gated, so out of combat the wait (true even while Lightspeed sat ready) left the corpse unraised forever.
/// </summary>
public class ResurrectionModuleTests
{
    private static Mock<IActionService> Actions(float swiftcastCooldown, float lightspeedCooldown, bool lightspeedLearned = true)
    {
        var actions = MockBuilders.CreateMockActionService();
        actions.Setup(a => a.GetCooldownRemaining(RoleActions.Swiftcast.ActionId)).Returns(swiftcastCooldown);
        actions.Setup(a => a.GetCooldownRemaining(ASTActions.Lightspeed.ActionId)).Returns(lightspeedCooldown);
        actions.Setup(a => a.IsActionReady(RoleActions.Swiftcast.ActionId)).Returns(swiftcastCooldown <= 0f);
        actions.Setup(a => a.IsActionLearned(ASTActions.Lightspeed.ActionId)).Returns(lightspeedLearned);
        return actions;
    }

    private static bool AscendPushed(Mock<IActionService> actions, bool inCombat)
    {
        var dead = MockBuilders.CreateMockBattleChara(entityId: 9, currentHp: 0, isDead: true).Object;
        var context = AstraeaTestContext.Create(
            partyHelper: new TestableAstraeaPartyHelper(new IBattleChara[] { dead }),
            actionService: actions, level: 100, inCombat: inCombat);
        var scheduler = SchedulerFactory.CreateForTest(actionService: actions, config: context.Configuration);
        new ResurrectionModule().CollectCandidates(context, scheduler, isMoving: false);
        return scheduler.InspectGcdQueue().Any(c => c.Behavior.Action.ActionId == RoleActions.Ascend.ActionId && c.TargetId == 9ul);
    }

    [Fact]
    public void OutOfCombat_LightspeedReady_HardcastsAscend()
        => Assert.True(AscendPushed(Actions(swiftcastCooldown: 0f, lightspeedCooldown: 0f), inCombat: false));

    [Fact]
    public void InCombat_LightspeedNotLearned_HardcastsAscend()
        => Assert.True(AscendPushed(Actions(swiftcastCooldown: 50f, lightspeedCooldown: 0f, lightspeedLearned: false), inCombat: true));

    [Fact]
    public void InCombat_LightspeedReady_WaitsForIt()
        => Assert.False(AscendPushed(Actions(swiftcastCooldown: 50f, lightspeedCooldown: 0f), inCombat: true));

    [Fact]
    public void InCombat_LightspeedLongCooldown_HardcastsAscend()
        => Assert.True(AscendPushed(Actions(swiftcastCooldown: 50f, lightspeedCooldown: 60f), inCombat: true));
}
