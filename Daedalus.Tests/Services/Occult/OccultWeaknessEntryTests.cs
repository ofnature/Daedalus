using Daedalus.Services.Occult;
using Xunit;

namespace Daedalus.Tests.Services.Occult;

/// <summary>
/// The boss-or-trash verdict (2026-07-31: "x mob is weak to y element" + "need to find out
/// what is a boss or trash enemy"). Both inputs are FACTS read from the game — the largest
/// max-HP observed, and whether a critical encounter was running (dynamic-event container).
/// The line itself is the ZONE'S OWN median enemy HP × a multiple once there are samples, so
/// no magic threshold survives contact with real data; the absolute value is only a bootstrap
/// for an unseen zone.
/// </summary>
public class OccultWeaknessClassificationTests
{
    private const uint Fallback = ElementalWeaknessLog.BossHpThresholdFallback;
    private const int Enough = ElementalWeaknessLog.MinZoneSamplesForRelative;

    [Fact]
    public void FewSamples_FallsBackToTheAbsoluteLine()
    {
        // A zone we've barely seen can't vote on its own distribution yet.
        Assert.Equal(OccultEnemyKind.Trash,
            ElementalWeaknessLog.Classify(Fallback - 1, seenInCriticalEncounter: true, zoneMedianHp: 50_000, zoneSamples: 1));
        Assert.Equal(OccultEnemyKind.CriticalEncounterBoss,
            ElementalWeaknessLog.Classify(Fallback, seenInCriticalEncounter: true, zoneMedianHp: 50_000, zoneSamples: 1));
    }

    [Fact]
    public void WithSamples_TheZonesOwnMedianSetsTheLine()
    {
        // Median trash 40k → the line is 400k, so a 500k mob is a boss even though it never
        // reaches the 1M bootstrap number. This is the whole point of self-calibrating.
        Assert.Equal(OccultEnemyKind.CriticalEncounterBoss,
            ElementalWeaknessLog.Classify(500_000, seenInCriticalEncounter: true, zoneMedianHp: 40_000, zoneSamples: Enough));
        Assert.Equal(OccultEnemyKind.Trash,
            ElementalWeaknessLog.Classify(300_000, seenInCriticalEncounter: true, zoneMedianHp: 40_000, zoneSamples: Enough));
    }

    [Fact]
    public void HighMedianZone_DoesNotPromoteEveryMobToBoss()
    {
        // A zone where everything is chunky: median 2M → the line is 20M, so a 3M mob is
        // still trash there. The absolute 1M bootstrap would have called it a boss.
        Assert.Equal(OccultEnemyKind.Trash,
            ElementalWeaknessLog.Classify(3_000_000, seenInCriticalEncounter: false, zoneMedianHp: 2_000_000, zoneSamples: Enough));
    }

    [Fact]
    public void CriticalEncounterFlag_SeparatesBossFromElite()
    {
        Assert.Equal(OccultEnemyKind.CriticalEncounterBoss,
            ElementalWeaknessLog.Classify(5_000_000, seenInCriticalEncounter: true, zoneMedianHp: 40_000, zoneSamples: Enough));
        Assert.Equal(OccultEnemyKind.Elite,
            ElementalWeaknessLog.Classify(5_000_000, seenInCriticalEncounter: false, zoneMedianHp: 40_000, zoneSamples: Enough));
    }

    [Fact]
    public void CriticalEncounterAdds_StayTrash()
    {
        // Adds spawn alongside the boss and carry the CE flag — HP is what tells them apart.
        Assert.Equal(OccultEnemyKind.Trash,
            ElementalWeaknessLog.Classify(60_000, seenInCriticalEncounter: true, zoneMedianHp: 40_000, zoneSamples: Enough));
    }

    [Fact]
    public void ZeroMedian_NeverDividesTheZoneByNothing()
    {
        Assert.Equal(OccultEnemyKind.Trash,
            ElementalWeaknessLog.Classify(500_000, seenInCriticalEncounter: false, zoneMedianHp: 0, zoneSamples: Enough));
    }

    [Fact]
    public void RealFieldScale_DarkArtistryBoss_ClassifiesFromTrashSizedMedian()
    {
        // Field 2026-07-31: CEs are unsynced (sized for 72 players), so the Dark Artistry
        // boss — the Necromancer soul-stone drop — carries ~450M HP. Against a trash median
        // of 120k the line is 1.2M, and the boss clears it by ~375x.
        Assert.Equal(OccultEnemyKind.CriticalEncounterBoss,
            ElementalWeaknessLog.Classify(450_000_000, seenInCriticalEncounter: true, zoneMedianHp: 120_000, zoneSamples: Enough));

        // And the same zone's ordinary trash stays trash — the old 1M bootstrap would have
        // promoted a 2M North Horn field mob to "elite" purely for being a level-100 mob.
        Assert.Equal(OccultEnemyKind.Trash,
            ElementalWeaknessLog.Classify(1_100_000, seenInCriticalEncounter: false, zoneMedianHp: 120_000, zoneSamples: Enough));
    }

    [Fact]
    public void Elements_AreFlags_SoAMobCanCarryMoreThanOne()
    {
        var e = new OccultWeaknessEntry { Elements = OccultElement.Ice };
        e.Elements |= OccultElement.Wind;

        Assert.True((e.Elements & OccultElement.Ice) != 0);
        Assert.True((e.Elements & OccultElement.Wind) != 0);
        Assert.False((e.Elements & OccultElement.Fire) != 0);
    }
}
