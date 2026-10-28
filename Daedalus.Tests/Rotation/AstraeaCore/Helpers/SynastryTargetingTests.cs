using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using Moq;
using Daedalus.Rotation.AstraeaCore.Helpers;
using Daedalus.Services;
using Daedalus.Services.Prediction;
using Daedalus.Tests.Mocks;
using Xunit;

namespace Daedalus.Tests.Rotation.AstraeaCore.Helpers;

/// <summary>
/// Synastry (3612) can't target self (game Action sheet: CanTargetSelf False). The lowest-HP fallback used to
/// return the Astrologian when they were lowest; the game refused it every time and the next-lowest ally never
/// got the link.
/// </summary>
public sealed class SynastryTargetingTests
{
    private static IPlayerCharacter Caster(uint currentHp)
    {
        var m = MockBuilders.CreateMockPlayerCharacter(level: 100, currentHp: currentHp, maxHp: 50000);
        m.Setup(x => x.EntityId).Returns(1u);
        m.Setup(x => x.GameObjectId).Returns(1ul);
        return m.Object;
    }

    [Fact]
    public void PlayerLowest_PicksNextLowestAlly()
    {
        var caster = Caster(currentHp: 20000);                                                            // 40%
        var dps = MockBuilders.CreateMockBattleChara(entityId: 3, currentHp: 22500, maxHp: 50000).Object;  // 45%
        var healthy = MockBuilders.CreateMockBattleChara(entityId: 4, currentHp: 45000, maxHp: 50000).Object;
        var helper = new TestableAstraeaPartyHelper(new IBattleChara[] { caster, dps, healthy });

        Assert.Same(dps, helper.FindSynastryTarget(caster));
    }

    [Fact]
    public void OnlyPlayerHurt_ReturnsNull()
    {
        var caster = Caster(currentHp: 20000);
        var full = MockBuilders.CreateMockBattleChara(entityId: 3, currentHp: 50000, maxHp: 50000).Object;
        var helper = new TestableAstraeaPartyHelper(new IBattleChara[] { caster, full });

        Assert.Null(helper.FindSynastryTarget(caster));
    }

    [Fact]
    public void AllyLowerThanPlayer_StillPicksAlly()
    {
        var caster = Caster(currentHp: 30000);                                                            // 60%
        var dps = MockBuilders.CreateMockBattleChara(entityId: 3, currentHp: 15000, maxHp: 50000).Object;  // 30%
        var helper = new TestableAstraeaPartyHelper(new IBattleChara[] { caster, dps });

        Assert.Same(dps, helper.FindSynastryTarget(caster));
    }

    [Fact]
    public void HurtTank_StillPreferred()
    {
        var caster = Caster(currentHp: 10000);                                                             // 20%
        var tank = MockBuilders.CreateMockBattleChara(entityId: 2, currentHp: 35000, maxHp: 50000).Object;  // 70%
        var dps = MockBuilders.CreateMockBattleChara(entityId: 3, currentHp: 15000, maxHp: 50000).Object;   // 30%
        var helper = new TankAwareHelper(new IBattleChara[] { caster, tank, dps }, tankId: 2);

        Assert.Same(tank, helper.FindSynastryTarget(caster));
    }

    /// <summary>Tank role reads ClassJob / stance status, neither mockable here, so the tank is named by id.</summary>
    private sealed class TankAwareHelper(IEnumerable<IBattleChara> members, uint tankId) : AstraeaPartyHelper(
        MockBuilders.CreateMockObjectTable().Object,
        new Mock<IPartyList>().Object,
        HpPrediction(),
        new Configuration(),
        new AstraeaStatusHelper())
    {
        private readonly List<IBattleChara> _members = members.ToList();

        public override IEnumerable<IBattleChara> GetAllPartyMembers(IPlayerCharacter player, bool includeDead = false) => _members;

        public override IBattleChara? FindTankInParty(IPlayerCharacter player) => _members.FirstOrDefault(m => m.EntityId == tankId);

        private static HpPredictionService HpPrediction()
        {
            var combatEvents = new Mock<ICombatEventService>();
            combatEvents.Setup(x => x.GetShadowHp(It.IsAny<uint>(), It.IsAny<uint>())).Returns((uint _, uint hp) => hp);
            return new HpPredictionService(combatEvents.Object, new Configuration());
        }
    }
}
