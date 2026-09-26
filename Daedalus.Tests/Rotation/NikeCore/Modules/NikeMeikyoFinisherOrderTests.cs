using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Moq;
using Daedalus.Data;
using Daedalus.Rotation.Common.Scheduling;
using Daedalus.Rotation.NikeCore.Abilities;
using Daedalus.Rotation.NikeCore.Modules;
using Daedalus.Services.Positional;
using Daedalus.Services.Positional.Navigation;
using Daedalus.Services.Targeting;
using Daedalus.Tests.Mocks;
using Daedalus.Tests.Rotation.Common.Scheduling;
using Xunit;

namespace Daedalus.Tests.Rotation.NikeCore.Modules;

/// <summary>
/// Which finisher a Meikyo Shisui stack goes on, and that the positional anticipation asks the mover for that
/// finisher's side. Field report 2026-09-25 (Saar under Minerva): after an Iaijutsu, Meikyo fired Gekko about
/// a second after going up while nothing had asked for the rear, so a SAM standing on the flank missed the
/// Gekko and landed the Kasha after it from the same spot -- 16 of 22 missed positionals in three boss fights.
/// </summary>
public class NikeMeikyoFinisherOrderTests
{
    private const int MeikyoFinisherPriority = 4;

    private readonly DamageModule _module = new();

    private AbilityBehavior? MeikyoPick(SAMActions.SenType sen, bool isAtFlank, bool isAtRear)
    {
        var enemy = new Mock<IBattleNpc>();
        enemy.Setup(x => x.GameObjectId).Returns(99999UL);
        enemy.Setup(x => x.CurrentHp).Returns(100000u);
        enemy.Setup(x => x.MaxHp).Returns(100000u);

        var targeting = MockBuilders.CreateMockTargetingService();
        targeting.Setup(x => x.FindEnemyForAction(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<uint>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);
        targeting.Setup(x => x.FindEnemy(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<float>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);

        var actionService = MockBuilders.CreateMockActionService();
        var config = NikeTestContext.CreateDefaultSamuraiConfiguration();
        var scheduler = SchedulerFactory.CreateForTest(config: config, actionService: actionService);
        var context = NikeTestContext.Create(
            config: config,
            actionService: actionService,
            targetingService: targeting,
            hasFugetsu: true,
            fugetsuRemaining: 30f,
            hasFuka: true,
            fukaRemaining: 30f,
            hasHiganbanaOnTarget: true,
            higanbanaRemaining: 60f,
            hasMeikyoShisui: true,
            meikyoStacks: 3,
            isAtFlank: isAtFlank,
            isAtRear: isAtRear,
            sen: sen);

        _module.CollectCandidates(context, scheduler, isMoving: false);
        return scheduler.InspectGcdQueue()
            .Where(c => c.Priority == MeikyoFinisherPriority)
            .Select(c => c.Behavior)
            .FirstOrDefault(b => b == NikeAbilities.Gekko || b == NikeAbilities.Kasha || b == NikeAbilities.Yukikaze);
    }

    [Fact]
    public void NoSen_OnFlank_KashaFirst()
        => Assert.Equal(NikeAbilities.Kasha, MeikyoPick(SAMActions.SenType.None, isAtFlank: true, isAtRear: false));

    [Fact]
    public void NoSen_AtRear_GekkoFirst()
        => Assert.Equal(NikeAbilities.Gekko, MeikyoPick(SAMActions.SenType.None, isAtFlank: false, isAtRear: true));

    [Fact]
    public void NoSen_InFront_GekkoFirst()
        => Assert.Equal(NikeAbilities.Gekko, MeikyoPick(SAMActions.SenType.None, isAtFlank: false, isAtRear: false));

    [Fact]
    public void KashaTaken_OnFlank_ThenGekko()
        => Assert.Equal(NikeAbilities.Gekko, MeikyoPick(SAMActions.SenType.Ka, isAtFlank: true, isAtRear: false));

    [Fact]
    public void GekkoTaken_AtRear_ThenKasha()
        => Assert.Equal(NikeAbilities.Kasha, MeikyoPick(SAMActions.SenType.Getsu, isAtFlank: false, isAtRear: true));

    [Fact]
    public void GetsuAndKaHeld_Yukikaze()
        => Assert.Equal(NikeAbilities.Yukikaze,
            MeikyoPick(SAMActions.SenType.Getsu | SAMActions.SenType.Ka, isAtFlank: true, isAtRear: false));

    /// <summary>
    /// The drift guard: for every Sen state and position, the side the anticipation asks for is the side of the
    /// finisher the rotation actually pushes (Yukikaze has none, so nothing is asked).
    /// </summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void AnticipatedSide_MatchesTheFinisherPushed(bool isAtFlank, bool isAtRear)
    {
        var provider = new SamuraiPositionalAnticipationProvider();
        for (var bits = 0; bits < 8; ++bits)
        {
            var sen = (SAMActions.SenType)bits;
            if (sen == (SAMActions.SenType.Setsu | SAMActions.SenType.Getsu | SAMActions.SenType.Ka))
                continue;   // three Sen: the rotation spends them on an Iaijutsu, not a Meikyo stack

            var pushed = MeikyoPick(sen, isAtFlank, isAtRear);
            var asked = provider.GetAnticipatedPositional(new PositionalAnticipationContext(
                LastComboAction: 0,
                PlayerLevel: 100,
                HasTrueNorth: false,
                TargetHasPositionalImmunity: false,
                IsAtRear: isAtRear,
                IsAtFlank: isAtFlank,
                HasMeikyoShisui: true,
                HasGetsuSen: (sen & SAMActions.SenType.Getsu) != 0,
                HasKaSen: (sen & SAMActions.SenType.Ka) != 0,
                HasSetsuSen: (sen & SAMActions.SenType.Setsu) != 0));

            PositionalType? expected = pushed == NikeAbilities.Gekko ? PositionalType.Rear
                : pushed == NikeAbilities.Kasha ? PositionalType.Flank
                : null;
            Assert.True(pushed != null, $"no Meikyo finisher pushed for {SAMActions.FormatSen(sen)}");
            Assert.True(expected == asked?.Required,
                $"{SAMActions.FormatSen(sen)} flank={isAtFlank} rear={isAtRear}: pushed {pushed!.Action.Name}, asked {asked?.Required.ToString() ?? "nothing"}");
        }
    }
}
