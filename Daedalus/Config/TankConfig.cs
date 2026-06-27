using System;
using Daedalus.Data;

namespace Daedalus.Config;

/// <summary>
/// Configuration settings for tank rotations.
/// </summary>
public sealed class TankConfig
{
    public TankConfig()
    {
        PaladinAoEMinTargetsOverride = 2;
        WarriorAoEMinTargetsOverride = 2;
    }

    /// <summary>
    /// Enable automatic mitigation cooldown usage.
    /// </summary>
    public bool EnableMitigation { get; set; } = true;

    /// <summary>
    /// Enable damage rotation (DPS actions).
    /// </summary>
    public bool EnableDamage { get; set; } = true;

    /// <summary>
    /// Automatically enable tank stance when entering combat.
    /// </summary>
    public bool AutoTankStance { get; set; } = true;

    /// <summary>
    /// Overrides automatic MT/OT detection.
    /// Null = auto-detect based on who the enemy is targeting.
    /// True = always behave as Main Tank (suppress Provoke, use MT-specific mitigation).
    /// False = always behave as Off Tank (use Provoke when appropriate, use OT mitigation).
    /// </summary>
    public bool? IsMainTankOverride { get; set; } = null;

    /// <summary>
    /// Enable automatic Provoke when losing aggro.
    /// </summary>
    public bool AutoProvoke { get; set; } = true;

    /// <summary>
    /// HP percentage threshold for using mitigation cooldowns.
    /// Lower values = more conservative (waits until lower HP).
    /// Range: 0.0 to 1.0 (0% to 100%).
    /// </summary>
    private float _mitigationThreshold = 0.70f;
    public float MitigationThreshold
    {
        get => _mitigationThreshold;
        set => _mitigationThreshold = Math.Clamp(value, 0f, 1f);
    }

    /// <summary>
    /// Use Rampart (or equivalent major cooldown) on cooldown when in combat.
    /// If false, major cooldowns are saved for tank busters.
    /// </summary>
    public bool UseRampartOnCooldown { get; set; } = false;

    /// <summary>
    /// Minimum Oath Gauge (Paladin) / Beast Gauge (Warrior) / etc. required to use short cooldowns.
    /// Range: 0 to 100.
    /// </summary>
    private int _sheltronMinGauge = 50;
    public int SheltronMinGauge
    {
        get => _sheltronMinGauge;
        set => _sheltronMinGauge = Math.Clamp(value, 0, 100);
    }

    /// <summary>
    /// Enable automatic Shirk to co-tank after tank swap.
    /// </summary>
    public bool AutoShirk { get; set; } = false;

    /// <summary>
    /// Number of seconds after losing aggro before using Provoke.
    /// Prevents accidental Provokes during intended tank swaps.
    /// Range: 0.0 to 5.0 seconds.
    /// </summary>
    private float _provokeDelay = 1.0f;
    public float ProvokeDelay
    {
        get => _provokeDelay;
        set => _provokeDelay = Math.Clamp(value, 0f, 5f);
    }

    /// <summary>
    /// Enable Reprisal (party-wide mitigation).
    /// </summary>
    public bool EnableReprisal { get; set; } = true;

    /// <summary>
    /// Enable Interject (interrupt).
    /// </summary>
    public bool EnableInterject { get; set; } = true;

    /// <summary>
    /// Enable Low Blow (stun).
    /// </summary>
    public bool EnableLowBlow { get; set; } = true;

    /// <summary>
    /// Enable Arm's Length (knockback immunity + slow).
    /// </summary>
    public bool EnableArmsLength { get; set; } = true;

    /// <summary>
    /// Enable AoE damage abilities (Total Eclipse, etc.).
    /// </summary>
    public bool EnableAoEDamage { get; set; } = true;

    /// <summary>
    /// Minimum number of enemies required for AoE damage rotation.
    /// Range: 2 to 8.
    /// </summary>
    private int _aoEMinTargets = 3;
    public int AoEMinTargets
    {
        get => _aoEMinTargets;
        set => _aoEMinTargets = Math.Clamp(value, 2, 8);
    }

    /// <summary>
    /// Paladin-specific AoE breakeven override. Null inherits <see cref="AoEMinTargets"/>.
    /// </summary>
    private int? _paladinAoEMinTargetsOverride;
    public int? PaladinAoEMinTargetsOverride
    {
        get => _paladinAoEMinTargetsOverride;
        set => _paladinAoEMinTargetsOverride = value.HasValue ? Math.Clamp(value.Value, 2, 8) : null;
    }

    /// <summary>
    /// Warrior-specific AoE breakeven override. Null inherits <see cref="AoEMinTargets"/>.
    /// </summary>
    private int? _warriorAoEMinTargetsOverride;
    public int? WarriorAoEMinTargetsOverride
    {
        get => _warriorAoEMinTargetsOverride;
        set => _warriorAoEMinTargetsOverride = value.HasValue ? Math.Clamp(value.Value, 2, 8) : null;
    }

    /// <summary>
    /// Dark Knight-specific AoE breakeven override. Null inherits <see cref="AoEMinTargets"/>.
    /// </summary>
    private int? _darkKnightAoEMinTargetsOverride;
    public int? DarkKnightAoEMinTargetsOverride
    {
        get => _darkKnightAoEMinTargetsOverride;
        set => _darkKnightAoEMinTargetsOverride = value.HasValue ? Math.Clamp(value.Value, 2, 8) : null;
    }

    /// <summary>
    /// Gunbreaker-specific AoE breakeven override. Null inherits <see cref="AoEMinTargets"/>.
    /// </summary>
    private int? _gunbreakerAoEMinTargetsOverride;
    public int? GunbreakerAoEMinTargetsOverride
    {
        get => _gunbreakerAoEMinTargetsOverride;
        set => _gunbreakerAoEMinTargetsOverride = value.HasValue ? Math.Clamp(value.Value, 2, 8) : null;
    }

    /// <summary>
    /// Resolves the effective AoE min-target threshold for a tank job.
    /// </summary>
    public int GetEffectiveAoEMinTargets(uint jobId) =>
        jobId switch
        {
            JobRegistry.Paladin or JobRegistry.Gladiator => PaladinAoEMinTargetsOverride ?? AoEMinTargets,
            JobRegistry.Warrior or JobRegistry.Marauder => WarriorAoEMinTargetsOverride ?? AoEMinTargets,
            JobRegistry.DarkKnight => DarkKnightAoEMinTargetsOverride ?? AoEMinTargets,
            JobRegistry.Gunbreaker => GunbreakerAoEMinTargetsOverride ?? AoEMinTargets,
            _ => AoEMinTargets,
        };

    #region Paladin

    /// <summary>
    /// Use Fight or Flight buff.
    /// </summary>
    public bool EnableFightOrFlight { get; set; } = true;

    /// <summary>
    /// Use Requiescat / Imperator for magic phase.
    /// </summary>
    public bool EnableRequiescat { get; set; } = true;

    /// <summary>
    /// Use Circle of Scorn (oGCD DoT).
    /// </summary>
    public bool EnableCircleOfScorn { get; set; } = true;

    /// <summary>
    /// Use Spirits Within / Expiacion (oGCD damage).
    /// </summary>
    public bool EnableSpiritsWithin { get; set; } = true;

    /// <summary>
    /// Use Intervene (gap closer).
    /// </summary>
    public bool EnableIntervene { get; set; } = true;

    /// <summary>
    /// Skip refreshing DoTs on targets about to die (RSR-style time-to-kill check).
    /// Disabled by default to preserve existing behavior.
    /// </summary>
    public bool EnableTimeToKillCheck { get; set; } = false;

    /// <summary>
    /// Minimum estimated seconds-to-kill below which DoTs are not refreshed.
    /// </summary>
    public float TimeToKillThresholdSeconds { get; set; } = 8f;

    /// <summary>
    /// Use Sentinel / Guardian (major mitigation).
    /// </summary>
    public bool EnableSentinel { get; set; } = true;

    /// <summary>
    /// Use Sheltron / Holy Sheltron (gauge-based mitigation).
    /// </summary>
    public bool EnableSheltron { get; set; } = true;

    /// <summary>
    /// Weave Sheltron / Holy Sheltron in combat whenever the Oath Gauge reaches
    /// <see cref="SheltronOvercapThreshold"/>, independent of incoming-damage gating. Oath regenerates
    /// passively (~50 per ~20s of combat), so dumping at cap keeps the physical-damage-reduction buff up
    /// at high uptime instead of wasting overcapped gauge. RSR parity: WhenToSheltron oath-overcap dump.
    /// </summary>
    public bool SheltronOathOvercapDump { get; set; } = true;

    /// <summary>
    /// Oath Gauge at or above which <see cref="SheltronOathOvercapDump"/> fires Sheltron in combat.
    /// 100 = only at hard cap; lower trades a little mitigation uptime for not sitting near cap.
    /// Range: 50 (Sheltron cost) to 100.
    /// </summary>
    private int _sheltronOvercapThreshold = 100;
    public int SheltronOvercapThreshold
    {
        get => _sheltronOvercapThreshold;
        set => _sheltronOvercapThreshold = Math.Clamp(value, 50, 100);
    }

    /// <summary>
    /// While moving in combat, tag stray adds that aren't on you yet with the job's ranged GCD
    /// (Shield Lob / Tomahawk / Unmend / Lightning Shot) so a wall-to-wall pull leaves nothing behind.
    /// Only fires while moving and only when not mid-combo (so it never breaks a 1-2-3 / AoE combo).
    /// Default true.
    /// </summary>
    public bool TagAddsWhileMovingWithRangedAttack { get; set; } = true;

    /// <summary>
    /// Use Hallowed Ground (invulnerability).
    /// </summary>
    public bool EnableHallowedGround { get; set; } = true;

    /// <summary>
    /// Use Bulwark (party mitigation).
    /// </summary>
    public bool EnableBulwark { get; set; } = true;

    /// <summary>
    /// Use Cover to redirect damage from a co-tank to yourself.
    /// </summary>
    public bool EnableCover { get; set; } = true;

    /// <summary>
    /// Proactively apply Divine Veil party shield.
    /// </summary>
    public bool EnableDivineVeil { get; set; } = true;

    /// <summary>
    /// Use Clemency GCD heal when HP is critically low.
    /// </summary>
    public bool EnableClemency { get; set; } = false;

    /// <summary>
    /// HP percentage threshold to trigger Clemency.
    /// Range: 0.0 to 1.0 (0% to 100%).
    /// </summary>
    private float _clemencyThreshold = 0.30f;
    public float ClemencyThreshold
    {
        get => _clemencyThreshold;
        set => _clemencyThreshold = Math.Clamp(value, 0f, 1f);
    }

    #endregion

    #region Warrior

    /// <summary>
    /// Use Inner Release / Berserk buff window.
    /// </summary>
    public bool EnableInnerRelease { get; set; } = true;

    /// <summary>
    /// Use Infuriate (gauge generation).
    /// </summary>
    public bool EnableInfuriate { get; set; } = true;

    /// <summary>
    /// Use Primal Rend (ranged GCD proc from Inner Release).
    /// </summary>
    public bool EnablePrimalRend { get; set; } = true;

    /// <summary>
    /// Auto-fire Primal Rend. When off, the player presses it manually (keeps
    /// gap-close agency); Primal Ruination still auto-fires once the combo is
    /// committed. Ignored when EnablePrimalRend is off.
    /// </summary>
    public bool AutoPrimalRend { get; set; } = false;

    /// <summary>
    /// Use Primal Ruination (follow-up GCD after Primal Rend).
    /// </summary>
    public bool EnablePrimalRuination { get; set; } = true;

    /// <summary>
    /// Use Primal Wrath (oGCD burst at Lv.96+, granted by stacking Burgeoning Fury
    /// during Inner Release via Fell Cleave). Self-centered AoE, no player-agency concern.
    /// </summary>
    public bool EnablePrimalWrath { get; set; } = true;

    /// <summary>
    /// Use Vengeance / Damnation (major mitigation).
    /// </summary>
    public bool EnableVengeance { get; set; } = true;

    /// <summary>
    /// Engaged pull size at or above which Vengeance / Damnation is used on cooldown as proactive
    /// mitigation for wall-to-wall pulls (sustained AoE damage), in addition to the reactive HP /
    /// damage-rate gate. Counts engaged enemies (pull size), not self-centered AoE hit count.
    /// Recommended default is 3 (a real pack). Range: 2 to 8.
    /// </summary>
    private int _vengeanceMinTargets = 3;
    public int VengeanceMinTargets
    {
        get => _vengeanceMinTargets;
        set => _vengeanceMinTargets = Math.Clamp(value, 2, 8);
    }

    /// <summary>
    /// Use Raw Intuition / Blood Whetting (short mitigation + heal).
    /// </summary>
    public bool EnableBloodWhetting { get; set; } = true;

    /// <summary>
    /// HP percentage at or below which Bloodwhetting / Raw Intuition is used as reactive sustain
    /// (10% mitigation + heal-on-weaponskill). Kept independent of the shared
    /// <see cref="MitigationThreshold"/> so the short-cooldown self-heal can be tuned separately from
    /// the major-cooldown gate. Raw Intuition / Bloodwhetting no longer costs Beast Gauge, so there is
    /// no gauge gate. At or below this value the weave is escalated above damage oGCDs so it actually
    /// fires in a busy burst window instead of being starved out of the single oGCD slot.
    /// Set to 1.0 (100%) to use it on cooldown as pure sustain. Range: 0.0 to 1.0 (0% to 100%).
    /// </summary>
    private float _bloodwhettingThreshold = 0.70f;
    public float BloodwhettingThreshold
    {
        get => _bloodwhettingThreshold;
        set => _bloodwhettingThreshold = Math.Clamp(value, 0f, 1f);
    }

    /// <summary>
    /// Use Thrill of Battle (HP boost + heal).
    /// </summary>
    public bool EnableThrillOfBattle { get; set; } = true;

    /// <summary>
    /// Use Shake It Off (party shield).
    /// </summary>
    public bool EnableShakeItOff { get; set; } = true;

    /// <summary>
    /// Use Upheaval / Orogeny (oGCD damage).
    /// </summary>
    public bool EnableOrogeny { get; set; } = true;

    /// <summary>
    /// Use Equilibrium (self-heal).
    /// </summary>
    public bool EnableEquilibrium { get; set; } = true;

    /// <summary>
    /// Use Onslaught (gap closer).
    /// </summary>
    public bool EnableOnslaught { get; set; } = true;

    /// <summary>
    /// Auto-weave Onslaught as damage. Burst-aligned: only weaves during Inner Release or when about to
    /// overcap charges, and never while moving (so the dash doesn't pull you out of position). When off,
    /// Onslaught is only used to close the gap (uptime). Ignored when EnableOnslaught is off.
    /// </summary>
    public bool AutoOnslaught { get; set; } = true;

    /// <summary>
    /// Pull with the job's ranged GCD (WAR Tomahawk) out of combat when an enemy is hard-targeted —
    /// a pre-pull opener. Default false. (Planned: gate to trials/raids only.)
    /// </summary>
    public bool EnablePrePullTomahawk { get; set; } = false;

    /// <summary>
    /// Share mitigation and healing with a party member via Nascent Flash.
    /// </summary>
    public bool EnableNascentFlash { get; set; } = true;

    /// <summary>
    /// Use Holmgang as an invulnerability cooldown.
    /// </summary>
    public bool EnableHolmgang { get; set; } = true;

    /// <summary>
    /// Spend Beast Gauge before reaching this cap to avoid overcapping.
    /// Range: 0 to 100.
    /// </summary>
    private int _beastGaugeCap = 90;
    public int BeastGaugeCap
    {
        get => _beastGaugeCap;
        set => _beastGaugeCap = Math.Clamp(value, 0, 100);
    }

    #endregion

    #region Dark Knight

    /// <summary>
    /// Use Blood Weapon / Delirium (gauge generation buff).
    /// </summary>
    public bool EnableBloodWeapon { get; set; } = true;

    /// <summary>
    /// Use Delirium (burst window).
    /// </summary>
    public bool EnableDelirium { get; set; } = true;

    /// <summary>
    /// Use Shadow Wall / Shadow Vigil (major mitigation).
    /// </summary>
    public bool EnableShadowWall { get; set; } = true;

    /// <summary>
    /// Use Dark Mind (magic mitigation).
    /// </summary>
    public bool EnableDarkMind { get; set; } = true;

    /// <summary>
    /// Use Oblation (short mitigation).
    /// </summary>
    public bool EnableOblation { get; set; } = true;

    /// <summary>
    /// Use Salted Earth (ground DoT).
    /// </summary>
    public bool EnableSaltedEarth { get; set; } = true;

    /// <summary>
    /// Minimum enemy count required to use Salted Earth. It's a 90s, 15s-duration AoE DoT, so on a
    /// wall-to-wall route you may want to hold it for big packs rather than spend it on a straggler.
    /// Default 1 = use on cooldown (optimal single-target/boss DPS, RSR behaviour); set higher (e.g. 5)
    /// to save it for large pulls. Range: 1 to 8.
    /// </summary>
    private int _saltedEarthMinTargets = 1;
    public int SaltedEarthMinTargets
    {
        get => _saltedEarthMinTargets;
        set => _saltedEarthMinTargets = Math.Clamp(value, 1, 8);
    }

    /// <summary>
    /// Use Carve and Spit (oGCD damage + MP).
    /// </summary>
    public bool EnableCarveAndSpit { get; set; } = true;

    /// <summary>
    /// Use Shadowbringer (oGCD damage).
    /// </summary>
    public bool EnableShadowbringer { get; set; } = true;

    /// <summary>
    /// Use Living Shadow (pet summon).
    /// </summary>
    public bool EnableLivingShadow { get; set; } = true;

    /// <summary>
    /// Use Abyssal Drain (AoE oGCD damage + heal).
    /// </summary>
    public bool EnableAbyssalDrain { get; set; } = true;

    /// <summary>
    /// Use Shadowstride (gap closer).
    /// </summary>
    public bool EnableShadowstride { get; set; } = true;

    /// <summary>
    /// Auto-weave Shadowstride as filler damage. Off by default — when off, Shadowstride is only used to
    /// close the gap to an out-of-range target, not woven in melee. Weaving it in melee darts you around
    /// the pack and eats weave slots other oGCDs (e.g. Salted Earth) need. Opt-in. Ignored when
    /// EnableShadowstride is off.
    /// </summary>
    public bool AutoShadowstride { get; set; } = false;

    /// <summary>
    /// Use Living Dead as an invulnerability cooldown.
    /// </summary>
    public bool EnableLivingDead { get; set; } = true;

    /// <summary>
    /// Use Dark Missionary for party magic damage mitigation.
    /// </summary>
    public bool EnableDarkMissionary { get; set; } = true;

    /// <summary>
    /// Apply The Blackest Night shield to the tank when HP is high enough.
    /// </summary>
    public bool EnableTheBlackestNight { get; set; } = true;

    /// <summary>
    /// HP percentage threshold to apply The Blackest Night as a reactive shield.
    /// Range: 0.0 to 1.0 (0% to 100%).
    /// </summary>
    private float _tbnThreshold = 0.80f;
    public float TBNThreshold
    {
        get => _tbnThreshold;
        set => _tbnThreshold = Math.Clamp(value, 0f, 1f);
    }

    /// <summary>
    /// Use The Blackest Night proactively on the tank while actively taking damage, so the shield breaks
    /// and grants Dark Arts — a free Edge/Flood of Shadow (consumes Dark Arts instead of 3000 MP). This is
    /// MP-neutral and adds a free shield, so it's a small DPS gain on top of mitigation. Only fires while
    /// taking damage (so the shield reliably breaks), with MP to spare, and when no Dark Arts is already
    /// banked. Default true.
    /// </summary>
    public bool TBNProactiveBanking { get; set; } = true;

    /// <summary>
    /// Spend Blood Gauge before reaching this cap to avoid overcapping.
    /// Range: 0 to 100.
    /// </summary>
    private int _bloodGaugeCap = 90;
    public int BloodGaugeCap
    {
        get => _bloodGaugeCap;
        set => _bloodGaugeCap = Math.Clamp(value, 0, 100);
    }

    #endregion

    #region Gunbreaker

    /// <summary>
    /// Use No Mercy (damage buff).
    /// </summary>
    public bool EnableNoMercy { get; set; } = true;

    /// <summary>
    /// Use Bloodfest (cartridge generation).
    /// </summary>
    public bool EnableBloodfest { get; set; } = true;

    /// <summary>
    /// Use Camouflage (mitigation).
    /// </summary>
    public bool EnableCamouflage { get; set; } = true;

    /// <summary>
    /// Use Nebula / Great Nebula (major mitigation).
    /// </summary>
    public bool EnableNebula { get; set; } = true;

    /// <summary>
    /// Engaged pull size at or above which GNB's sustain mitigation (Camouflage, Rampart, Nebula) is used
    /// proactively on cooldown for wall-to-wall pulls, in addition to the reactive HP / damage-rate gate.
    /// Without this, those cooldowns wait for HP to fall below <see cref="MitigationThreshold"/>, so on a
    /// big pull they fire late (after you have already taken avoidable damage). Takes the larger of engaged
    /// pull size and self-centered AoE count. Recommended default is 3 (a real pack). Range: 2 to 8.
    /// </summary>
    private int _proactiveMitMinTargets = 3;
    public int ProactiveMitMinTargets
    {
        get => _proactiveMitMinTargets;
        set => _proactiveMitMinTargets = Math.Clamp(value, 2, 8);
    }

    /// <summary>
    /// Use Aurora (regen).
    /// </summary>
    public bool EnableAurora { get; set; } = true;

    /// <summary>
    /// Use Bow Shock (oGCD AoE damage + DoT).
    /// </summary>
    public bool EnableBowShock { get; set; } = true;

    /// <summary>
    /// Use Trajectory (gap closer).
    /// </summary>
    public bool EnableTrajectory { get; set; } = true;

    /// <summary>
    /// Use Continuation abilities (Jugular Rip, Abdomen Tear, Eye Gouge, Hypervelocity).
    /// </summary>
    public bool EnableContinuation { get; set; } = true;

    /// <summary>
    /// Use Superbolide (invulnerability).
    /// </summary>
    public bool EnableSuperbolide { get; set; } = true;

    /// <summary>
    /// Use Heart of Light for party magic damage mitigation.
    /// </summary>
    public bool EnableHeartOfLight { get; set; } = true;

    /// <summary>
    /// Apply Heart of Corundum shield to the tank.
    /// </summary>
    public bool EnableHeartOfCorundum { get; set; } = true;

    /// <summary>
    /// HP percentage threshold to apply Heart of Corundum.
    /// Range: 0.0 to 1.0 (0% to 100%).
    /// </summary>
    private float _heartOfCorundumThreshold = 0.80f;
    public float HeartOfCorundumThreshold
    {
        get => _heartOfCorundumThreshold;
        set => _heartOfCorundumThreshold = Math.Clamp(value, 0f, 1f);
    }

    #endregion

    #region Shared tank behavior

    /// <summary>
    /// When the target is out of melee range, pull it with the job's ranged GCD (Lightning Shot,
    /// Shield Lob, Tomahawk, Unmend) while staying put instead of gap-closing/dashing into it.
    /// Useful for pulling stationary/ranged mobs to camp without running out of position.
    /// The gap-closer is still used as a weave damage oGCD once in melee range.
    /// Default false (preserve dash-to-engage behavior).
    /// </summary>
    public bool PullRangedMobsWithRangedAttack { get; set; } = false;

    /// <summary>
    /// When a mob has slipped to another player (we lost aggro, it's now out of melee and targeting
    /// someone else), don't dash after it with the gap-closer. Provoke (25y, auto-fired by the
    /// EnmityModule) plus the ranged GCD reclaim it in place, so the tank stays on the pack and resumes
    /// AoE/ST instead of running across the room. Gap-closers still weave as damage once in melee, and
    /// initial dash-to-engage on un-aggroed pulls is unaffected. Default true.
    /// </summary>
    public bool SuppressGapCloserOnLostMob { get; set; } = true;

    /// <summary>
    /// When a co-tank is present (party has another tank), do not auto-acquire loose adds — stick to
    /// the player's current hard target instead of letting the enemy strategy expand to additional
    /// enemies. Does not affect Provoke/tank-swap logic. No-op when solo / single-tank.
    /// Default false (keep grabbing adds).
    /// </summary>
    public bool IgnoreAddsWithCoTank { get; set; } = false;

    #endregion

    /// <summary>
    /// Enable coordination of personal defensive cooldowns between Daedalus tanks.
    /// When enabled, tanks will stagger major mitigations (Rampart, Sentinel, etc.)
    /// to maximize mitigation uptime across a tankbuster sequence.
    /// </summary>
    public bool EnableDefensiveCoordination { get; set; } = true;

    /// <summary>
    /// Time window in seconds to delay personal defensives if another tank used one recently.
    /// Range: 1.0 to 10.0 seconds.
    /// </summary>
    private float _defensiveStaggerWindowSeconds = 3.0f;
    public float DefensiveStaggerWindowSeconds
    {
        get => _defensiveStaggerWindowSeconds;
        set => _defensiveStaggerWindowSeconds = Math.Clamp(value, 1f, 10f);
    }

    /// <summary>
    /// Enable coordination of invulnerability abilities between Daedalus tanks.
    /// When enabled, tanks will avoid using invulns simultaneously to maximize coverage.
    /// </summary>
    public bool EnableInvulnerabilityCoordination { get; set; } = true;

    /// <summary>
    /// Time window in seconds to delay invulnerability if another tank used one recently.
    /// Range: 1.0 to 10.0 seconds.
    /// </summary>
    private float _invulnerabilityStaggerWindowSeconds = 5.0f;
    public float InvulnerabilityStaggerWindowSeconds
    {
        get => _invulnerabilityStaggerWindowSeconds;
        set => _invulnerabilityStaggerWindowSeconds = Math.Clamp(value, 1f, 10f);
    }
}
