using System;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Moq;
using Daedalus.Config;
using Daedalus.Services;
using Daedalus.Services.Analytics;
using Daedalus.Windows;
using Xunit;

namespace Daedalus.Tests.Services.Analytics;

public sealed class DpsMeterServiceTests
{
    private static readonly CombatantIdentity Self = new(1, CombatantKind.Self, "Prometheus Kai", "MCH");
    private static readonly CombatantIdentity Ally = new(2, CombatantKind.Player, "Nikephoros Astra", "SAM");
    private static readonly CombatantIdentity Trust = new(3, CombatantKind.Support, "Alisaie", "RDM");

    // ── DpsEncounter: pure accumulation ──

    [Fact]
    public void Encounter_AccumulatesDamageAndHits()
    {
        var enc = new DpsEncounter { DurationSeconds = 10f };

        enc.AddDamage(Self, "Dummy", 1000, isCrit: true, isDirectHit: false);
        enc.AddDamage(Self, "Dummy", 3000, isCrit: false, isDirectHit: true);

        var stats = Assert.Single(enc.GetRanked());
        Assert.Equal(4000, stats.TotalDamage);
        Assert.Equal(2, stats.HitCount);
        Assert.Equal(50f, stats.CritPercent);
        Assert.Equal(50f, stats.DirectHitPercent);
        Assert.Equal(400f, enc.GetDps(stats));
        Assert.Equal(4000, enc.TotalDamage);
    }

    [Fact]
    public void Encounter_RanksByTotalDamageDescending()
    {
        var enc = new DpsEncounter();

        enc.AddDamage(Self, "Dummy", 100, false, false);
        enc.AddDamage(Ally, "Dummy", 900, false, false);
        enc.AddDamage(Trust, "Dummy", 500, false, false);

        var ranked = enc.GetRanked();
        uint[] actualOrder = [ranked[0].EntityId, ranked[1].EntityId, ranked[2].EntityId];
        Assert.Equal(new uint[] { 2, 3, 1 }, actualOrder);
    }

    [Fact]
    public void Encounter_DamageShareSumsToOne()
    {
        var enc = new DpsEncounter();
        enc.AddDamage(Self, "Dummy", 750, false, false);
        enc.AddDamage(Ally, "Dummy", 250, false, false);

        var ranked = enc.GetRanked();
        Assert.Equal(0.75f, enc.GetDamageShare(ranked[0]), 3);
        Assert.Equal(0.25f, enc.GetDamageShare(ranked[1]), 3);
    }

    [Fact]
    public void Encounter_TitleFollowsMostDamagedTarget()
    {
        var enc = new DpsEncounter();

        enc.AddDamage(Self, "Trash Add", 500, false, false);
        Assert.Equal("Trash Add", enc.TargetName);

        enc.AddDamage(Self, "Honey B. Lovely", 9000, false, false);
        Assert.Equal("Honey B. Lovely", enc.TargetName);

        // Later small hits on the add don't steal the title back
        enc.AddDamage(Ally, "Trash Add", 100, false, false);
        Assert.Equal("Honey B. Lovely", enc.TargetName);
    }

    [Fact]
    public void Encounter_IgnoresDamageOnceEnded()
    {
        var enc = new DpsEncounter();
        enc.AddDamage(Self, "Dummy", 100, false, false);
        enc.IsActive = false;

        enc.AddDamage(Self, "Dummy", 100, false, false);

        Assert.Equal(100, enc.TotalDamage);
    }

    // ── Service: segmentation + queue draining ──

    private sealed class TestClock
    {
        public DateTime Now = new(2026, 7, 3, 16, 0, 0, DateTimeKind.Utc);
        public void Advance(double seconds) => Now = Now.AddSeconds(seconds);
    }

    private static (DpsMeterService Service, Mock<ICombatEventService> Ces, ParserConfig Config, TestClock Clock) MakeService(
        Func<uint, uint, ResolvedDamage?>? resolver = null,
        Func<string, bool>? isPartyMember = null,
        Func<ushort>? territory = null)
    {
        var ces = new Mock<ICombatEventService>();
        var config = new ParserConfig();
        resolver ??= (casterId, _) => casterId switch
        {
            1 => new ResolvedDamage(Self, "Dummy"),
            2 => new ResolvedDamage(Ally, "Dummy"),
            _ => null,
        };
        var clock = new TestClock();
        var svc = new DpsMeterService(ces.Object, config, resolver, isPartyMember, territory)
        {
            UtcNow = () => clock.Now,
        };
        return (svc, ces, config, clock);
    }

    private static void SetCombat(Mock<ICombatEventService> ces, bool inCombat)
        => ces.Setup(x => x.IsInCombat).Returns(inCombat);

    /// <summary>Ends combat and waits out the encounter linger so the fight finalizes.</summary>
    private static void EndCombatAndLinger(DpsMeterService svc, Mock<ICombatEventService> ces, TestClock clock)
    {
        SetCombat(ces, false);
        svc.Update();
        clock.Advance(DpsMeterService.EncounterLingerSeconds + 1);
        svc.Update();
    }

    [Fact]
    public void Service_StartsEncounterOnCombatEntry_AndAccumulates()
    {
        var (svc, ces, _, clock) = MakeService();

        SetCombat(ces, true);
        svc.Update();
        ces.Raise(x => x.OnDamageDealt += null, new DamageDealtEvent(1, 100, 2500, 7, false, false));
        clock.Advance(5);
        svc.Update();

        Assert.NotNull(svc.Current);
        Assert.Equal(2500, svc.Current!.TotalDamage);
        Assert.Equal(5f, svc.Current.DurationSeconds);
    }

    [Fact]
    public void Service_UnresolvableCasters_AreDropped()
    {
        var (svc, ces, _, _) = MakeService();

        SetCombat(ces, true);
        svc.Update();
        ces.Raise(x => x.OnDamageDealt += null, new DamageDealtEvent(99, 100, 5000, 7, false, false));
        svc.Update();

        Assert.Equal(0, svc.Current!.TotalDamage);
    }

    [Fact]
    public void Service_CombatEnd_FreezesEncounterIntoHistory()
    {
        var (svc, ces, _, clock) = MakeService();

        SetCombat(ces, true);
        svc.Update();
        ces.Raise(x => x.OnDamageDealt += null, new DamageDealtEvent(1, 100, 6000, 7, false, false));
        clock.Advance(30);
        svc.Update();

        EndCombatAndLinger(svc, ces, clock);

        Assert.Null(svc.Current);
        var ended = Assert.Single(svc.History);
        Assert.False(ended.IsActive);
        Assert.Equal(30f, ended.DurationSeconds); // frozen at the last in-combat tick
        Assert.Equal(6000, ended.TotalDamage);
    }

    [Fact]
    public void Service_CombatFlicker_WithinLinger_KeepsOneEncounter()
    {
        // Phase cutscenes drop the combat flag briefly — one fight must stay one encounter.
        var (svc, ces, _, clock) = MakeService();

        SetCombat(ces, true);
        svc.Update();
        ces.Raise(x => x.OnDamageDealt += null, new DamageDealtEvent(1, 100, 1000, 7, false, false));
        clock.Advance(20);
        svc.Update();

        SetCombat(ces, false);   // cutscene starts
        clock.Advance(5);        // shorter than the linger
        svc.Update();

        SetCombat(ces, true);    // cutscene over
        ces.Raise(x => x.OnDamageDealt += null, new DamageDealtEvent(1, 100, 2000, 7, false, false));
        clock.Advance(5);
        svc.Update();

        Assert.NotNull(svc.Current);
        Assert.Empty(svc.History);
        Assert.Equal(3000, svc.Current!.TotalDamage); // both phases in one encounter
        Assert.Equal(30f, svc.Current.DurationSeconds);
    }

    [Fact]
    public void Service_EmptyFights_DoNotEnterHistory()
    {
        var (svc, ces, _, clock) = MakeService();

        SetCombat(ces, true);
        svc.Update();
        EndCombatAndLinger(svc, ces, clock);

        Assert.Empty(svc.History);
    }

    [Fact]
    public void Service_HistoryCapped_NewestFirst()
    {
        var (svc, ces, config, clock) = MakeService();
        config.FightHistoryCount = 2;

        for (var i = 1; i <= 3; i++)
        {
            SetCombat(ces, true);
            svc.Update();
            ces.Raise(x => x.OnDamageDealt += null, new DamageDealtEvent(1, 100, i * 1000, 7, false, false));
            svc.Update();
            EndCombatAndLinger(svc, ces, clock);
        }

        Assert.Equal(2, svc.History.Count);
        Assert.Equal(3000, svc.History[0].TotalDamage);
        Assert.Equal(2000, svc.History[1].TotalDamage);
    }

    [Fact]
    public void Service_DisabledConfig_DropsEvents()
    {
        var (svc, ces, config, _) = MakeService();
        config.Enabled = false;

        SetCombat(ces, true);
        svc.Update();
        ces.Raise(x => x.OnDamageDealt += null, new DamageDealtEvent(1, 100, 2500, 7, false, false));
        svc.Update();

        Assert.Equal(0, svc.Current!.TotalDamage);
    }

    [Fact]
    public void Service_Reset_ClearsCurrentAndHistory()
    {
        var (svc, ces, _, clock) = MakeService();

        SetCombat(ces, true);
        svc.Update();
        ces.Raise(x => x.OnDamageDealt += null, new DamageDealtEvent(1, 100, 2500, 7, false, false));
        svc.Update();
        EndCombatAndLinger(svc, ces, clock);

        svc.Reset();

        Assert.Null(svc.Current);
        Assert.Empty(svc.History);
    }

    [Fact]
    public void Service_PreCombatQueueLeftovers_DoNotLeakIntoNewFight()
    {
        var (svc, ces, _, _) = MakeService();

        // Event arrives while out of combat (e.g. pull spell before server combat flag)
        SetCombat(ces, false);
        svc.Update();
        ces.Raise(x => x.OnDamageDealt += null, new DamageDealtEvent(1, 100, 9999, 7, false, false));

        SetCombat(ces, true);
        svc.Update();

        Assert.Equal(0, svc.Current!.TotalDamage);
    }

    // ── Default resolver: Trust NPCs get their own Support row (regression: trusts missing from parser) ──

    [Fact]
    public void DefaultResolver_TrustNpcCaster_GetsSupportRow()
    {
        // Realistic in-combat avatar: the game sets Hostile|InCombat flags on fighting trust
        // allies AND gives avatars an OwnerId (the summoning player). Neither may divert the
        // avatar from its own Support row — flag-gating dropped them, OwnerId merged their
        // damage into the player ("trust/support combined" field reports).
        var trustNpc = new Mock<IBattleNpc>();
        trustNpc.Setup(x => x.ObjectKind).Returns(ObjectKind.BattleNpc);
        trustNpc.Setup(x => x.SubKind).Returns((byte)Daedalus.Data.FFXIVConstants.TrustNpcSubKind);
        trustNpc.Setup(x => x.EntityId).Returns(2u);
        trustNpc.Setup(x => x.OwnerId).Returns(1u);
        trustNpc.Setup(x => x.CurrentHp).Returns(50000u);
        trustNpc.Setup(x => x.MaxHp).Returns(50000u);
        trustNpc.Setup(x => x.StatusFlags).Returns(StatusFlags.Hostile | StatusFlags.InCombat);
        trustNpc.Setup(x => x.Name).Returns(new SeString(new TextPayload("Thancred's Avatar")));

        var enemyNpc = new Mock<IBattleNpc>();
        enemyNpc.Setup(x => x.BattleNpcKind).Returns((Dalamud.Game.ClientState.Objects.Enums.BattleNpcSubKind)Daedalus.Compat.BattleNpcKinds.Combatant);
        enemyNpc.Setup(x => x.SubKind).Returns(Daedalus.Compat.BattleNpcKinds.Combatant);
        enemyNpc.Setup(x => x.Name).Returns(new SeString(new TextPayload("Sanduruva")));

        var objectTable = new Mock<Dalamud.Plugin.Services.IObjectTable>();
        objectTable.Setup(x => x.SearchById(2u)).Returns(trustNpc.Object);
        objectTable.Setup(x => x.SearchById(100u)).Returns(enemyNpc.Object);
        objectTable.Setup(x => x.LocalPlayer).Returns((Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter?)null);

        var ces = new Mock<ICombatEventService>();
        var svc = new DpsMeterService(ces.Object, objectTable.Object, new ParserConfig());

        SetCombat(ces, true);
        svc.Update();
        ces.Raise(x => x.OnDamageDealt += null, new DamageDealtEvent(2, 100, 4300, 7, false, false));
        svc.Update();

        var stats = Assert.Single(svc.Current!.GetRanked());
        Assert.Equal(CombatantKind.Support, stats.Kind);
        Assert.Equal("Thancred's Avatar", stats.Name);
        Assert.Equal(4300, stats.TotalDamage);
        Assert.Equal("Sanduruva", svc.Current.TargetName);
    }

    // ── Milestone 2: remote self-reports over IPC/LAN ──

    private static Daedalus.Services.Network.LanDpsReportPayload MakeReport(
        string name = "Nikephoros Astra", long damage = 500_000, long startTicks = 0,
        float duration = 60f, float crit = 25f, float dh = 40f)
        => new()
        {
            CharacterName = name,
            JobAbbrev = "SAM",
            EncounterStartTicks = startTicks,
            TotalDamage = damage,
            DurationSeconds = duration,
            CritPercent = crit,
            DirectHitPercent = dh,
        };

    [Fact]
    public void ApplyRemoteReport_OverridesLocallyObservedRow()
    {
        var enc = new DpsEncounter { DurationSeconds = 60f };
        enc.AddDamage(Ally, "Dummy", 300_000, false, false); // observed (range-culled, low)
        enc.AddDamage(Self, "Dummy", 400_000, false, false);

        enc.ApplyRemoteReport(MakeReport(name: Ally.Name, damage: 500_000, duration: 62f));

        var ranked = enc.GetRanked();
        Assert.Equal(Ally.Key, ranked[0].EntityId); // reported 500k now outranks self 400k
        Assert.True(ranked[0].IsSelfReported);
        Assert.Equal(500_000, ranked[0].EffectiveDamage);
        // ONE clock for everything: local encounter duration, so row DPS, share and party
        // DPS stay mutually consistent (reported duration is informational only).
        Assert.Equal(500_000f / 60f, enc.GetDps(ranked[0]), 1);
        Assert.Equal(25f, ranked[0].CritPercent);               // reported crit wins
        Assert.Equal(500_000f / 900_000f, enc.GetDamageShare(ranked[0]), 3); // effective total
    }

    [Fact]
    public void ApplyRemoteReport_UnseenSender_CreatesRow()
    {
        var enc = new DpsEncounter { DurationSeconds = 60f };
        enc.AddDamage(Self, "Dummy", 400_000, false, false);

        enc.ApplyRemoteReport(MakeReport(name: "Chroma Wilde", damage: 450_000));

        var ranked = enc.GetRanked();
        Assert.Equal(2, ranked.Count);
        Assert.Equal("Chroma Wilde", ranked[0].Name);
        Assert.Equal(CombatantKind.Player, ranked[0].Kind);
        Assert.True(ranked[0].IsSelfReported);
    }

    [Fact]
    public void ApplyRemoteReport_RepeatedReports_UpdateInPlace()
    {
        var enc = new DpsEncounter();
        enc.ApplyRemoteReport(MakeReport(damage: 100_000));
        enc.ApplyRemoteReport(MakeReport(damage: 250_000));

        var row = Assert.Single(enc.GetRanked());
        Assert.Equal(250_000, row.EffectiveDamage);
    }

    [Fact]
    public void BuildSelfReport_UsesSelfRow()
    {
        var enc = new DpsEncounter { DurationSeconds = 90f };
        enc.AddDamage(Self, "Dummy", 800_000, true, false);
        enc.AddDamage(Self, "Dummy", 200_000, false, true);
        enc.AddDamage(Ally, "Dummy", 999_999, false, false);

        var report = DpsMeterService.BuildSelfReport(enc, isFinal: true);

        Assert.NotNull(report);
        Assert.Equal(Self.Name, report!.CharacterName);
        Assert.Equal(1_000_000, report.TotalDamage);
        Assert.Equal(90f, report.DurationSeconds);
        Assert.Equal(50f, report.CritPercent);
        Assert.True(report.IsFinal);
    }

    [Fact]
    public void BuildSelfReport_NoSelfDamage_ReturnsNull()
    {
        var enc = new DpsEncounter();
        enc.AddDamage(Ally, "Dummy", 100, false, false);

        Assert.Null(DpsMeterService.BuildSelfReport(enc, isFinal: false));
    }

    [Fact]
    public void ApplyRemoteReport_SenderEncounterRestart_AccumulatesSegments()
    {
        // The sender's combat flag flickered → its encounter restarted → its cumulative
        // counter reset. The receiver must bank the finished segment, not go backwards.
        var enc = new DpsEncounter();
        enc.ApplyRemoteReport(MakeReport(damage: 100_000));
        enc.ApplyRemoteReport(MakeReport(damage: 120_000)); // same segment, growing
        enc.ApplyRemoteReport(MakeReport(damage: 5_000));   // reset detected

        var row = Assert.Single(enc.GetRanked());
        Assert.Equal(125_000, row.EffectiveDamage); // 120k banked + 5k new segment
    }

    [Fact]
    public void ShouldAcceptReport_NonPartyMember_Rejected()
    {
        // Cross-bleed regression (observed live): a toon in a DIFFERENT duty broadcast its
        // parse and it merged into this fight. Only party members may merge.
        var (svc, _, _, _) = MakeService(isPartyMember: name => name == "Nikephoros Astra");

        Assert.True(svc.ShouldAcceptReport(MakeReport(name: "Nikephoros Astra")));
        Assert.False(svc.ShouldAcceptReport(MakeReport(name: "Korha Ishere")));
    }

    [Fact]
    public void ShouldAcceptReport_DifferentTerritory_Rejected()
    {
        var (svc, _, _, _) = MakeService(isPartyMember: _ => true, territory: () => 1044);

        var sameZone = MakeReport();
        sameZone.TerritoryId = 1044;
        var otherZone = MakeReport();
        otherZone.TerritoryId = 152;

        Assert.True(svc.ShouldAcceptReport(sameZone));
        Assert.False(svc.ShouldAcceptReport(otherZone));
    }

    [Fact]
    public void FindEncounterForReport_LiveEncounter_AcceptsRegardlessOfStartDrift()
    {
        // Partied toons share the fight even when a cutscene shifted one side's start.
        var (svc, ces, _, _) = MakeService();
        SetCombat(ces, true);
        svc.Update();

        var drifted = MakeReport(startTicks: svc.Current!.StartUtc.Ticks + 60 * TimeSpan.TicksPerSecond);
        Assert.NotNull(svc.FindEncounterForReport(drifted));
    }

    [Fact]
    public void FindEncounterForReport_FinalReportAfterCombatEnd_MatchesRecentHistoryOnly()
    {
        var (svc, ces, _, clock) = MakeService();
        SetCombat(ces, true);
        svc.Update();
        ces.Raise(x => x.OnDamageDealt += null, new DamageDealtEvent(1, 100, 6000, 7, false, false));
        svc.Update();
        var start = svc.Current!.StartUtc.Ticks;

        EndCombatAndLinger(svc, ces, clock);

        var trailingFinal = MakeReport(startTicks: start + 2 * TimeSpan.TicksPerSecond);
        var matched = svc.FindEncounterForReport(trailingFinal);
        Assert.NotNull(matched);
        Assert.False(matched!.IsActive);

        var unrelated = MakeReport(startTicks: start + 300 * TimeSpan.TicksPerSecond);
        Assert.Null(svc.FindEncounterForReport(unrelated));
    }

    // ── Effect decode + formatting ──

    [Theory]
    [InlineData((ushort)1234, (byte)0, (byte)0, 1234)]                  // plain value
    [InlineData((ushort)5000, (byte)2, (byte)0x40, 5000 + 2 * 65536)]   // large-value bit set
    [InlineData((ushort)5000, (byte)2, (byte)0x00, 5000)]               // param3 ignored without the bit
    [InlineData(ushort.MaxValue, (byte)255, (byte)0x40, 65535 + 255 * 65536)]
    public void DecodeEffectValue_UnpacksExtendedDamage(ushort value, byte param3, byte param4, int expected)
    {
        Assert.Equal(expected, CombatEventService.DecodeEffectValue(value, param3, param4));
    }

    [Theory]
    [InlineData(950, "950")]
    [InlineData(61_200, "61.2k")]
    [InlineData(30_400_000, "30.4M")]
    public void FormatNumber_UsesCompactUnits(double value, string expected)
    {
        Assert.Equal(expected, DpsMeterWindow.FormatNumber(value));
    }
}
