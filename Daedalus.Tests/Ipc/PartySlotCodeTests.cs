using System.Collections.Generic;
using System.Linq;
using Daedalus.Data;
using Daedalus.Ipc;
using Xunit;

namespace Daedalus.Tests.Ipc;

/// <summary>
/// The eight standard party slots, derived so nobody has to tick them on eight boxes.
/// <para>
/// Minerva kept BossMod's CONSUMING half of party roles and dropped the producing half — its
/// PartyRolesConfig is an enum, a dictionary and an indexer, with no auto-assign and no UI — so
/// without this every member reads Unassigned and role mechanics (tower soaks, tethers) never
/// resolve. The ordering deliberately mirrors BossMod's own AutoAssignRoles so a fleet that later
/// runs a real BossMod gets the same answer rather than a competing one.
/// </para>
/// </summary>
public sealed class PartySlotCodeTests
{
    private static ulong _cid;

    /// <summary>Content ids ascend in listed order, so listed order IS the canonical order.</summary>
    private static Dictionary<string, string> Assign(params (string Sender, string Role, uint Job)[] party)
        => PartySlotCode.Assign(party.Select(p => (p.Sender, p.Role, p.Job, ++_cid)));

    [Fact]
    public void AFullPartyOfEight_FillsEverySlot()
    {
        var codes = Assign(
            ("a@W", "Tank", JobRegistry.Warrior),
            ("b@W", "Tank", JobRegistry.DarkKnight),
            ("c@W", "Healer", JobRegistry.WhiteMage),
            ("d@W", "Healer", JobRegistry.Sage),
            ("e@W", "DPS", JobRegistry.Samurai),
            ("f@W", "DPS", JobRegistry.Ninja),
            ("g@W", "DPS", JobRegistry.Machinist),
            ("h@W", "DPS", JobRegistry.BlackMage));

        Assert.Equal(new[] { "MT", "OT", "H1", "H2", "M1", "M2", "R1", "R2" }.OrderBy(x => x),
            codes.Values.Where(v => v.Length > 0).OrderBy(x => x));
        Assert.Equal("MT", codes["a@W"]);
        Assert.Equal("H1", codes["c@W"]);
        Assert.Equal("R1", codes["g@W"]);
    }

    /// <summary>
    /// Tank order follows BossMod's MainTankPriority (WAR before PLD), not arrival order. A naive
    /// "first tank wins" would disagree with BossMod on every two-tank party.
    /// </summary>
    [Fact]
    public void MainTank_FollowsJobPriority_NotArrivalOrder()
    {
        var codes = Assign(
            ("pld@W", "Tank", JobRegistry.Paladin),
            ("war@W", "Tank", JobRegistry.Warrior));

        Assert.Equal("MT", codes["war@W"]);
        Assert.Equal("OT", codes["pld@W"]);
    }

    /// <summary>H1 is WHM &gt; AST &gt; SCH &gt; SGE, as BossMod sorts healers.</summary>
    [Fact]
    public void HealerOrder_FollowsJobPriority()
    {
        var codes = Assign(
            ("sge@W", "Healer", JobRegistry.Sage),
            ("whm@W", "Healer", JobRegistry.WhiteMage));

        Assert.Equal("H1", codes["whm@W"]);
        Assert.Equal("H2", codes["sge@W"]);
    }

    /// <summary>R1 is MCH &gt; BRD &gt; DNC &gt; casters.</summary>
    [Fact]
    public void RangedOrder_FollowsJobPriority()
    {
        var codes = Assign(
            ("blm@W", "DPS", JobRegistry.BlackMage),
            ("mch@W", "DPS", JobRegistry.Machinist));

        Assert.Equal("R1", codes["mch@W"]);
        Assert.Equal("R2", codes["blm@W"]);
    }

    /// <summary>
    /// Three ranged and one melee cannot fit the two-and-two shape, so BossMod promotes the
    /// lowest-priority ranged into a melee slot. Without it M2 sits empty and a caster goes
    /// unassigned — which is the state this whole thing exists to end.
    /// </summary>
    [Fact]
    public void ThreeRanged_PromoteTheLowestPriorityIntoMelee()
    {
        var codes = Assign(
            ("sam@W", "DPS", JobRegistry.Samurai),
            ("mch@W", "DPS", JobRegistry.Machinist),
            ("blm@W", "DPS", JobRegistry.BlackMage),
            ("blu@W", "DPS", JobRegistry.BlueMage));

        Assert.Equal("M1", codes["sam@W"]);
        Assert.Equal("M2", codes["blu@W"]);   // lowest ranged priority, promoted
        Assert.Equal("R1", codes["mch@W"]);
        Assert.Equal("R2", codes["blm@W"]);
    }

    /// <summary>
    /// A third tank cannot be expressed by the eight codes, so it is left unassigned rather than
    /// invented — the consumer defaults to Unassigned anyway, and a made-up slot gets acted on.
    /// </summary>
    [Fact]
    public void AThirdOfAnyFamily_IsLeftUnassigned()
    {
        var codes = Assign(
            ("a@W", "Tank", JobRegistry.Warrior),
            ("b@W", "Tank", JobRegistry.Paladin),
            ("c@W", "Tank", JobRegistry.Gunbreaker));

        Assert.Equal("", codes["c@W"]);
        Assert.Equal(2, codes.Values.Count(v => v.Length > 0));
    }

    /// <summary>An unrecognised job is neither melee nor ranged, so it gets no DPS slot.</summary>
    [Fact]
    public void AnUnknownJob_GetsNoSlot()
    {
        var codes = PartySlotCode.Assign(new[] { ("a@W", "DPS", 0u, 1ul) });
        Assert.False(codes.TryGetValue("a@W", out var v) && v.Length > 0);
    }

    /// <summary>
    /// Determinism across boxes: the same party ordered differently by observation must still
    /// produce identical codes, because assignment is computed locally on every machine rather
    /// than broadcast. Two boxes disagreeing puts two toons on one tower.
    /// </summary>
    [Fact]
    public void ObservationOrder_DoesNotChangeTheAnswer()
    {
        (string, string, uint, ulong)[] party =
        [
            ("a@W", "Tank", JobRegistry.Warrior, 10ul),
            ("b@W", "Healer", JobRegistry.WhiteMage, 20ul),
            ("c@W", "DPS", JobRegistry.Samurai, 30ul),
            ("d@W", "DPS", JobRegistry.Machinist, 40ul),
        ];

        var forward = PartySlotCode.Assign(party);
        var reversed = PartySlotCode.Assign(party.Reverse());

        Assert.Equal(forward.OrderBy(kv => kv.Key), reversed.OrderBy(kv => kv.Key));
    }
}
