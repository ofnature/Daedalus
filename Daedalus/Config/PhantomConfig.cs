using System.Collections.Generic;

namespace Daedalus.Config;

/// <summary>
/// One remembered pot FATE spawn. Unix seconds rather than DateTime so the round trip through
/// the config file can't lose the UTC kind.
/// </summary>
public sealed class PotFateSighting
{
    public long LastSeenUnixSeconds { get; set; }

    /// <summary>Cycle measured from two observed spawns, or null while only the default is known.</summary>
    public double? CycleSeconds { get; set; }
}

/// <summary>
/// Occult Crescent phantom action settings (Phase 2 of docs/occult-phantom-plan.md).
/// Consumed by the Phase 3+ PhantomActionLayer; until then only the config UI reads it.
/// Thresholds default to RSR PhantomDefault's field-tested values.
/// </summary>
public sealed class PhantomConfig
{
    /// <summary>Master toggle for the phantom action layer (in-zone only).</summary>
    public bool EnablePhantomActions { get; set; } = true;

    /// <summary>
    /// Pot FATE sighting history, keyed "{territoryId}:{fateName}".
    /// <para>
    /// Persisted deliberately: the countdown is derived from observation, so holding it only in
    /// memory means every plugin reload — including every Debug rebuild — forgets when the last
    /// pot was and the warning stays silent until one actually spawns. Field-reported
    /// 2026-07-31 after two pots were missed that way.
    /// </para>
    /// </summary>
    public Dictionary<string, PotFateSighting> PotFateHistory { get; set; } = [];

    /// <summary>Hold damage phantom actions for the main job's burst window.
    /// Survival/utility actions ignore this.</summary>
    /// <summary>
    /// Hold phantom damage actions for the main job's burst window.
    /// <para>
    /// DEFAULT CHANGED 2026-07-31 after measuring it: phantom nukes hit FAR harder than a main
    /// job GCD, because "potency scales with item level" means their numbers are not
    /// comparable to job potencies at all. Field reading on a fire-weak target: Occult Fire II
    /// landed 75,000-120,000 against a 57,000 maximum from the character's own class skills —
    /// so a phantom cast that displaces a job GCD is a large net GAIN, not the loss the
    /// potency figures suggest. Holding them for burst just leaves that damage unspent.
    /// </para>
    /// </summary>
    public bool SaveDamageForBurst { get; set; } = false;

    /// <summary>Auto-open the compact zone HUD (consumables, currency, shard banner)
    /// when entering Occult Crescent.</summary>
    public bool ShowOccultHud { get; set; } = true;

    // ── Freelancer ──
    public float FreelancerResuscitationHpPct { get; set; } = 0.70f;

    /// <summary>
    /// Raise dead party members with the phantom job's own raise — Chemist's Revive or Phantom
    /// White Mage's Occult Raise.
    /// <para>
    /// Worth having even on a party with a real healer: these are independent of Swiftcast, and
    /// Occult Raise is instant. A healer whose Swiftcast is down may not reach you before the
    /// death timer returns you to base.
    /// </para>
    /// </summary>
    public bool UsePhantomRaise { get; set; } = true;
    public bool UseTreasuresight { get; set; } = false;


    // ── Knight ──
    public bool KnightPrayAsHeal { get; set; } = false;
    public bool KnightPledgeSelf { get; set; } = false;

    // ── Monk ──
    public float MonkKickMaxRangeYalms { get; set; } = 5f;
    public int MonkChakraMpThreshold { get; set; } = 3000;
    public float MonkChakraHpPct { get; set; } = 0.30f;

    // ── Chemist ──
    public bool ChemistPotionSelfOnly { get; set; } = true;
    public float ChemistPotionHpPct { get; set; } = 0.50f;
    public bool ChemistEtherSelfOnly { get; set; } = true;
    public int ChemistEtherMpThreshold { get; set; } = 2000;
    public float ChemistElixirPartyHpPct { get; set; } = 0.30f;

    // ── Oracle ──
    public bool OracleUseJudgment { get; set; } = true;
    public bool OracleUseCleansing { get; set; } = true;
    public bool OracleUseBlessing { get; set; } = true;
    public bool OracleUseStarfall { get; set; } = true;
    public bool OracleSaveInvulnForStarfall { get; set; } = true;
    public float OracleJudgmentPartyHpPct { get; set; } = 0.70f;
    public float OracleBlessingPartyHpPct { get; set; } = 0.50f;

    // ── Cannoneer ──
    /// <summary>Preferred cannon when the target takes both blind and paralysis.</summary>
    public bool CannoneerPreferDarkCannon { get; set; } = true;
    /// <summary>Cannon used when the target is immune to both debuffs.</summary>
    public bool CannoneerImmuneFallbackDark { get; set; } = true;

    // ── Geomancer ──
    public bool GeomancerSuspendInCombat { get; set; } = false;
    public bool GeomancerSuspendOutOfCombat { get; set; } = false;

    // ── Red Mage (North Horn) ──
    /// <summary>
    /// Self-HP fraction below which Occult Cure II is cast (40,000 cure potency for 1,500 MP).
    /// It is a 1.5s spell, so it costs a GCD — worth it at a real deficit, wasteful as a
    /// top-off, hence a lower default than the utility heals.
    /// </summary>
    private float _redMageCureHpPct = 0.60f;
    public float RedMageCureHpPct
    {
        get => _redMageCureHpPct;
        set => _redMageCureHpPct = System.Math.Clamp(value, 0.10f, 1f);
    }

    // ── Necromancer (North Horn) ──
    /// <summary>
    /// Deep Freeze: big 30y line nuke, but it costs 10% of MAX HP and DOOMS the caster for 10
    /// seconds — the Doom is dispelled ONLY by a heal back to FULL HP. Miss that and the toon
    /// dies (same class as the Oracle False Prediction death). OFF by default: turn it on only
    /// when a healer (or your own kit) reliably tops this toon to 100% within the window.
    /// </summary>
    public bool NecromancerUseDeepFreeze { get; set; } = false;

    /// <summary>
    /// Minimum HP fraction required before Deep Freeze is allowed to fire — the 10% max-HP
    /// cost plus incoming damage must not leave the toon in a hole it can't be healed out of.
    /// </summary>
    private float _necromancerDeepFreezeMinHpPercent = 0.95f;
    public float NecromancerDeepFreezeMinHpPercent
    {
        get => _necromancerDeepFreezeMinHpPercent;
        set => _necromancerDeepFreezeMinHpPercent = System.Math.Clamp(value, 0.5f, 1f);
    }

    /// <summary>
    /// Require the Drain Touch self-buff ("attacks cannot reduce own HP below 1") before Deep
    /// Freeze. That buff is what makes the HP cost survivable and raises Deep Freeze's potency
    /// (300→400, 390→520 on ice-weak targets), so the combo is both safer AND stronger.
    /// </summary>
    public bool NecromancerDeepFreezeRequireDrainTouch { get; set; } = true;

    /// <summary>
    /// Spend the Doom where it pays most: hold Deep Freeze on enemies we have LEARNED are not
    /// ice-weak. Ice-weak targets take 520 potency instead of 400 under Drain Touch (+30%, i.e.
    /// +120 per cast), so on anything long-lived the reveal repays itself in ~4 casts — and
    /// taking a 10%-HP cost plus a death timer for 77% of the payoff is the bad trade.
    /// Enemies whose weakness is still unknown are ALLOWED, so this can never lock the action
    /// out before the weakness table has learned anything.
    /// </summary>
    public bool NecromancerMatchElementalWeakness { get; set; } = true;

    /// <summary>
    /// Doomsday: unaspected (350, or 500 under Drain Touch), its own 120s recast, and it
    /// strips one beneficial status from the target under Drain Touch. Dooms the caster the
    /// same way the elemental nukes do, so it lives behind the same healer requirement.
    /// </summary>
    public bool NecromancerUseDoomsday { get; set; } = false;
}
