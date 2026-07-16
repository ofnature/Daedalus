using System;
using System.Linq;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Moq;
using Daedalus.Config.DPS;
using Daedalus.Data;
using Daedalus.Rotation.Common.Helpers;
using Daedalus.Rotation.ProteusCore.Abilities;
using Daedalus.Rotation.ProteusCore.Context;
using Daedalus.Rotation.ProteusCore.Modules;
using Daedalus.Services.Blu;
using Daedalus.Services.Targeting;
using Daedalus.Tests.Mocks;
using Daedalus.Tests.Rotation.Common.Scheduling;
using Xunit;

namespace Daedalus.Tests.Rotation.ProteusCore;

/// <summary>
/// BLU v3 multi-BLU coordination, rotation side: DoT ownership gates (v3.1), the owner's
/// Bristle-snapshot bleed rule (v3.1b), synced Moon Flute windows with T13 stagger (v3.2), and
/// the Gobskin/Cactguard coordinators (v3.3). BluCoordinationState is static — this class rides
/// the serialized BLU collection and resets it around every test.
/// </summary>
[Collection("BluStaticState")]
public class ProteusV3CoordinationTests : IDisposable
{
    public ProteusV3CoordinationTests() => BluCoordinationState.Reset();
    public void Dispose()
    {
        BluCoordinationState.Reset();
        BluCoordinationState.SignalBurstReady = null;
    }

    private static void Coordinate(
        bool bleed = true, bool mortalFlame = true, bool breath = true,
        bool gobskin = true, bool cactguard = true, int staggerDelay = 0, char group = 'A')
        => BluCoordinationState.Apply(new BluCoordinationSnapshot(
            CoordinationActive: true,
            IsBleedOwner: bleed,
            IsMortalFlameOwner: mortalFlame,
            IsBreathOfMagicOwner: breath,
            IsGobskinOwner: gobskin,
            IsCactguardOwner: cactguard,
            StaggerGroup: group,
            MoonFluteStaggerDelaySeconds: staggerDelay,
            Summary: "test"));

    // ── v3.1 DoT ownership ──────────────────────────────────────────────────

    [Fact]
    public void NonOwner_SkipsMortalFlame_OwnerDotStillFlows()
    {
        var h = new Harness();
        Coordinate(mortalFlame: false); // someone else owns MF; we still own BoM

        h.Damage.CollectCandidates(h.Context, h.Scheduler, isMoving: false);

        var gcd = h.Scheduler.InspectGcdQueue();
        Assert.DoesNotContain(gcd, c => c.Behavior == ProteusAbilities.MortalFlame);
        Assert.Contains(gcd, c => c.Behavior == ProteusAbilities.BreathOfMagic);
    }

    [Fact]
    public void NonOwner_SkipsBreathOfMagic_OwnerDotStillFlows()
    {
        var h = new Harness();
        Coordinate(breath: false);

        h.Damage.CollectCandidates(h.Context, h.Scheduler, isMoving: false);

        var gcd = h.Scheduler.InspectGcdQueue();
        Assert.DoesNotContain(gcd, c => c.Behavior == ProteusAbilities.BreathOfMagic);
        Assert.Contains(gcd, c => c.Behavior == ProteusAbilities.MortalFlame);
    }

    [Fact]
    public void NonOwner_NeverCastsBleedFamily()
    {
        // Bleeding 1714 is shared — a second BLU's Song of Torment clobbers the owner's snapshot.
        var h = new Harness();
        h.Targeting.Setup(x => x.FindEnemyNeedingDot(
                It.IsAny<uint>(), It.IsAny<float>(), It.IsAny<float>(), It.IsAny<IPlayerCharacter>()))
            .Returns(h.Enemy.Object); // the solo path WOULD refresh here
        Coordinate(bleed: false);

        h.Damage.CollectCandidates(h.Context, h.Scheduler, isMoving: false);

        Assert.DoesNotContain(h.Scheduler.InspectGcdQueue(), c => c.Behavior == ProteusAbilities.SongOfTorment);
    }

    [Fact]
    public void BleedOwner_RefreshesWithBristleSnapshot()
    {
        // v3.1b owner rule: the coordinated bleed refresh rides the Bristle-snapshot block —
        // never a plain unbuffed overwrite.
        var h = new Harness();
        // Only the bleed needs work this frame (MF/BoM owned by others).
        Coordinate(bleed: true, mortalFlame: false, breath: false);
        h.Targeting.Setup(x => x.FindEnemyNeedingDot(
                It.IsAny<uint>(), It.IsAny<float>(), It.IsAny<float>(), It.IsAny<IPlayerCharacter>()))
            .Returns(h.Enemy.Object);

        h.Damage.CollectCandidates(h.Context, h.Scheduler, isMoving: false);

        var gcd = h.Scheduler.InspectGcdQueue();
        var bristle = gcd.FirstOrDefault(c => c.Behavior == ProteusAbilities.Bristle);
        var sot = gcd.FirstOrDefault(c => c.Behavior == ProteusAbilities.SongOfTorment);
        Assert.NotNull(bristle);
        Assert.NotNull(sot);
        Assert.True(bristle!.Priority < sot!.Priority, "Bristle must snapshot before the bleed");
    }

    [Fact]
    public void SoloDefault_SongOfTorment_PlainRefreshUnchanged()
    {
        // Coordination inactive (single BLU) → the field-validated v1 refresh path, no Bristle
        // requirement on the bleed.
        var h = new Harness();
        h.Targeting.Setup(x => x.FindEnemyNeedingDot(
                It.IsAny<uint>(), It.IsAny<float>(), It.IsAny<float>(), It.IsAny<IPlayerCharacter>()))
            .Returns(h.Enemy.Object);

        h.Damage.CollectCandidates(h.Context, h.Scheduler, isMoving: false);

        Assert.Contains(h.Scheduler.InspectGcdQueue(), c => c.Behavior == ProteusAbilities.SongOfTorment);
    }

    // ── v3.2 synced Moon Flute ──────────────────────────────────────────────

    private static Harness FluteReadyHarness()
    {
        var h = new Harness();
        h.Config.BlueMage.EnableMoonFlute = true;
        h.ActionService.Setup(x => x.GetCurrentCharges(BLUActions.Surpanakha.ActionId)).Returns(4u);
        return h;
    }

    [Fact]
    public void SyncedFlute_WaitsForBurstSignal_AndAnnouncesReadiness()
    {
        var h = FluteReadyHarness();
        Coordinate();
        var readySignals = 0;
        BluCoordinationState.SignalBurstReady = () => readySignals++;

        h.Damage.CollectCandidates(h.Context, h.Scheduler, isMoving: false);

        Assert.DoesNotContain(h.Scheduler.InspectGcdQueue(), c => c.Behavior == ProteusAbilities.MoonFlute);
        Assert.Equal(1, readySignals); // pieces ready → BurstReady announced (throttled)
        Assert.Contains("waiting for party burst signal", h.Context.Debug.DamageState);
    }

    [Fact]
    public void SyncedFlute_FiresOnBurstSignal()
    {
        var h = FluteReadyHarness();
        Coordinate();
        var now = DateTime.UtcNow;
        h.Damage.UtcNow = () => now;
        BluCoordinationState.NotifyBurstFire(now);

        h.Damage.CollectCandidates(h.Context, h.Scheduler, isMoving: false);

        Assert.Contains(h.Scheduler.InspectGcdQueue(), c => c.Behavior == ProteusAbilities.MoonFlute);
    }

    [Fact]
    public void SyncedFlute_GroupB_Waits30sAfterSignal_ThenFires()
    {
        var h = FluteReadyHarness();
        Coordinate(staggerDelay: BluCoordinationCalculator.StaggerDelaySeconds, group: 'B');
        var signal = DateTime.UtcNow;
        BluCoordinationState.NotifyBurstFire(signal);

        // 5s after the signal: group B still holds.
        var now = signal.AddSeconds(5);
        h.Damage.UtcNow = () => now;
        h.Damage.CollectCandidates(h.Context, h.Scheduler, isMoving: false);
        Assert.DoesNotContain(h.Scheduler.InspectGcdQueue(), c => c.Behavior == ProteusAbilities.MoonFlute);
        Assert.Contains("staggered start", h.Context.Debug.DamageState);

        // 31s after: the stagger window is open.
        now = signal.AddSeconds(31);
        h.Scheduler.Reset();
        h.Damage.CollectCandidates(h.Context, h.Scheduler, isMoving: false);
        Assert.Contains(h.Scheduler.InspectGcdQueue(), c => c.Behavior == ProteusAbilities.MoonFlute);
    }

    [Fact]
    public void SyncedFlute_StaleSignal_NeverFires()
    {
        // A signal older than delay+grace must not start a window (someone rejoining mid-fight
        // an hour later would otherwise solo-Flute off an ancient timestamp).
        var h = FluteReadyHarness();
        Coordinate();
        var signal = DateTime.UtcNow;
        BluCoordinationState.NotifyBurstFire(signal);
        h.Damage.UtcNow = () => signal.AddSeconds(60);

        h.Damage.CollectCandidates(h.Context, h.Scheduler, isMoving: false);

        Assert.DoesNotContain(h.Scheduler.InspectGcdQueue(), c => c.Behavior == ProteusAbilities.MoonFlute);
    }

    [Fact]
    public void SyncDisabled_FluteFiresSolo_EvenWithCoordination()
    {
        var h = FluteReadyHarness();
        h.Config.BlueMage.SyncMoonFluteWithParty = false;
        Coordinate();

        h.Damage.CollectCandidates(h.Context, h.Scheduler, isMoving: false);

        Assert.Contains(h.Scheduler.InspectGcdQueue(), c => c.Behavior == ProteusAbilities.MoonFlute);
    }

    // ── v3.3 Gobskin coordinator ────────────────────────────────────────────

    [Fact]
    public void Gobskin_NonOwner_Suppressed_OwnerPushes()
    {
        var nonOwner = new Harness(role: BluRole.Healer);
        Mock.Get(nonOwner.Context).Setup(x => x.PartyHealthMetrics).Returns((0.6f, 0.5f, 2));
        Coordinate(gobskin: false);
        nonOwner.Healing.CollectCandidates(nonOwner.Context, nonOwner.Scheduler, isMoving: false);
        Assert.DoesNotContain(nonOwner.Scheduler.InspectGcdQueue(), c => c.Behavior == ProteusAbilities.Gobskin);
        Assert.Equal("Gobskin: another BLU owns the barrier", nonOwner.Context.Debug.HealingState);

        var owner = new Harness(role: BluRole.Healer);
        Mock.Get(owner.Context).Setup(x => x.PartyHealthMetrics).Returns((0.6f, 0.5f, 2));
        Coordinate(gobskin: true);
        owner.Healing.CollectCandidates(owner.Context, owner.Scheduler, isMoving: false);
        Assert.Contains(owner.Scheduler.InspectGcdQueue(), c => c.Behavior == ProteusAbilities.Gobskin);
    }

    // ── v3.3 Cactguard ──────────────────────────────────────────────────────

    [Fact]
    public void Cactguard_NoBusterForecast_NeverPushes()
    {
        var h = new Harness();
        Coordinate();
        BluCoordinationState.NextTankbusterInSeconds = float.MaxValue; // no BMR / nothing coming

        h.Mitigation.CollectCandidates(h.Context, h.Scheduler, isMoving: false);

        Assert.DoesNotContain(h.Scheduler.InspectGcdQueue(), c => c.Behavior == ProteusAbilities.Cactguard);
    }

    [Fact]
    public void Cactguard_NonOwner_Suppressed()
    {
        var h = new Harness();
        Coordinate(cactguard: false);
        BluCoordinationState.NextTankbusterInSeconds = 3f;

        h.Mitigation.CollectCandidates(h.Context, h.Scheduler, isMoving: false);

        Assert.DoesNotContain(h.Scheduler.InspectGcdQueue(), c => c.Behavior == ProteusAbilities.Cactguard);
    }

    [Fact]
    public void Cactguard_TankRole_NeverCasts()
    {
        // The tank is the TARGET — even as the sole BLU it must not try to Cactguard.
        var h = new Harness(role: BluRole.Tank);
        BluCoordinationState.NextTankbusterInSeconds = 3f;

        h.Mitigation.CollectCandidates(h.Context, h.Scheduler, isMoving: false);

        Assert.DoesNotContain(h.Scheduler.InspectGcdQueue(), c => c.Behavior == ProteusAbilities.Cactguard);
    }

    [Fact]
    public void Cactguard_ForecastButNoTankInParty_NoPush()
    {
        // Owner + imminent buster, but the scan finds no tank (solo party) → nothing to protect.
        var h = new Harness();
        Coordinate();
        BluCoordinationState.NextTankbusterInSeconds = 3f;

        h.Mitigation.CollectCandidates(h.Context, h.Scheduler, isMoving: false);

        Assert.DoesNotContain(h.Scheduler.InspectGcdQueue(), c => c.Behavior == ProteusAbilities.Cactguard);
    }

    // ── Heartbeat wire format ───────────────────────────────────────────────

    [Fact]
    public void Heartbeat_CapabilityField_RoundTrips_AndDefaultsToZero()
    {
        var payload = new Daedalus.Services.Network.LanHeartbeatPayload
        {
            CharacterName = "Saar",
            BluCapabilities = (uint)(BluCapabilities.SongOfTorment | BluCapabilities.MoonFlute),
        };
        var parsed = Daedalus.Services.Network.LanHeartbeatPayload.FromJson(payload.ToJson());
        Assert.NotNull(parsed);
        Assert.Equal(payload.BluCapabilities, parsed!.BluCapabilities);

        // Pre-cap clients: the field is simply absent → 0 (no capabilities, never elected).
        var old = Daedalus.Services.Network.LanHeartbeatPayload.FromJson("""{"n":"Old"}""");
        Assert.NotNull(old);
        Assert.Equal(0u, old!.BluCapabilities);
    }

    // ── Harness (wave-2 pattern) ────────────────────────────────────────────

    private sealed class Harness
    {
        public IProteusContext Context { get; }
        public Daedalus.Rotation.Common.Scheduling.RotationScheduler Scheduler { get; }
        public DamageModule Damage { get; } = new();
        public HealingModule Healing { get; } = new();
        public MitigationModule Mitigation { get; } = new();
        public Mock<ITargetingService> Targeting { get; }
        public Mock<Daedalus.Services.Action.IActionService> ActionService { get; }
        public Mock<IBattleNpc> Enemy { get; }
        public Configuration Config { get; }

        public Harness(BluRole role = BluRole.Dps, int partySize = 0)
        {
            Config = new Configuration();
            Config.BlueMage.Role = role;

            Enemy = new Mock<IBattleNpc>();
            Enemy.Setup(x => x.GameObjectId).Returns(4242UL);
            Enemy.Setup(x => x.CurrentHp).Returns(100000u);
            Enemy.Setup(x => x.MaxHp).Returns(100000u);

            Targeting = MockBuilders.CreateMockTargetingService();
            Targeting.Setup(x => x.IsDamageTargetingPaused()).Returns(false);
            Targeting.Setup(x => x.FindEnemy(
                    It.IsAny<EnemyTargetingStrategy>(), It.IsAny<float>(), It.IsAny<IPlayerCharacter>()))
                .Returns(Enemy.Object);
            Targeting.Setup(x => x.FindEnemyNeedingDot(
                    It.IsAny<uint>(), It.IsAny<float>(), It.IsAny<float>(), It.IsAny<IPlayerCharacter>()))
                .Returns((IBattleNpc?)null);
            Targeting.Setup(x => x.CountEnemiesInRange(It.IsAny<float>(), It.IsAny<IPlayerCharacter>()))
                .Returns(1);

            ActionService = MockBuilders.CreateMockActionService();
            ActionService.Setup(x => x.IsActionLearned(It.IsAny<uint>())).Returns(true);
            ActionService.Setup(x => x.GcdRemaining).Returns(1.2f);

            var player = MockBuilders.CreateMockPlayerCharacter(level: 80, currentHp: 100000, maxHp: 100000);

            var objectTable = MockBuilders.CreateMockObjectTable();
            var partyList = MockBuilders.CreateMockPartyList(length: partySize);

            var mock = new Mock<IProteusContext>();
            mock.Setup(x => x.Player).Returns(player.Object);
            mock.Setup(x => x.InCombat).Returns(true);
            mock.Setup(x => x.Configuration).Returns(Config);
            mock.Setup(x => x.ActionService).Returns(ActionService.Object);
            mock.Setup(x => x.TargetingService).Returns(Targeting.Object);
            mock.Setup(x => x.Role).Returns(role);
            mock.Setup(x => x.HasCorrectMimicry).Returns(true);
            mock.Setup(x => x.IsSpellUsable(It.IsAny<uint>())).Returns(true);
            mock.Setup(x => x.CurrentMp).Returns(10000);
            mock.Setup(x => x.PartyHealthMetrics).Returns((1f, 1f, 0));
            mock.Setup(x => x.Debug).Returns(new ProteusDebugState());
            mock.Setup(x => x.PartyList).Returns(partyList.Object);
            mock.Setup(x => x.ObjectTable).Returns(objectTable.Object);
            mock.Setup(x => x.PartyHelper).Returns(new CasterPartyHelper(objectTable.Object, partyList.Object));
            mock.Setup(x => x.DebuffDetectionService).Returns(MockBuilders.CreateMockDebuffDetectionService().Object);

            Context = mock.Object;
            Scheduler = SchedulerFactory.CreateForTest(actionService: ActionService, config: Config);
        }
    }
}
