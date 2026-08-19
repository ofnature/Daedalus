using System.Collections.Generic;
using Daedalus.Data;
using Daedalus.Ipc;
using Xunit;

namespace Daedalus.Tests.Ipc;

/// <summary>
/// The eight standard party slots, derived for consumers that assign duties per player.
/// Daedalus's own fields cannot serve: Role is coarse ("Tank"/"Healer"/"DPS") and AssignedSlot
/// numbers DPS 1..4 without separating melee from ranged.
/// </summary>
public sealed class PartySlotCodeTests
{
    private static Dictionary<string, string> Assign(params (string, string, uint)[] party)
        => PartySlotCode.Assign(party);

    [Fact]
    public void AFullLightParty_GetsTheStandardEight()
    {
        var codes = Assign(
            ("a@W", "Tank", JobRegistry.Paladin),
            ("b@W", "Tank", JobRegistry.Warrior),
            ("c@W", "Healer", JobRegistry.WhiteMage),
            ("d@W", "Healer", JobRegistry.Sage),
            ("e@W", "DPS", JobRegistry.Samurai),
            ("f@W", "DPS", JobRegistry.Ninja),
            ("g@W", "DPS", JobRegistry.Bard),
            ("h@W", "DPS", JobRegistry.BlackMage));

        Assert.Equal("MT", codes["a@W"]);
        Assert.Equal("OT", codes["b@W"]);
        Assert.Equal("H1", codes["c@W"]);
        Assert.Equal("H2", codes["d@W"]);
        Assert.Equal("M1", codes["e@W"]);
        Assert.Equal("M2", codes["f@W"]);
        Assert.Equal("R1", codes["g@W"]);
        Assert.Equal("R2", codes["h@W"]);
    }

    /// <summary>
    /// The split AssignedSlot cannot express: a caster and a physical ranged are both R, and both
    /// melee are M, regardless of the order they appear in.
    /// </summary>
    [Fact]
    public void CastersAndPhysicalRanged_ShareTheRangedSlots()
    {
        var codes = Assign(
            ("a@W", "DPS", JobRegistry.RedMage),
            ("b@W", "DPS", JobRegistry.Machinist),
            ("c@W", "DPS", JobRegistry.Monk));

        Assert.Equal("R1", codes["a@W"]);
        Assert.Equal("R2", codes["b@W"]);
        Assert.Equal("M1", codes["c@W"]);
    }

    /// <summary>
    /// An unrecognised job is left UNASSIGNED rather than guessed into a side. A wrong slot is
    /// worse than none, because the consumer acts on it — two people soaking one tower is not
    /// recoverable, nobody soaking it is.
    /// </summary>
    [Fact]
    public void AnUnknownJob_IsLeftUnassigned()
    {
        var codes = Assign(("a@W", "DPS", 0u), ("b@W", "DPS", JobRegistry.Carpenter));
        Assert.Equal("", codes["a@W"]);
        Assert.Equal("", codes["b@W"]);
    }

    /// <summary>Only eight slots exist; a ninth body gets nothing rather than a duplicate.</summary>
    [Fact]
    public void OverflowBeyondTheEightSlots_IsUnassigned()
    {
        var codes = Assign(
            ("a@W", "Tank", JobRegistry.Paladin),
            ("b@W", "Tank", JobRegistry.Warrior),
            ("c@W", "Tank", JobRegistry.Gunbreaker),
            ("d@W", "Healer", JobRegistry.WhiteMage),
            ("e@W", "Healer", JobRegistry.Sage),
            ("f@W", "Healer", JobRegistry.Astrologian),
            ("g@W", "DPS", JobRegistry.Samurai),
            ("h@W", "DPS", JobRegistry.Ninja),
            ("i@W", "DPS", JobRegistry.Dragoon));

        Assert.Equal("", codes["c@W"]);
        Assert.Equal("", codes["f@W"]);
        Assert.Equal("", codes["i@W"]);
    }

    /// <summary>
    /// Deterministic: the same party in the same order produces the same codes on every box.
    /// The later duty coordinator depends on this — each machine computes assignments locally
    /// rather than broadcasting them, so identical inputs must give identical answers.
    /// </summary>
    [Fact]
    public void TheSameOrderedParty_AlwaysProducesTheSameCodes()
    {
        (string, string, uint)[] party =
        [
            ("a@W", "Tank", JobRegistry.Paladin),
            ("b@W", "DPS", JobRegistry.Samurai),
            ("c@W", "Healer", JobRegistry.Scholar),
        ];

        Assert.Equal(PartySlotCode.Assign(party), PartySlotCode.Assign(party));
    }

    [Fact]
    public void EmptySenderIds_AreSkipped()
    {
        var codes = Assign(("", "Tank", JobRegistry.Paladin), ("a@W", "Tank", JobRegistry.Warrior));
        Assert.False(codes.ContainsKey(""));
        Assert.Equal("MT", codes["a@W"]);
    }
}
