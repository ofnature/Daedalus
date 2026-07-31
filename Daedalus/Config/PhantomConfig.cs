namespace Daedalus.Config;

/// <summary>
/// Occult Crescent phantom action settings (Phase 2 of docs/occult-phantom-plan.md).
/// Consumed by the Phase 3+ PhantomActionLayer; until then only the config UI reads it.
/// Thresholds default to RSR PhantomDefault's field-tested values.
/// </summary>
public sealed class PhantomConfig
{
    /// <summary>Master toggle for the phantom action layer (in-zone only).</summary>
    public bool EnablePhantomActions { get; set; } = true;

    /// <summary>Hold damage phantom actions for the main job's burst window.
    /// Survival/utility actions ignore this.</summary>
    public bool SaveDamageForBurst { get; set; } = true;

    /// <summary>Auto-open the compact zone HUD (consumables, currency, shard banner)
    /// when entering Occult Crescent.</summary>
    public bool ShowOccultHud { get; set; } = true;

    // ── Freelancer ──
    public float FreelancerResuscitationHpPct { get; set; } = 0.70f;
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
    public bool NecromancerDeepFreezePreferIceWeak { get; set; } = true;
}
