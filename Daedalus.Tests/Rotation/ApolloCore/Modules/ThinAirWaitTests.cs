using System.Linq;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Moq;
using Daedalus.Data;
using Daedalus.Models.Action;
using Daedalus.Rotation.ApolloCore.Modules;
using Daedalus.Rotation.ApolloCore.Modules.Healing;
using Daedalus.Rotation.Common.Scheduling;
using Daedalus.Services.Action;
using Daedalus.Tests.Mocks;
using Daedalus.Tests.Rotation.Common.Scheduling;
using Xunit;

namespace Daedalus.Tests.Rotation.ApolloCore.Modules;

/// <summary>
/// The "wait for Thin Air" gates (heals and raise). Thin Air is a Lv58 job-quest unlock and syncs out, and
/// IsActionReady reads true for an unlearned action, so a raw-level gate made a Lv58+ White Mage without the
/// quest (or synced) hold every Cure II / Raise for a buff that never comes. Out of combat Thin Air (an oGCD)
/// is never cast at all, so the pre-raise wait left the corpse on the floor.
/// </summary>
public class ThinAirWaitTests
{
    private static Mock<IActionService> Actions(bool thinAirLearned = true, float swiftcastCooldown = 0f)
    {
        var actions = MockBuilders.CreateMockActionService();
        actions.Setup(a => a.IsActionLearned(WHMActions.ThinAir.ActionId)).Returns(thinAirLearned);
        actions.Setup(a => a.GetCooldownRemaining(RoleActions.Swiftcast.ActionId)).Returns(swiftcastCooldown);
        return actions;
    }

    // ---- Raise

    private static RotationScheduler RunRaise(Mock<IActionService> actions, bool inCombat, byte level = 100)
    {
        var dead = MockBuilders.CreateMockBattleChara(entityId: 9, currentHp: 0, isDead: true).Object;
        var partyHelper = MockBuilders.CreateMockPartyHelper();
        partyHelper.Setup(p => p.FindDeadPartyMemberNeedingRaise(It.IsAny<IPlayerCharacter>(), It.IsAny<bool>())).Returns(dead);
        var context = ApolloTestContext.Create(partyHelper: partyHelper, actionService: actions, level: level, inCombat: inCombat);
        var scheduler = SchedulerFactory.CreateForTest(actionService: actions, config: context.Configuration);
        new ResurrectionModule().CollectCandidates(context, scheduler, isMoving: false);
        return scheduler;
    }

    private static bool RaisePushed(RotationScheduler s)
        => s.InspectGcdQueue().Any(c => c.Behavior.Action.ActionId == RoleActions.Raise.ActionId && c.TargetId == 9ul);

    [Fact]
    public void Raise_OutOfCombat_ThinAirReady_HardcastsInsteadOfWaiting()
        => Assert.True(RaisePushed(RunRaise(Actions(), inCombat: false)));

    [Fact]
    public void Raise_InCombat_ThinAirNotLearned_HardcastsInsteadOfWaiting()
        => Assert.True(RaisePushed(RunRaise(Actions(thinAirLearned: false, swiftcastCooldown: 50f), inCombat: true)));

    [Fact]
    public void Raise_InCombat_ThinAirLearnedAndReady_StillWaitsForIt()
        => Assert.False(RaisePushed(RunRaise(Actions(swiftcastCooldown: 50f), inCombat: true)));

    [Fact]
    public void Raise_InCombat_BelowLevel58_DoesNotWait()
        => Assert.True(RaisePushed(RunRaise(Actions(swiftcastCooldown: 50f), inCombat: true, level: 57)));

    // ---- Cure II (SingleTargetHealingHandler -> ThinAirHelper)

    private static RotationScheduler RunCureII(Mock<IActionService> actions, byte level = 100)
    {
        var target = MockBuilders.CreateMockBattleChara(entityId: 5, currentHp: 20000, maxHp: 50000).Object;
        var partyHelper = MockBuilders.CreateMockPartyHelper(lowestHpMember: target);
        var selector = ApolloTestContext.CreateDefaultHealingSpellSelector();
        selector.Setup(x => x.SelectBestSingleHeal(
                It.IsAny<IPlayerCharacter>(), It.IsAny<IBattleChara>(), It.IsAny<bool>(), It.IsAny<bool>(),
                It.IsAny<bool>(), It.IsAny<float>(), It.IsAny<bool>()))
            .Returns(((ActionDefinition?)WHMActions.CureII, 8000));
        var config = ApolloTestContext.CreateDefaultWhiteMageConfiguration();
        config.Healing.UseDamageIntakeTriage = false;
        var context = ApolloTestContext.Create(config: config, partyHelper: partyHelper, actionService: actions,
            healingSpellSelector: selector, level: level, inCombat: true);
        var scheduler = SchedulerFactory.CreateForTest(actionService: actions, config: config);
        new SingleTargetHealingHandler().CollectCandidates(context, scheduler, isMoving: false);
        return scheduler;
    }

    private static bool CureIIPushed(RotationScheduler s)
        => s.InspectGcdQueue().Any(c => c.Behavior.Action.ActionId == WHMActions.CureII.ActionId && c.TargetId == 5ul);

    [Fact]
    public void CureII_ThinAirNotLearned_IsCastWithoutWaiting()
        => Assert.True(CureIIPushed(RunCureII(Actions(thinAirLearned: false))));

    [Fact]
    public void CureII_ThinAirLearnedAndReady_WaitsForIt()
        => Assert.False(CureIIPushed(RunCureII(Actions())));

    [Fact]
    public void CureII_BelowLevel58_DoesNotWait()
        => Assert.True(CureIIPushed(RunCureII(Actions(), level: 57)));
}
