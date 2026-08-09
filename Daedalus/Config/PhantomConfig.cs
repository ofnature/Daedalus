using System.Collections.Generic;

namespace Daedalus.Config;

/// <summary>
/// One recorded coffer spawn point: where it was, what tier, and how often it has been seen.
/// Position is stored flat rather than as a Vector3 so the config round trip stays boring.
/// </summary>
public sealed class ChestLedgerEntry
{
    public ushort Zone { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }

    /// <summary>Most-observed tier at this spot — a convenience view over <see cref="TierCounts"/>.</summary>
    public string Tier { get; set; } = "Unknown";

    /// <summary>
    /// How many times each tier has been seen at this spot, e.g. {"Bronze":3,"Silver":1}.
    /// <para>
    /// A spot can genuinely produce different tiers on different spawns, and two toons watching
    /// the same spot will disagree. Keeping a single tier field threw that away — last write
    /// won and the conflict vanished. A predictor needs the distribution, not the last sample.
    /// </para>
    /// </summary>
    public Dictionary<string, int> TierCounts { get; set; } = [];

    public int TimesSeen { get; set; }

    /// <summary>
    /// How many times a coffer at this spot was opened — **witnessed, not necessarily by you**.
    /// <para>
    /// Detection is the Opened flag transition (or a nearby despawn), and neither carries an
    /// owner, so in a 72-player zone this counts other people's chests too. That is fine for
    /// what the ledger is for: the location and the tier are true regardless of who looted it,
    /// and other players' spawns are extra samples rather than noise. It is NOT a record of
    /// what you personally picked up.
    /// </para>
    /// </summary>
    public int TimesOpened { get; set; }

    /// <summary>
    /// This coffer was seen while "Cache Me if You Can" (the pot FATE treasure hunt) was up.
    /// <para>
    /// The discriminator the whole pot-coffer question turns on: a hunt coffer is otherwise
    /// indistinguishable from a world coffer — same object kind, same name, same fields — so
    /// without this the candidate positions can never be separated from ordinary chests.
    /// Sticky: once true it stays true, since a spot that has EVER produced a hunt coffer is a
    /// candidate regardless of what else spawns there.
    /// </para>
    /// </summary>
    public bool FoundDuringTreasureHunt { get; set; }

    /// <summary>
    /// Source of this coffer — the two are entirely different mechanics and must never be
    /// pooled: <c>EventObj</c> coffers come from POTS (the hidden hunt coffer), while
    /// <c>Treasure</c> chests are rolled PER PLAYER when you enter the instance.
    /// <para>
    /// That second point is why a spot's tier looks fixed within a visit and re-rolls on the
    /// next one, and it is the discriminator any predictor has to split on first.
    /// </para>
    /// </summary>
    public string Source { get; set; } = "Treasure";

    /// <summary>
    /// The object's BaseId — recorded so coffer TYPES stay separable later even where the name
    /// and object kind match.
    /// <para>
    /// Specifically: chests raised by a Fortune Carrot are expected to be EventObj as well, and
    /// one opened during a pot hunt would otherwise satisfy every candidate test. Keeping the id
    /// means that can be untangled from data already collected, instead of gathering it again.
    /// Known so far: pot Gold Coffer = 2014741; carrot chest id UNKNOWN.
    /// </para>
    /// </summary>
    public uint BaseId { get; set; }

    public long FirstSeenUnixSeconds { get; set; }
    public long LastSeenUnixSeconds { get; set; }
}

/// <summary>
/// One measured elixir reading: what the game called the distance, and how far it actually
/// turned out to be once the coffer was found. Ground truth for the guessed distance bands.
/// </summary>
public sealed class PotHuntCalibrationSample
{
    public string Band { get; set; } = string.Empty;
    public float ActualDistance { get; set; }

    /// <summary>
    /// How far off the reported compass direction the treasure actually was, in radians. The
    /// arc must be at least as wide as the worst of these, so enough samples turn the guessed
    /// 22.5 degrees into a measurement.
    /// </summary>
    public float AngularErrorRadians { get; set; }
}

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

    /// <summary>
    /// Also raise dead players who are NOT in your party.
    /// <para>
    /// In a 72-player Critical Encounter most bodies on the floor belong to strangers, so a
    /// party-only scan reports "nobody down" while someone lies a few yalms away. Occult Raise
    /// is instant and costs nothing but the recast, which makes picking them up close to free —
    /// and it is the entire point of carrying the ability into a CE.
    /// </para>
    /// <para>
    /// Party members always take precedence; strangers are only considered once nobody in the
    /// party needs it.
    /// </para>
    /// </summary>
    public bool RaiseNonPartyPlayers { get; set; } = true;

    /// <summary>
    /// Every coffer spawn point we've seen in an Occult zone, with its tier. Pure evidence —
    /// nothing reads it yet beyond a count. It exists so that questions we can't answer today
    /// (is a spot's tier fixed? do gold coffers repeat?) become answerable from real samples
    /// rather than from one lucky observation.
    /// <para>
    /// Unlike the pot FATE timer this is NOT cleared on leaving the zone: spawn points are a
    /// property of the map, not of the instance.
    /// </para>
    /// </summary>
    public List<ChestLedgerEntry> ChestLedger { get; set; } = [];

    /// <summary>
    /// Elixir readings measured against where the coffer actually turned out to be. Only
    /// "immediately" (&lt;10y) is a confirmed band; these samples are how the rest stop being
    /// guesses.
    /// </summary>
    public List<PotHuntCalibrationSample> PotHuntCalibration { get; set; } = [];

    /// <summary>
    /// Distance from the player to the coffer at the instant it spawned — i.e. the interact
    /// range that triggers it. Measured rather than assumed, so the "activation area" ring can
    /// be sized from observation.
    /// </summary>
    public List<float> ActivationRadiusSamples { get; set; } = [];
    public bool UseTreasuresight { get; set; } = false;

    // ── Phantom buff cycle (docs/occult-buff-cycle.md) ──────────────────────────────────
    // Phantom self-buffs last ~30 minutes and survive a job switch, so cycling the jobs once
    // leaves you carrying all of them. Cast beside a Knowledge Crystal they reach every party
    // member in the zone, so one toon can cover a fleet.

    /// <summary>Collect Knight's Pray — −10% damage taken.</summary>
    public bool BuffCycleKnight { get; set; } = true;

    /// <summary>
    /// Collect Bard's Romeo's Ballad — +10% phantom EXP. This is an EXP buff, not a combat one:
    /// worth keeping up while levelling phantom jobs and worth nothing once they are capped.
    /// </summary>
    public bool BuffCycleBard { get; set; } = true;

    /// <summary>Collect Monk's Counterstance — movement speed. Needs Monk Lv3, not Lv2.</summary>
    public bool BuffCycleMonk { get; set; } = true;

    /// <summary>Collect Dancer's Quickstep — +2% damage dealt.</summary>
    public bool BuffCycleDancer { get; set; } = true;

    /// <summary>
    /// Re-collect automatically once the weakest buff drops below this many seconds. Only the
    /// buffs this character can actually hold are counted, so a locked job never keeps the
    /// minimum pinned at zero and re-triggers forever.
    /// </summary>
    public float BuffCycleRefreshSeconds { get; set; } = 600f;

    /// <summary>
    /// Run the cycle automatically when the threshold trips. Default OFF — a feature that
    /// switches your job four times should be trusted from the button first.
    /// </summary>
    public bool BuffCycleAutoRefresh { get; set; } = false;


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
