using System.Collections.Generic;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Daedalus.Config;
using Daedalus.Data;
using Daedalus.Rotation.AstraeaCore.Helpers;
using Daedalus.Tests.Mocks;
using Xunit;

namespace Daedalus.Tests.Rotation.AstraeaCore.Helpers;

/// <summary>
/// Confirms AST support cards actually resolve to a party ally — Trust NPC *or* real player — rather
/// than being dropped. These finders key off HP / MP / tank-stance, not ClassJob, so they are the part
/// of card targeting that is unit-testable.
///
/// NOTE ON DPS CARDS (The Balance / The Spear): their target search classifies allies by role via
/// <see cref="Daedalus.Rotation.Common.Helpers.TrustPartyRoleHelper"/>, which reads
/// <c>IBattleChara.ClassJob.RowId</c>. That is real Excel-sheet data (a Lumina <c>RowRef</c>) and cannot
/// be constructed or mocked from this test assembly, so role-based selection is NOT covered here. It is
/// covered instead by (a) TrustPartyRoleHelperTests — JobRegistry role classification + the documented
/// fact that Trust WAR/RDM/PCT expose ClassJob on IBattleChara in-game — and (b) in-game Trust validation.
/// A "Trust NPC" ally is modelled below as a plain IBattleChara mock (not IPlayerCharacter); a "player"
/// ally as an IPlayerCharacter mock — the only distinction the finders actually branch on (MP reads).
/// </summary>
public sealed class AstraeaCardTargetingTests
{
    private static AstraeaPartyHelper Helper(IEnumerable<IBattleChara> members, Configuration config) =>
        new TestableAstraeaPartyHelper(members, config);

    private static Configuration Config() => AstraeaTestContext.CreateDefaultAstrologianConfiguration();

    private static IPlayerCharacter Caster()
    {
        // Entity 1 — the Sage/Astrologian casting the card; kept out of the members list.
        var m = MockBuilders.CreateMockPlayerCharacter(level: 100);
        m.Setup(x => x.EntityId).Returns(1u);
        m.Setup(x => x.GameObjectId).Returns(1ul);
        return m.Object;
    }

    [Fact]
    public void HealingCard_LandsOnInjuredTrustNpc()
    {
        var caster = Caster();
        var healthy = MockBuilders.CreateMockBattleChara(entityId: 2, currentHp: 49000, maxHp: 50000).Object; // 98%
        var injuredNpc = MockBuilders.CreateMockBattleChara(entityId: 3, currentHp: 25000, maxHp: 50000).Object; // 50%
        var helper = Helper(new[] { healthy, injuredNpc }, Config());

        var target = helper.FindHealingCardTarget(caster, 0.80f, ASTActions.TheArrowStatusId);

        Assert.Same(injuredNpc, target);
    }

    [Fact]
    public void HealingCard_LandsOnInjuredPlayer()
    {
        var caster = Caster();
        var healthyNpc = MockBuilders.CreateMockBattleChara(entityId: 2, currentHp: 49000, maxHp: 50000).Object;
        var injuredPlayerMock = MockBuilders.CreateMockPlayerCharacter(level: 100, currentHp: 20000, maxHp: 50000); // 40%
        injuredPlayerMock.Setup(x => x.EntityId).Returns(4u);
        injuredPlayerMock.Setup(x => x.GameObjectId).Returns(4ul);
        var injuredPlayer = injuredPlayerMock.Object;
        var helper = Helper(new IBattleChara[] { healthyNpc, injuredPlayer }, Config());

        var target = helper.FindHealingCardTarget(caster, 0.80f, ASTActions.TheEwerStatusId);

        Assert.Same(injuredPlayer, target);
    }

    [Fact]
    public void HealingCard_AllHealthy_ReturnsNull()
    {
        // No injured ally → finder returns null; the module then holds or self-dumps (config-driven),
        // rather than force-landing a heal card on a full-HP target.
        var caster = Caster();
        var a = MockBuilders.CreateMockBattleChara(entityId: 2, currentHp: 50000, maxHp: 50000).Object;
        var b = MockBuilders.CreateMockBattleChara(entityId: 3, currentHp: 49000, maxHp: 50000).Object;
        var helper = Helper(new[] { a, b }, Config());

        Assert.Null(helper.FindHealingCardTarget(caster, 0.80f, ASTActions.TheArrowStatusId));
    }

    [Fact]
    public void BoleCard_NoTank_LandsOnInjuredAlly()
    {
        // No tank resolvable (no ClassJob, no stance, no aggro) → The Bole falls back to the injured ally.
        var caster = Caster();
        var healthy = MockBuilders.CreateMockBattleChara(entityId: 2, currentHp: 49000, maxHp: 50000).Object;
        var injured = MockBuilders.CreateMockBattleChara(entityId: 3, currentHp: 30000, maxHp: 50000).Object; // 60%
        var helper = Helper(new[] { healthy, injured }, Config());

        var target = helper.FindBoleTarget(caster, 0.80f);

        Assert.Same(injured, target);
    }

    [Fact]
    public void SpireCard_LandsOnLowMpPlayer()
    {
        // The Spire prefers a low-MP ally. Only real players expose MP, so this is the player-specific path.
        var caster = Caster();
        var healthy = MockBuilders.CreateMockBattleChara(entityId: 2, currentHp: 50000, maxHp: 50000).Object;
        var lowMpMock = MockBuilders.CreateMockPlayerCharacter(level: 100, currentMp: 3000, maxMp: 10000); // 30% MP, full HP
        lowMpMock.Setup(x => x.EntityId).Returns(5u);
        lowMpMock.Setup(x => x.GameObjectId).Returns(5ul);
        var lowMpPlayer = lowMpMock.Object;
        var helper = Helper(new IBattleChara[] { healthy, lowMpPlayer }, Config());

        var target = helper.FindSpireTarget(caster, 0.80f);

        Assert.Same(lowMpPlayer, target);
    }

    [Fact]
    public void SpireCard_TrustNpcNoMp_FallsBackToInjuredHp()
    {
        // Trust NPCs are not IPlayerCharacter, so the MP branch is skipped; The Spire falls back to the
        // lowest-HP ally so the card still lands on the Trust NPC rather than defaulting to self.
        var caster = Caster();
        var healthy = MockBuilders.CreateMockBattleChara(entityId: 2, currentHp: 49000, maxHp: 50000).Object;
        var injuredNpc = MockBuilders.CreateMockBattleChara(entityId: 3, currentHp: 20000, maxHp: 50000).Object; // 40%
        var helper = Helper(new[] { healthy, injuredNpc }, Config());

        var target = helper.FindSpireTarget(caster, 0.80f);

        Assert.Same(injuredNpc, target);
    }

    [Fact]
    public void ResolveCardTarget_RoutesSupportCardsToInjuredAlly()
    {
        // Dispatch mapping: Arrow / Ewer / Bole / Spire all resolve onto the injured ally (Trust NPC here).
        var caster = Caster();
        var config = Config();
        var healthy = MockBuilders.CreateMockBattleChara(entityId: 2, currentHp: 49000, maxHp: 50000).Object;
        var injuredNpc = MockBuilders.CreateMockBattleChara(entityId: 3, currentHp: 25000, maxHp: 50000).Object;
        var helper = Helper(new[] { healthy, injuredNpc }, config);

        Assert.Same(injuredNpc, helper.ResolveCardTarget(caster, ASTActions.TheArrow, config.Astrologian));
        Assert.Same(injuredNpc, helper.ResolveCardTarget(caster, ASTActions.TheEwer, config.Astrologian));
        Assert.Same(injuredNpc, helper.ResolveCardTarget(caster, ASTActions.TheBole, config.Astrologian));
        Assert.Same(injuredNpc, helper.ResolveCardTarget(caster, ASTActions.TheSpire, config.Astrologian));
    }
}
