using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Moq;
using Daedalus.Data;
using Daedalus.Services.Action;
using Daedalus.Rotation.Common.Helpers;
using Daedalus.Rotation.Common.Scheduling;
using Daedalus.Tests.Mocks;
using Daedalus.Tests.Rotation.ApolloCore;
using Daedalus.Tests.Rotation.AsclepiusCore;
using Daedalus.Tests.Rotation.AstraeaCore;
using Daedalus.Tests.Rotation.AthenaCore;
using Daedalus.Tests.Rotation.Common.Scheduling;
using Xunit;
using ApolloDoom = Daedalus.Rotation.ApolloCore.Modules.Healing.DoomTopOffHandler;
using AsclepiusDoom = Daedalus.Rotation.AsclepiusCore.Modules.Healing.DoomTopOffHandler;
using AstraeaDoom = Daedalus.Rotation.AstraeaCore.Modules.Healing.DoomTopOffHandler;
using AthenaDoom = Daedalus.Rotation.AthenaCore.Modules.Healing.DoomTopOffHandler;

namespace Daedalus.Tests.Rotation.Common;

/// <summary>
/// A Doomed member is healed to FULL, whatever their HP. Korha, 2026-09-26, died twice to his own Deep Freeze Doom
/// at 83-85% HP while the Sage cast Dosis: target selection put him first, but every heal handler read his real HP
/// against its own threshold and passed. Doom itself can't be put on a mock (StatusList isn't mockable), so the
/// handlers take the "needs a top-off" test as a parameter and these tests name the Doomed member by entity id.
/// </summary>
public class DoomTopOffHandlerTests
{
    private const uint Doomed = 77u;
    private static bool IsDoomed(IBattleChara m) => m.EntityId == Doomed;

    private static List<IBattleChara> Party(uint doomedHp = 42000, float doomedDistance = 5f) =>
    [
        MockBuilders.CreateMockBattleChara(entityId: 11, currentHp: 20000, maxHp: 50000).Object,   // lower HP, not Doomed
        MockBuilders.CreateMockBattleChara(entityId: Doomed, currentHp: doomedHp, maxHp: 50000,
            position: new Vector3(doomedDistance, 0, 0)).Object,
    ];

    private static Mock<IActionService> Ready(params uint[] notReady)
        => MockBuilders.CreateMockActionService(isActionReady: id => !notReady.Contains(id));

    private static AbilityCandidate? Pushed(IReadOnlyList<AbilityCandidate> queue, ulong target)
        => queue.Where(c => c.TargetId == target).Select(c => (AbilityCandidate?)c).FirstOrDefault();

    // ---- the shared lookup

    [Fact]
    public void FindsTheDoomedMember_NotTheLowestHp()
    {
        var player = MockBuilders.CreateMockPlayerCharacter().Object;
        var found = HealerPartyHelper.FindMemberNeedingTopOff(player, Party(), 30f, IsDoomed);
        Assert.Equal(Doomed, found?.EntityId);
    }

    [Theory]
    [InlineData(50000u, 5f)]    // already full: Doom is gone
    [InlineData(42000u, 31f)]   // out of heal range
    public void NobodyToTopOff_WhenFullOrOutOfRange(uint hp, float distance)
    {
        var player = MockBuilders.CreateMockPlayerCharacter().Object;
        Assert.Null(HealerPartyHelper.FindMemberNeedingTopOff(player, Party(hp, distance), 30f, IsDoomed));
    }

    [Fact]
    public void NobodyToTopOff_WhenNobodyIsDoomed()
    {
        var player = MockBuilders.CreateMockPlayerCharacter().Object;
        Assert.Null(HealerPartyHelper.FindMemberNeedingTopOff(player, Party(), 30f, _ => false));
    }

    // ---- White Mage

    private static RotationScheduler RunApollo(Mock<IActionService> actions, bool isMoving = false, List<IBattleChara>? party = null)
    {
        var partyHelper = MockBuilders.CreateMockPartyHelper();
        partyHelper.Setup(p => p.GetAllPartyMembers(It.IsAny<IPlayerCharacter>(), It.IsAny<bool>())).Returns(party ?? Party());
        var context = ApolloTestContext.Create(partyHelper: partyHelper, actionService: actions, level: 100);
        var scheduler = SchedulerFactory.CreateForTest(actionService: actions, config: context.Configuration);
        new ApolloDoom(IsDoomed).CollectCandidates(context, scheduler, isMoving);
        return scheduler;
    }

    [Fact]
    public void WhiteMage_Benediction_TopsTheDoomedMemberToFull()
    {
        var s = RunApollo(Ready());
        Assert.Equal(WHMActions.Benediction.ActionId, Pushed(s.InspectOgcdQueue(), Doomed)?.Behavior.Action.ActionId);
    }

    [Fact]
    public void WhiteMage_Tetragrammaton_WhenBenedictionIsDown()
    {
        var s = RunApollo(Ready(WHMActions.Benediction.ActionId));
        Assert.Equal(WHMActions.Tetragrammaton.ActionId, Pushed(s.InspectOgcdQueue(), Doomed)?.Behavior.Action.ActionId);
    }

    [Fact]
    public void WhiteMage_CureII_WhenNoOgcdIsReady_AndNothingWhileMoving()
    {
        var down = Ready(WHMActions.Benediction.ActionId, WHMActions.Tetragrammaton.ActionId);
        Assert.Equal(WHMActions.CureII.ActionId, Pushed(RunApollo(down).InspectGcdQueue(), Doomed)?.Behavior.Action.ActionId);
        Assert.Null(Pushed(RunApollo(down, isMoving: true).InspectGcdQueue(), Doomed));
    }

    [Fact]
    public void WhiteMage_NothingWhenNobodyIsDoomed()
    {
        var s = RunApollo(Ready(), party: Party(doomedHp: 50000));
        Assert.Empty(s.InspectOgcdQueue());
        Assert.Empty(s.InspectGcdQueue());
    }

    // ---- Sage

    private static RotationScheduler RunAsclepius(Mock<IActionService> actions, int addersgall, bool isMoving = false)
    {
        var partyHelper = MockBuilders.CreateMockPartyHelper();
        partyHelper.Setup(p => p.GetAllPartyMembers(It.IsAny<IPlayerCharacter>(), It.IsAny<bool>())).Returns(Party());
        var context = AsclepiusTestContext.Create(partyHelper: partyHelper, actionService: actions, addersgallStacks: addersgall, level: 100);
        var scheduler = SchedulerFactory.CreateForTest(actionService: actions, config: context.Configuration);
        new AsclepiusDoom(IsDoomed).CollectCandidates(context, scheduler, isMoving);
        return scheduler;
    }

    [Fact]
    public void Sage_Druochole_WithAddersgall()
        => Assert.Equal(SGEActions.Druochole.ActionId, Pushed(RunAsclepius(Ready(), addersgall: 1).InspectOgcdQueue(), Doomed)?.Behavior.Action.ActionId);

    [Fact]
    public void Sage_Diagnosis_WithoutAddersgall_AndNothingWhileMoving()
    {
        Assert.Equal(SGEActions.Diagnosis.ActionId, Pushed(RunAsclepius(Ready(), addersgall: 0).InspectGcdQueue(), Doomed)?.Behavior.Action.ActionId);
        Assert.Null(Pushed(RunAsclepius(Ready(), addersgall: 0, isMoving: true).InspectGcdQueue(), Doomed));
    }

    // ---- Astrologian

    private static RotationScheduler RunAstraea(Mock<IActionService> actions, bool isMoving = false)
    {
        var context = AstraeaTestContext.Create(partyHelper: new TestableAstraeaPartyHelper(Party()), actionService: actions, level: 100);
        var scheduler = SchedulerFactory.CreateForTest(actionService: actions, config: context.Configuration);
        new AstraeaDoom(IsDoomed).CollectCandidates(context, scheduler, isMoving);
        return scheduler;
    }

    [Fact]
    public void Astrologian_EssentialDignity()
        => Assert.Equal(ASTActions.EssentialDignity.ActionId, Pushed(RunAstraea(Ready()).InspectOgcdQueue(), Doomed)?.Behavior.Action.ActionId);

    [Fact]
    public void Astrologian_BeneficII_OrAspectedBeneficOnTheMove_WhenNoOgcdIsReady()
    {
        var down = Ready(ASTActions.EssentialDignity.ActionId, ASTActions.CelestialIntersection.ActionId);
        Assert.Equal(ASTActions.BeneficII.ActionId, Pushed(RunAstraea(down).InspectGcdQueue(), Doomed)?.Behavior.Action.ActionId);
        Assert.Equal(ASTActions.AspectedBenefic.ActionId, Pushed(RunAstraea(down, isMoving: true).InspectGcdQueue(), Doomed)?.Behavior.Action.ActionId);
    }

    // ---- Scholar

    private static RotationScheduler RunAthena(Mock<IActionService> actions, int aetherflow, bool isMoving = false)
    {
        var context = AthenaTestContext.Create(partyHelper: new TestableAthenaPartyHelper(Party()), actionService: actions, aetherflowStacks: aetherflow, level: 100);
        var scheduler = SchedulerFactory.CreateForTest(actionService: actions, config: context.Configuration);
        new AthenaDoom(IsDoomed).CollectCandidates(context, scheduler, isMoving);
        return scheduler;
    }

    [Fact]
    public void Scholar_Lustrate_WithAetherflow()
        => Assert.Equal(SCHActions.Lustrate.ActionId, Pushed(RunAthena(Ready(), aetherflow: 1).InspectOgcdQueue(), Doomed)?.Behavior.Action.ActionId);

    [Fact]
    public void Scholar_Physick_WithoutAetherflow()
        => Assert.Equal(SCHActions.Physick.ActionId, Pushed(RunAthena(Ready(), aetherflow: 0).InspectGcdQueue(), Doomed)?.Behavior.Action.ActionId);

    // ---- wired in

    [Fact]
    public void EveryHealerRunsTheTopOffFirst()
    {
        foreach (var module in new object[]
                 {
                     new Daedalus.Rotation.ApolloCore.Modules.HealingModule(),
                     new Daedalus.Rotation.AsclepiusCore.Modules.HealingModule(),
                     new Daedalus.Rotation.AstraeaCore.Modules.HealingModule(),
                     new Daedalus.Rotation.AthenaCore.Modules.HealingModule(),
                 })
        {
            var handlers = (System.Collections.IEnumerable)module.GetType()
                .GetField("_handlers", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .GetValue(module)!;
            Assert.Equal("DoomTopOffHandler", handlers.Cast<object>().First().GetType().Name);
        }
    }
}
