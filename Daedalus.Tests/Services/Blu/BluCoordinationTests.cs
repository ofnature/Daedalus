using System.Linq;
using Daedalus.Config.DPS;
using Daedalus.Data;
using Daedalus.Services.Blu;
using Xunit;

namespace Daedalus.Tests.Services.Blu;

/// <summary>
/// BLU v3.1b: capability bitfield + deterministic owner election + coordination snapshot.
/// Everything here is pure — the same inputs must produce the same owners on every machine.
/// </summary>
public class BluCoordinationTests
{
    private static BluPeerCapability Peer(string id, BluCapabilities caps) => new(id, caps);

    // ── Capability bitfield ─────────────────────────────────────────────────

    [Fact]
    public void CapabilityMap_SetsBits_ForUsableSpells()
    {
        var usable = new[]
        {
            BLUActions.SongOfTorment.ActionId,
            BLUActions.MortalFlame.ActionId,
            BLUActions.Gobskin.ActionId,
            BLUActions.UtilityIds.Level5Death,
        };
        var caps = BluCapabilityMap.Compute(id => usable.Contains(id), BluRole.Dps);

        Assert.True(caps.HasFlag(BluCapabilities.SongOfTorment));
        Assert.True(caps.HasFlag(BluCapabilities.MortalFlame));
        Assert.True(caps.HasFlag(BluCapabilities.Gobskin));
        Assert.True(caps.HasFlag(BluCapabilities.Level5Death));
        Assert.False(caps.HasFlag(BluCapabilities.BreathOfMagic));
        Assert.False(caps.HasFlag(BluCapabilities.MoonFlute));
        Assert.False(caps.HasFlag(BluCapabilities.StickyTongue));
    }

    [Fact]
    public void CapabilityMap_TankRole_NeverAdvertisesCactguardOrSting()
    {
        // The tank is Cactguard's TARGET and must never Final Sting (someone holds the boss).
        var caps = BluCapabilityMap.Compute(_ => true, BluRole.Tank);

        Assert.False(caps.HasFlag(BluCapabilities.Cactguard));
        Assert.False(caps.HasFlag(BluCapabilities.FinalSting));
        Assert.True(caps.HasFlag(BluCapabilities.TankRole));
        Assert.False(caps.HasFlag(BluCapabilities.HealerRole));
    }

    [Fact]
    public void CapabilityMap_HealerRole_SetsPreferenceBit()
    {
        var caps = BluCapabilityMap.Compute(_ => true, BluRole.Healer);

        Assert.True(caps.HasFlag(BluCapabilities.HealerRole));
        Assert.True(caps.HasFlag(BluCapabilities.Cactguard)); // healers may Cactguard
    }

    // ── Election determinism ────────────────────────────────────────────────

    [Fact]
    public void Election_FiltersByCapability_SortsBySenderId()
    {
        var roster = new[]
        {
            Peer("Zeta@World", BluCapabilities.MortalFlame),
            Peer("Alpha@World", BluCapabilities.SongOfTorment),
            Peer("Beta@World", BluCapabilities.MortalFlame | BluCapabilities.SongOfTorment),
        };

        // Beta sorts after Alpha but is the FIRST MortalFlame-capable sender ordinally.
        Assert.Equal("Beta@World", BluPartyElection.ElectOwner(roster, BluCapabilities.MortalFlame));
        Assert.Equal("Alpha@World", BluPartyElection.ElectOwner(roster, BluCapabilities.SongOfTorment));
        Assert.Null(BluPartyElection.ElectOwner(roster, BluCapabilities.Avail)); // nobody capable
    }

    [Fact]
    public void Election_MultiOwner_ReturnsFirstN_MayReturnFewer()
    {
        var roster = new[]
        {
            Peer("C@W", BluCapabilities.StickyTongue),
            Peer("A@W", BluCapabilities.StickyTongue),
            Peer("B@W", BluCapabilities.None),
        };

        var two = BluPartyElection.ElectOwners(roster, BluCapabilities.StickyTongue, 2);
        Assert.Equal(new[] { "A@W", "C@W" }, two);

        var three = BluPartyElection.ElectOwners(roster, BluCapabilities.StickyTongue, 3);
        Assert.Equal(2, three.Count); // short — the checklist shows red, nothing blocks
    }

    [Fact]
    public void Election_Gobskin_PrefersHealerRole_FallsBackToAnyCapable()
    {
        var withHealer = new[]
        {
            Peer("A@W", BluCapabilities.Gobskin),
            Peer("Z@W", BluCapabilities.Gobskin | BluCapabilities.HealerRole),
        };
        Assert.Equal("Z@W", BluPartyElection.ElectGobskinOwner(withHealer));

        var noHealer = new[]
        {
            Peer("B@W", BluCapabilities.Gobskin),
            Peer("A@W", BluCapabilities.None),
        };
        Assert.Equal("B@W", BluPartyElection.ElectGobskinOwner(noHealer));
    }

    [Fact]
    public void Election_StaggerGroups_SplitFluteCapableByOrder()
    {
        var roster = new[]
        {
            Peer("D@W", BluCapabilities.MoonFlute),
            Peer("B@W", BluCapabilities.MoonFlute),
            Peer("A@W", BluCapabilities.MoonFlute),
            Peer("C@W", BluCapabilities.None), // no Flute — never grouped
        };

        Assert.Equal('A', BluPartyElection.StaggerGroupFor(roster, "A@W"));
        Assert.Equal('B', BluPartyElection.StaggerGroupFor(roster, "B@W"));
        Assert.Equal('A', BluPartyElection.StaggerGroupFor(roster, "D@W"));
        Assert.Equal('A', BluPartyElection.StaggerGroupFor(roster, "C@W")); // default A
    }

    [Fact]
    public void Election_OperatorPick_OutranksSenderSort_FallsBackWhenIncapable()
    {
        var pick = BluCapabilities.Ultravibration | BluCapabilities.PreferredFreezeShatter;
        var roster = new[]
        {
            Peer("A@W", BluCapabilities.Ultravibration), // would win the plain sort
            Peer("Z@W", pick),                           // the operator's pick
        };
        Assert.Equal("Z@W", BluPartyElection.ElectPreferredOwner(
            roster, BluCapabilities.Ultravibration, BluCapabilities.PreferredFreezeShatter));

        // A pick that ISN'T capable can never be elected — normal election stands.
        var incapablePick = new[]
        {
            Peer("A@W", BluCapabilities.Ultravibration),
            Peer("Z@W", BluCapabilities.PreferredFreezeShatter), // flagged but no Ultravibration
        };
        Assert.Equal("A@W", BluPartyElection.ElectPreferredOwner(
            incapablePick, BluCapabilities.Ultravibration, BluCapabilities.PreferredFreezeShatter));
    }

    [Fact]
    public void Snapshot_FreezeShatterPick_DrivesBothRoles()
    {
        var both = BluCapabilities.RamsVoice | BluCapabilities.Ultravibration;
        var roster = new[]
        {
            Peer("A@W", both),
            Peer("Z@W", both | BluCapabilities.PreferredFreezeShatter),
        };
        var onZ = BluCoordinationCalculator.Compute("Z@W", roster, 0);
        var onA = BluCoordinationCalculator.Compute("A@W", roster, 0);
        Assert.True(onZ.IsFreezeLead);
        Assert.True(onZ.IsShatterOwner);
        Assert.False(onA.IsFreezeLead);
        Assert.False(onA.IsShatterOwner);
    }

    // ── Snapshot computation ────────────────────────────────────────────────

    [Fact]
    public void Snapshot_SingleBlu_SelfOwnsEverything()
    {
        var solo = BluCoordinationCalculator.Compute(
            "Me@W", new[] { Peer("Me@W", BluCapabilities.SongOfTorment) }, territoryId: 0);

        Assert.False(solo.CoordinationActive);
        Assert.True(solo.IsBleedOwner);
        Assert.True(solo.IsMortalFlameOwner);
        Assert.True(solo.IsBreathOfMagicOwner);
        Assert.True(solo.IsGobskinOwner);
        Assert.True(solo.IsCactguardOwner);
        Assert.Equal(0, solo.MoonFluteStaggerDelaySeconds);
    }

    [Fact]
    public void Snapshot_TwoBlu_SplitsOwnership_SameAnswerOnBothMachines()
    {
        var roster = new[]
        {
            Peer("A@W", BluCapabilities.SongOfTorment | BluCapabilities.MortalFlame | BluCapabilities.Gobskin),
            Peer("B@W", BluCapabilities.SongOfTorment | BluCapabilities.BreathOfMagic
                        | BluCapabilities.Gobskin | BluCapabilities.HealerRole | BluCapabilities.Cactguard),
        };

        var onA = BluCoordinationCalculator.Compute("A@W", roster, 0);
        var onB = BluCoordinationCalculator.Compute("B@W", roster, 0);

        Assert.True(onA.CoordinationActive);
        // A is first ordinally → bleed owner; B is the only BoM-capable and the healer-mimic.
        Assert.True(onA.IsBleedOwner);
        Assert.False(onB.IsBleedOwner);
        Assert.True(onA.IsMortalFlameOwner);
        Assert.False(onB.IsMortalFlameOwner);
        Assert.False(onA.IsBreathOfMagicOwner);
        Assert.True(onB.IsBreathOfMagicOwner);
        Assert.False(onA.IsGobskinOwner); // healer-mimic B wins the barrier
        Assert.True(onB.IsGobskinOwner);
        Assert.False(onA.IsCactguardOwner);
        Assert.True(onB.IsCactguardOwner);
    }

    [Fact]
    public void Snapshot_NobodyCapable_RoleStaysUnassigned()
    {
        var roster = new[]
        {
            Peer("A@W", BluCapabilities.SongOfTorment),
            Peer("B@W", BluCapabilities.SongOfTorment),
        };

        var snap = BluCoordinationCalculator.Compute("A@W", roster, 0);
        Assert.False(snap.IsMortalFlameOwner); // nobody slots it — nobody casts it
        Assert.False(snap.IsBreathOfMagicOwner);
    }

    [Fact]
    public void Snapshot_T13GroupB_Gets30sStaggerDelay_OtherDutiesZero()
    {
        var roster = new[]
        {
            Peer("A@W", BluCapabilities.MoonFlute),
            Peer("B@W", BluCapabilities.MoonFlute),
        };

        var t13B = BluCoordinationCalculator.Compute("B@W", roster, BluDutyAssignments.FinalCoilTurn4);
        Assert.Equal('B', t13B.StaggerGroup);
        Assert.Equal(BluCoordinationCalculator.StaggerDelaySeconds, t13B.MoonFluteStaggerDelaySeconds);

        var t13A = BluCoordinationCalculator.Compute("A@W", roster, BluDutyAssignments.FinalCoilTurn4);
        Assert.Equal(0, t13A.MoonFluteStaggerDelaySeconds);

        var t9B = BluCoordinationCalculator.Compute("B@W", roster, BluDutyAssignments.SecondCoilTurn4);
        Assert.Equal(0, t9B.MoonFluteStaggerDelaySeconds); // stagger is T13-only
    }

    // ── Coil duty assignments (v3.5) ────────────────────────────────────────

    [Fact]
    public void DutyAssignments_TerritoryIds_MatchGameSheets()
    {
        // ContentFinderCondition → TerritoryType, XIVAPI-verified 2026-07-15.
        Assert.Equal(245u, BluDutyAssignments.BindingCoilTurn5);
        Assert.Equal(358u, BluDutyAssignments.SecondCoilTurn4);
        Assert.Equal(196u, BluDutyAssignments.FinalCoilTurn4);
        Assert.True(BluDutyAssignments.HasRequirements(BluDutyAssignments.BindingCoilTurn5));
        Assert.False(BluDutyAssignments.HasRequirements(1055)); // random dungeon → no checklist
    }

    [Fact]
    public void DutyAssignments_Evaluate_SatisfiedAndShortSlots()
    {
        var roster = new[]
        {
            Peer("A@W", BluCapabilities.Level5Death | BluCapabilities.Avail),
            Peer("B@W", BluCapabilities.Avail),
        };

        var t13 = BluDutyAssignments.Evaluate(BluDutyAssignments.FinalCoilTurn4, roster);
        var avail = t13.Single(r => r.Requirement.Capability == BluCapabilities.Avail);
        var l5d = t13.Single(r => r.Requirement.Capability == BluCapabilities.Level5Death);

        Assert.True(avail.Satisfied); // needs 2, both capable
        Assert.Equal(new[] { "A@W", "B@W" }, avail.Assigned);
        Assert.False(l5d.Satisfied);  // needs 2, only A capable → red pre-pull warning
        Assert.Equal(new[] { "A@W" }, l5d.Assigned);
    }

    [Fact]
    public void DutyAssignments_UtilityIds_MatchSpellbook()
    {
        var byName = BLUSpellbook.All.ToDictionary(e => e.Name, e => e.ActionId);
        Assert.Equal(byName["Level 5 Death"], BLUActions.UtilityIds.Level5Death);
        Assert.Equal(byName["Sticky Tongue"], BLUActions.UtilityIds.StickyTongue);
        Assert.Equal(byName["Avail"], BLUActions.UtilityIds.Avail);
        Assert.Equal(byName["Cactguard"], BLUActions.Cactguard.ActionId);
    }
}
