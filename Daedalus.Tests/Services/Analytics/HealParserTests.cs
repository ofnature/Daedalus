using Daedalus.Services.Analytics;
using Xunit;

namespace Daedalus.Tests.Services.Analytics;

/// <summary>
/// H1 of the heal parser: direct heals, HoT ticks and the overheal split, accumulated on the
/// same encounter object as damage (one fight, two views).
///
/// <para>
/// The load-bearing invariant is that healing and damage never leak into each other. HoT ticks
/// arrive on ActorControl category 1540, one number away from the DoT channel at 1541 — routing
/// one into the other would silently inflate every healer's damage row, which is exactly why
/// 1540 was left unconsumed until this feature existed.
/// </para>
/// </summary>
public class HealParserTests
{
    private static CombatantIdentity Healer(uint id = 1, string name = "Asclepia Morn")
        => new(id, CombatantKind.Player, name, "SGE");

    private static CombatantIdentity Dps(uint id = 2, string name = "Nikephoros Astra")
        => new(id, CombatantKind.Player, name, "SAM");

    private static DpsEncounter Fight(float seconds = 100f)
        => new() { DurationSeconds = seconds };

    // ── the overheal split ──────────────────────────────────────────────────────────────

    [Fact]
    public void Effective_healing_excludes_overheal()
    {
        var f = Fight();
        f.AddHeal(Healer(), amount: 10_000, overheal: 4_000, isCrit: false);

        var stats = Assert.Single(f.GetRankedByHealing());
        Assert.Equal(10_000, stats.TotalHealing);
        Assert.Equal(4_000, stats.Overheal);
        Assert.Equal(6_000, stats.EffectiveHealing);
        Assert.Equal(0.4f, stats.OverhealPercent, 3);
    }

    [Fact]
    public void Ranking_uses_effective_healing_not_raw()
    {
        // The whole reason overheal is tracked: a big wasteful heal must not outrank a smaller
        // efficient one. 20k at 80% waste is 4k effective; 6k at zero waste beats it.
        var f = Fight();
        f.AddHeal(Healer(1, "Wasteful"), amount: 20_000, overheal: 16_000, isCrit: false);
        f.AddHeal(Healer(2, "Efficient"), amount: 6_000, overheal: 0, isCrit: false);

        var ranked = f.GetRankedByHealing();
        Assert.Equal("Efficient", ranked[0].Name);
        Assert.Equal("Wasteful", ranked[1].Name);
    }

    [Fact]
    public void Overheal_can_never_exceed_the_heal_itself()
    {
        // Shadow HP is a prediction; a stale read could report more waste than heal, which
        // would make effective healing negative and invert the ranking.
        var f = Fight();
        f.AddHeal(Healer(), amount: 5_000, overheal: 9_999, isCrit: false);

        var stats = Assert.Single(f.GetRankedByHealing());
        Assert.Equal(5_000, stats.Overheal);
        Assert.Equal(0, stats.EffectiveHealing);
        Assert.True(stats.EffectiveHealing >= 0);
    }

    // ── HoT ticks ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void Hot_ticks_count_toward_healing_but_not_toward_heal_casts()
    {
        // Ticks carry no crit flag, so folding them into HealCount would dilute the crit rate —
        // the same reasoning that keeps DoT ticks out of HitCount.
        var f = Fight();
        f.AddHeal(Healer(), amount: 4_000, overheal: 0, isCrit: true);
        f.AddHotTick(Healer(), amount: 1_500);
        f.AddHotTick(Healer(), amount: 1_500);

        var stats = Assert.Single(f.GetRankedByHealing());
        Assert.Equal(7_000, stats.TotalHealing);
        Assert.Equal(3_000, stats.HotHealing);
        Assert.Equal(1, stats.HealCount);
        Assert.Equal(100f, stats.HealCritPercent);
    }

    [Fact]
    public void Hot_ticks_never_touch_damage()
    {
        // Category 1540 is one digit from the DoT channel. If it were ever routed to the damage
        // path, healers would silently gain DPS.
        var f = Fight();
        f.AddHotTick(Healer(), amount: 50_000);

        Assert.Equal(0, f.TotalDamage);
        Assert.Equal(0, Assert.Single(f.GetRankedByHealing()).TotalDamage);
    }

    [Fact]
    public void Damage_never_touches_healing()
    {
        var f = Fight();
        f.AddDamage(Dps(), "Boss", amount: 50_000, isCrit: false, isDirectHit: false);

        Assert.Equal(0, f.TotalHealing);
        Assert.Empty(f.GetRankedByHealing());
    }

    // ── one fight, two views ────────────────────────────────────────────────────────────

    [Fact]
    public void A_combatant_who_both_damages_and_heals_is_one_row_in_each_tab()
    {
        // Every tank and most DPS self-heal. They must not become two identities.
        var f = Fight();
        f.AddDamage(Dps(), "Boss", amount: 100_000, isCrit: false, isDirectHit: false);
        f.AddHeal(Dps(), amount: 8_000, overheal: 1_000, isCrit: false);

        var byDamage = Assert.Single(f.GetRanked());
        var byHealing = Assert.Single(f.GetRankedByHealing());
        Assert.Equal(byDamage.EntityId, byHealing.EntityId);
        Assert.Equal(100_000, byDamage.TotalDamage);
        Assert.Equal(7_000, byHealing.EffectiveHealing);
    }

    [Fact]
    public void Healing_a_party_member_never_renames_the_fight()
    {
        // The encounter is titled by what was damaged. AddHeal takes no target name for exactly
        // this reason — healing the tank must not retitle the fight to the tank.
        var f = Fight();
        f.AddDamage(Dps(), "Honey B. Lovely", amount: 10_000, isCrit: false, isDirectHit: false);
        f.AddHeal(Healer(), amount: 9_000, overheal: 0, isCrit: false);

        Assert.Equal("Honey B. Lovely", f.TargetName);
    }

    [Fact]
    public void Only_combatants_that_healed_appear_on_the_healing_tab()
    {
        var f = Fight();
        f.AddDamage(Dps(), "Boss", amount: 10_000, isCrit: false, isDirectHit: false);
        f.AddHeal(Healer(), amount: 5_000, overheal: 0, isCrit: false);

        Assert.Equal(2, f.GetRanked().Count);
        Assert.Equal("Asclepia Morn", Assert.Single(f.GetRankedByHealing()).Name);
    }

    // ── rates and shares ────────────────────────────────────────────────────────────────

    [Fact]
    public void Effective_and_raw_hps_differ_by_exactly_the_overheal()
    {
        var f = Fight(seconds: 100f);
        f.AddHeal(Healer(), amount: 10_000, overheal: 4_000, isCrit: false);

        var stats = Assert.Single(f.GetRankedByHealing());
        Assert.Equal(60f, f.GetHps(stats), 2);
        Assert.Equal(100f, f.GetRawHps(stats), 2);
    }

    [Fact]
    public void Healing_shares_sum_to_one()
    {
        var f = Fight();
        f.AddHeal(Healer(1, "A"), amount: 6_000, overheal: 0, isCrit: false);
        f.AddHeal(Healer(2, "B"), amount: 4_000, overheal: 0, isCrit: false);

        var total = 0f;
        foreach (var stats in f.GetRankedByHealing())
            total += f.GetHealingShare(stats);

        Assert.Equal(1f, total, 3);
    }

    [Fact]
    public void An_encounter_with_no_healing_reports_zero_rather_than_dividing_by_zero()
    {
        var f = Fight();
        f.AddDamage(Dps(), "Boss", amount: 1, isCrit: false, isDirectHit: false);

        Assert.Equal(0, f.TotalHealing);
        Assert.Equal(0f, f.GetPartyHps());
        Assert.Equal(0f, f.GetHealingShare(f.GetRanked()[0]));
    }

    [Fact]
    public void A_finished_encounter_stops_accepting_healing()
    {
        var f = Fight();
        f.AddHeal(Healer(), amount: 5_000, overheal: 0, isCrit: false);
        f.IsActive = false;
        f.AddHeal(Healer(), amount: 5_000, overheal: 0, isCrit: false);
        f.AddHotTick(Healer(), amount: 5_000);

        Assert.Equal(5_000, f.TotalHealing);
    }

    [Fact]
    public void Non_positive_amounts_are_ignored()
    {
        var f = Fight();
        f.AddHeal(Healer(), amount: 0, overheal: 0, isCrit: false);
        f.AddHeal(Healer(), amount: -100, overheal: 0, isCrit: false);
        f.AddHotTick(Healer(), amount: 0);

        Assert.Empty(f.GetRankedByHealing());
        Assert.Equal(0, f.TotalHealing);
    }
}
