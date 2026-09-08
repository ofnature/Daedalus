using System;
using Daedalus.Services.Occult;
using Xunit;

namespace Daedalus.Tests.Services.Occult;

/// <summary>
/// The Forbidden Folios Pages: entities that exist to cast an action, targetable and
/// attackable-flagged and immune to damage. Both of the table's "is this a real enemy" tests
/// say yes and both are wrong, which is how they sat on the chase list across three runs of the
/// encounter while the user cast Libra on them by hand.
/// </summary>
public sealed class InvulnerableMechanicTests
{
    private static OccultWeaknessEntry Page(uint nameId, OccultElement elements = OccultElement.None) => new()
    {
        NameId = nameId,
        Name = "Page 16",
        TerritoryId = 1346,
        CriticalEncounter = "Forbidden Folios",
        MaxHp = 74_755_100,
        // The trap: the game reports these as both targetable and attackable.
        EverTargetable = true,
        EverAttackable = true,
        Elements = elements,
        Sightings = 317,
        LastSeenUtc = new DateTime(2026, 9, 8, 1, 9, 10, DateTimeKind.Utc).ToString("O"),
    };

    [Theory]
    [InlineData(14521u)]  // Page 16
    [InlineData(14522u)]  // Page 8
    [InlineData(14528u)]  // Page 512
    [InlineData(3915u)]   // Page 64
    public void AllFourPages_AreDropped(uint nameId)
        => Assert.False(ElementalWeaknessLog.IsWorthKeeping(Page(nameId)));

    /// <summary>
    /// The existing untargetable rule cannot do this job — it requires never-targetable, and
    /// these are targetable. Pinned so nobody "simplifies" the id list away in favour of it.
    /// </summary>
    [Fact]
    public void TheUntargetableRule_DoesNotCatchThem()
        => Assert.False(ElementalWeaknessLog.IsMechanicObject(Page(14521)));

    /// <summary>
    /// Self-correcting, and it must stay that way: a revealed element beats the hardcoded list,
    /// so if one of these ever does carry a weakness the row is kept and the list is wrong, not
    /// the data.
    /// </summary>
    [Fact]
    public void ARevealedElement_OverridesTheList()
        => Assert.True(ElementalWeaknessLog.IsWorthKeeping(Page(14521, OccultElement.Wind)));

    /// <summary>A real Forbidden Folios add in the same encounter must survive.</summary>
    [Fact]
    public void RealAddsInTheSameEncounter_AreKept()
    {
        var rothound = new OccultWeaknessEntry
        {
            NameId = 14915,
            Name = "Crescent Rothound",
            TerritoryId = 1346,
            CriticalEncounter = "Forbidden Folios",
            MaxHp = 772_030,
            EverTargetable = true,
            EverAttackable = true,
            Elements = OccultElement.Fire,
            Sightings = 3214,
            LastSeenUtc = new DateTime(2026, 9, 8, 1, 9, 10, DateTimeKind.Utc).ToString("O"),
        };

        Assert.True(ElementalWeaknessLog.IsWorthKeeping(rothound));
    }

    /// <summary>
    /// The list is deliberately tiny. A large one would mean we had started guessing rather than
    /// confirming, which is the failure the evidence-based rule exists to avoid.
    /// </summary>
    [Fact]
    public void TheListStaysShortAndConfirmed()
    {
        Assert.Equal(4, ElementalWeaknessLog.InvulnerableNameIds.Count);
        foreach (var id in ElementalWeaknessLog.InvulnerableNameIds)
            Assert.DoesNotContain(id, ElementalWeaknessLog.NonCombatNameIds);
    }
}
