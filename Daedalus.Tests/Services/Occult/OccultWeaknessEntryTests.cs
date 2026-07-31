using Daedalus.Services.Occult;
using Xunit;

namespace Daedalus.Tests.Services.Occult;

/// <summary>
/// The boss-or-trash verdict (2026-07-31 user ask: "x mob is weak to y element" plus "what is
/// a boss or trash enemy"). Both inputs are FACTS read from the game — the largest max-HP ever
/// observed, and whether a critical encounter was running at the time (dynamic-event
/// container) — so the classification stays re-tunable from the persisted table.
/// </summary>
public class OccultWeaknessEntryTests
{
    private static OccultWeaknessEntry Entry(uint maxHp, bool inCe) => new()
    {
        NameId = 1234,
        Name = "Mistwake Something",
        MaxHp = maxHp,
        SeenInCriticalEncounter = inCe,
        Elements = OccultElement.Ice,
    };

    [Fact]
    public void SmallHp_IsTrash_EvenIfSeenDuringACriticalEncounter()
    {
        // CE adds spawn alongside the boss — HP is what separates them.
        Assert.Equal(OccultEnemyKind.Trash, Entry(80_000, inCe: true).Kind);
        Assert.Equal(OccultEnemyKind.Trash, Entry(80_000, inCe: false).Kind);
    }

    [Fact]
    public void BigHp_OutsideACriticalEncounter_IsElite()
    {
        Assert.Equal(OccultEnemyKind.Elite, Entry(ElementalWeaknessLog.BossHpThreshold, inCe: false).Kind);
    }

    [Fact]
    public void BigHp_DuringACriticalEncounter_IsTheCriticalEncounterBoss()
    {
        Assert.Equal(
            OccultEnemyKind.CriticalEncounterBoss,
            Entry(ElementalWeaknessLog.BossHpThreshold * 4, inCe: true).Kind);
    }

    [Fact]
    public void ThresholdIsInclusive_AtTheBoundary()
    {
        Assert.Equal(OccultEnemyKind.Trash, Entry(ElementalWeaknessLog.BossHpThreshold - 1, inCe: true).Kind);
        Assert.Equal(OccultEnemyKind.CriticalEncounterBoss, Entry(ElementalWeaknessLog.BossHpThreshold, inCe: true).Kind);
    }

    [Fact]
    public void Elements_AreFlags_SoAMobCanCarryMoreThanOne()
    {
        var e = Entry(1, inCe: false);
        e.Elements |= OccultElement.Wind;

        Assert.True((e.Elements & OccultElement.Ice) != 0);
        Assert.True((e.Elements & OccultElement.Wind) != 0);
        Assert.False((e.Elements & OccultElement.Fire) != 0);
    }
}
