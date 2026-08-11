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
/// <summary>
/// Table hygiene (2026-08-10, cleaning up South Horn): the object table reports some things as
/// hostile NPCs that are not enemies, and they were sitting in the trash bucket skewing it from
/// both ends — the Striking Dummy at 4.7M against a ~700k zone median, the traps and treasure
/// bunny at 188k. The filter runs on LOAD as well as on record, because the log rewrites the
/// local file wholesale and would otherwise restore the junk after every scrub.
/// </summary>
public class OccultWeaknessHygieneTests
{
    private static OccultWeaknessEntry Entry(
        uint nameId = 13884, string name = "Crescent Garula", uint maxHp = 634_571,
        OccultElement elements = OccultElement.None) => new()
    {
        NameId = nameId,
        Name = name,
        TerritoryId = 1252,
        MaxHp = maxHp,
        Elements = elements,
    };

    [Fact]
    public void RealTrashMob_IsKept()
    {
        Assert.True(ElementalWeaknessLog.IsWorthKeeping(Entry()));
    }

    [Theory]
    [InlineData(541u, "Striking Dummy")]
    [InlineData(7248u, "Happy Bunny")]
    [InlineData(7958u, "Hidden Trap")]
    [InlineData(13967u, "Trap")]
    public void NonCombatObjects_AreDropped(uint nameId, string name)
    {
        Assert.True(ElementalWeaknessLog.NonCombatNameIds.Contains(nameId));
        Assert.False(ElementalWeaknessLog.IsWorthKeeping(Entry(nameId, name, maxHp: 4_716_915)));
    }

    /// <summary>
    /// The Striking Dummy is the one that actually distorted the maths: every boss/trash call
    /// here is RELATIVE to the zone median, so a 4.7M non-enemy in the trash bucket drags the
    /// line it is measured against.
    /// </summary>
    [Fact]
    public void StrikingDummy_IsDroppedEvenThoughItsHpLooksLikeAnEncounter()
    {
        var dummy = Entry(541u, "Striking Dummy", maxHp: 4_716_915);

        Assert.Equal(OccultEnemyKind.Trash,
            ElementalWeaknessLog.Classify(dummy.MaxHp, seenInCriticalEncounter: false, zoneMedianHp: 704_242, zoneSamples: 40));
        Assert.False(ElementalWeaknessLog.IsWorthKeeping(dummy));
    }

    [Fact]
    public void NamelessOrUnmeasuredRows_AreDropped()
    {
        Assert.False(ElementalWeaknessLog.IsWorthKeeping(Entry(name: "", maxHp: 7_475_510)));
        Assert.False(ElementalWeaknessLog.IsWorthKeeping(Entry(name: "Hinkypunk", maxHp: 0)));
        Assert.False(ElementalWeaknessLog.IsWorthKeeping(Entry(maxHp: ElementalWeaknessLog.MinCredibleMaxHp - 1)));
        Assert.False(ElementalWeaknessLog.IsWorthKeeping(Entry(nameId: 0)));
    }

    /// <summary>
    /// Elements only appear when something reveals them (Occult Libra), so a learned element is
    /// the expensive part of this table and outranks every data-quality complaint.
    /// </summary>
    [Fact]
    public void ALearnedElement_SurvivesEveryQualityComplaint()
    {
        Assert.True(ElementalWeaknessLog.IsWorthKeeping(Entry(name: "", maxHp: 0, elements: OccultElement.Wind)));
        Assert.True(ElementalWeaknessLog.IsWorthKeeping(Entry(maxHp: 5, elements: OccultElement.Fire)));

        // ...but a non-enemy is still a non-enemy. A dummy cannot have a real weakness.
        Assert.False(ElementalWeaknessLog.IsWorthKeeping(
            Entry(541u, "Striking Dummy", maxHp: 4_716_915, elements: OccultElement.Ice)));
    }

    [Fact]
    public void ShippedSeed_CarriesNoJunk()
    {
        // Guards the scrub: if a future rebake re-imports a dummy or a nameless row, this fails.
        var seed = LoadShippedSeed();

        Assert.NotEmpty(seed);
        Assert.All(seed, e => Assert.True(
            ElementalWeaknessLog.IsWorthKeeping(e),
            $"shipped seed row '{e.Name}' (NameId {e.NameId}, {e.MaxHp:N0} HP) would be filtered on load"));
    }

    private static System.Collections.Generic.List<OccultWeaknessEntry> LoadShippedSeed()
    {
        var asm = typeof(ElementalWeaknessLog).Assembly;
        var resource = System.Linq.Enumerable.FirstOrDefault(
            asm.GetManifestResourceNames(),
            n => n.EndsWith("OccultWeaknessSeed.json", System.StringComparison.Ordinal));
        Assert.NotNull(resource);

        using var stream = asm.GetManifestResourceStream(resource!);
        Assert.NotNull(stream);
        using var reader = new System.IO.StreamReader(stream!);
        return System.Text.Json.JsonSerializer
            .Deserialize<System.Collections.Generic.List<OccultWeaknessEntry>>(reader.ReadToEnd())!;
    }
}

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

    /// <summary>
    /// The zone uses two different words and they are not interchangeable: a critical encounter
    /// has a BOSS, a FATE has an ELITE. A big-HP enemy with neither stamp is a field notorious.
    /// </summary>
    [Fact]
    public void EncounterType_DecidesWhetherItIsABossOrAnElite()
    {
        Assert.Equal(OccultEnemyKind.CriticalEncounterBoss,
            ElementalWeaknessLog.Classify(5_000_000, seenInCriticalEncounter: true, zoneMedianHp: 40_000, zoneSamples: Enough));

        Assert.Equal(OccultEnemyKind.FateElite,
            ElementalWeaknessLog.Classify(5_000_000, seenInCriticalEncounter: false, zoneMedianHp: 40_000, zoneSamples: Enough,
                seenInFate: true));

        Assert.Equal(OccultEnemyKind.FieldNotorious,
            ElementalWeaknessLog.Classify(5_000_000, seenInCriticalEncounter: false, zoneMedianHp: 40_000, zoneSamples: Enough));

        // A CE stamp outranks a FATE stamp — an enemy caught in both is reported as the boss.
        Assert.Equal(OccultEnemyKind.CriticalEncounterBoss,
            ElementalWeaknessLog.Classify(5_000_000, seenInCriticalEncounter: true, zoneMedianHp: 40_000, zoneSamples: Enough,
                seenInFate: true));
    }

    /// <summary>
    /// Mechanic objects can carry encounter-sized HP — the Forbidden Folios Pages are 74M each —
    /// so the untargetable verdict has to be checked before the HP line, or they file as bosses.
    /// </summary>
    /// <summary>
    /// Field 2026-08-11: Nammu, the ELITE of Rough Waters, is 152,523 HP — a fifth of ordinary
    /// South Horn trash — and the HP line filed it as trash. An encounter's named target earns
    /// its title from being the target, not from the size of its pool.
    /// </summary>
    [Fact]
    public void TinyFateElite_IsStillAnElite()
    {
        Assert.Equal(OccultEnemyKind.FateElite,
            ElementalWeaknessLog.Classify(
                152_523, seenInCriticalEncounter: false, zoneMedianHp: 704_242, zoneSamples: Enough,
                seenInFate: true, isEncounterTopMember: true));
    }

    /// <summary>
    /// The target rule is PROMOTE-ONLY. A fat encounter add still classifies on HP, so a 9M
    /// add in a zone whose median is 844k still reads as a boss. Arguably wrong, deliberately
    /// left alone: demoting non-target members would reclassify a great many rows on a rule
    /// nobody has checked in the field. Documented here so the behaviour is a choice, not a gap.
    /// </summary>
    [Fact]
    public void FatEncounterAdd_StillClassifiesOnHp_KnownLimitation()
    {
        Assert.Equal(OccultEnemyKind.CriticalEncounterBoss,
            ElementalWeaknessLog.Classify(
                9_170_210, seenInCriticalEncounter: true, zoneMedianHp: 843_584, zoneSamples: Enough,
                isEncounterTopMember: false));
    }

    [Fact]
    public void EncounterTarget_TakesItsWordFromTheEncounterType()
    {
        Assert.Equal(OccultEnemyKind.CriticalEncounterBoss,
            ElementalWeaknessLog.Classify(
                90_098_725, seenInCriticalEncounter: true, zoneMedianHp: 704_242, zoneSamples: Enough,
                isEncounterTopMember: true));

        Assert.Equal(OccultEnemyKind.FateElite,
            ElementalWeaknessLog.Classify(
                23_840_286, seenInCriticalEncounter: false, zoneMedianHp: 704_242, zoneSamples: Enough,
                seenInFate: true, isEncounterTopMember: true));
    }

    /// <summary>Enemies with no encounter still fall back to the zone-relative HP line.</summary>
    [Fact]
    public void FieldEnemies_StillUseTheHpLine()
    {
        Assert.Equal(OccultEnemyKind.Trash,
            ElementalWeaknessLog.Classify(700_000, seenInCriticalEncounter: false, zoneMedianHp: 704_242, zoneSamples: Enough));
        Assert.Equal(OccultEnemyKind.FieldNotorious,
            ElementalWeaknessLog.Classify(33_104_646, seenInCriticalEncounter: false, zoneMedianHp: 704_242, zoneSamples: Enough));
    }

    [Fact]
    public void UntargetableMechanicObject_OutranksItsHpPool()
    {
        Assert.Equal(OccultEnemyKind.MechanicObject,
            ElementalWeaknessLog.Classify(74_755_100, seenInCriticalEncounter: true, zoneMedianHp: 840_000, zoneSamples: Enough,
                isMechanicObject: true));
    }

    /// <summary>
    /// Field 2026-08-11: the Persistent Pot is the thing you ESCORT in the pot FATEs. It is
    /// perfectly targetable, so a targetability check waves it through — only attackability
    /// catches it. Both it and an untargetable mechanic are excluded for the same reason:
    /// Occult Libra can never reveal anything on either.
    /// </summary>
    [Fact]
    public void TargetableFriendly_IsNotAnEnemy()
    {
        var pot = new OccultWeaknessEntry
        {
            NameId = 14770, Name = "Persistent Pot", TerritoryId = 1346, MaxHp = 2_649_381,
            Sightings = ElementalWeaknessLog.MinSightingsForTargetabilityVerdict,
            EverTargetable = true,   // you can click it
            EverAttackable = false,  // you cannot hit it
        };

        Assert.True(ElementalWeaknessLog.IsFriendly(pot));
        Assert.False(ElementalWeaknessLog.IsMechanicObject(pot), "targetable, so not a mechanic object");
        Assert.True(ElementalWeaknessLog.IsNotAnEnemy(pot));
    }

    [Fact]
    public void RealEnemy_SeenAttackableOnce_StaysAnEnemyForever()
    {
        var mob = new OccultWeaknessEntry
        {
            NameId = 13884, Name = "Crescent Garula", TerritoryId = 1252, MaxHp = 634_571,
            Sightings = 500, EverTargetable = true, EverAttackable = true,
        };

        Assert.False(ElementalWeaknessLog.IsNotAnEnemy(mob));
        Assert.False(ElementalWeaknessLog.IsFriendly(mob));
    }

    [Fact]
    public void MechanicObjectVerdict_NeedsEvidence_AndYieldsToBothTargetabilityAndAWeakness()
    {
        OccultWeaknessEntry E(int sightings, bool targetable, OccultElement el = OccultElement.None) => new()
        {
            NameId = 1, Name = "Page 512", TerritoryId = 1346, MaxHp = 74_755_100,
            Sightings = sightings, EverTargetable = targetable, Elements = el,
        };
        var enough = ElementalWeaknessLog.MinSightingsForTargetabilityVerdict;

        Assert.True(ElementalWeaknessLog.IsMechanicObject(E(enough, targetable: false)));

        // Seen only in passing — not enough exposure to conclude anything.
        Assert.False(ElementalWeaknessLog.IsMechanicObject(E(enough - 1, targetable: false)));

        // Targetable even once = a real enemy. Bosses go untargetable between phases.
        Assert.False(ElementalWeaknessLog.IsMechanicObject(E(enough, targetable: true)));

        // And a revealed weakness proves it was hit, whatever the flag said.
        Assert.False(ElementalWeaknessLog.IsMechanicObject(E(enough, targetable: false, OccultElement.Ice)));
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
    public void RescaleFraction_TreatsACollapsedPoolAsAPatch_NotAMidFightReading()
    {
        // North Horn CEs launched unsynced (450M / 250M). South Horn was corrected after
        // launch, so the same fix is expected here — a boss that reappears at a fraction of
        // its recorded HP has been rescaled, and the stored maximum must give way.
        const uint prePatch = 450_000_000;
        var postPatch = (uint)(prePatch * ElementalWeaknessLog.RescaleDetectionFraction) - 1;
        Assert.True(postPatch <= prePatch * ElementalWeaknessLog.RescaleDetectionFraction);

        // A normal mid-fight reading (boss at 60% HP) is NOT a rescale — it must not overwrite.
        var midFight = (uint)(prePatch * 0.6f);
        Assert.False(midFight <= prePatch * ElementalWeaknessLog.RescaleDetectionFraction);
    }

    [Fact]
    public void GarbageMaxHp_IsNeverCredible()
    {
        // Field 2026-07-31: the Doubled Trouble CE boss (Conjured Calofisteri) was captured at
        // 44 HP from a transient spawn-time read, and the rescale rule accepted it — wiping a
        // multi-million pool and demoting a boss to "trash".
        Assert.False(ElementalWeaknessLog.IsCredibleMaxHp(44));
        Assert.False(ElementalWeaknessLog.IsCredibleMaxHp(0));
        Assert.True(ElementalWeaknessLog.IsCredibleMaxHp(ElementalWeaknessLog.MinCredibleMaxHp));
        Assert.True(ElementalWeaknessLog.IsCredibleMaxHp(216_049_771));
    }

    [Fact]
    public void GarbageReading_NeverLooksLikeARescale()
    {
        // 216M -> 44 must NOT be treated as a re-sync, however large the drop looks.
        Assert.False(ElementalWeaknessLog.LooksLikeRescale(stored: 216_049_771, observed: 44));
    }

    [Fact]
    public void GenuineCollapse_StillLooksLikeARescale()
    {
        // 450M -> 9M after a sync patch: credible magnitude, real collapse. (It still needs a
        // second agreeing sighting before it is committed — see RescaleConfirmations.)
        Assert.True(ElementalWeaknessLog.LooksLikeRescale(stored: 450_000_000, observed: 9_000_000));
        Assert.True(ElementalWeaknessLog.RescaleConfirmations >= 2);
    }

    [Fact]
    public void MidFightReading_IsNotARescale()
    {
        Assert.False(ElementalWeaknessLog.LooksLikeRescale(stored: 216_000_000, observed: 130_000_000));
    }

    [Fact]
    public void CriticalEncounterGroup_KeepsAddsAndDropsBystanders()
    {
        // Real North Horn tiers (2026-07-31): field mobs 780-850k (the zone median), encounter
        // adds 4-7M, bosses 126-216M. Field trash standing in scan range while a CE ran was
        // being filed under that CE.
        const uint median = 800_000;
        const int enough = ElementalWeaknessLog.MinZoneSamplesForRelative;

        // Adds — belong to the fight.
        Assert.True(ElementalWeaknessLog.IsCriticalEncounterParticipant(true, 4_086_110, median, enough)); // Abductor's Plume
        Assert.True(ElementalWeaknessLog.IsCriticalEncounterParticipant(true, 6_967_100, median, enough)); // Alabaster Golem
        Assert.True(ElementalWeaknessLog.IsCriticalEncounterParticipant(true, 216_049_771, median, enough)); // Alabaster Blade

        // Bystanders — merely visible while the CE ran.
        Assert.False(ElementalWeaknessLog.IsCriticalEncounterParticipant(true, 783_328, median, enough)); // Crescent Wraith
        Assert.False(ElementalWeaknessLog.IsCriticalEncounterParticipant(true, 843_584, median, enough)); // Crescent Arioch
        Assert.False(ElementalWeaknessLog.IsCriticalEncounterParticipant(true, 188_300, median, enough)); // Persistent Pot
    }

    [Fact]
    public void CriticalEncounterGroup_NeverAppliesToMobsNotSeenInOne()
    {
        Assert.False(ElementalWeaknessLog.IsCriticalEncounterParticipant(false, 200_000_000, 800_000, 50));
    }

    [Fact]
    public void CriticalEncounterGroup_ThinZone_KeepsTheRawSighting()
    {
        // Too little data to judge — show what was actually observed rather than hide it.
        Assert.True(ElementalWeaknessLog.IsCriticalEncounterParticipant(true, 783_328, zoneMedianHp: 800_000, zoneSamples: 1));
        Assert.True(ElementalWeaknessLog.IsCriticalEncounterParticipant(true, 783_328, zoneMedianHp: 0, zoneSamples: 99));
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
