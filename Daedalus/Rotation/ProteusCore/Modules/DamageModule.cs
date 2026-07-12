using System;
using System.Collections.Generic;
using Daedalus.Config.DPS;
using Daedalus.Data;
using Daedalus.Rotation.Common.Helpers;
using Daedalus.Rotation.Common.Scheduling;
using Daedalus.Rotation.PrometheusCore.Helpers;
using Daedalus.Rotation.ProteusCore.Abilities;
using Daedalus.Rotation.ProteusCore.Context;

namespace Daedalus.Rotation.ProteusCore.Modules;

/// <summary>
/// Blue Mage damage. Two layers:
///
/// GCD priority: freeze→shatter (Ram's Voice → Ultravibration, instantly deletes trash packs) →
/// Bristle-snapshotted DoTs (Mortal Flame once per target, Breath of Magic upkeep) → Song of
/// Torment → White Death (Touch of Frost filler) → Rose of Destruction → Matra Magic → Cold Fog →
/// Bad Breath (AoE debuff) → Plaincracker (AoE) → role filler → Water Cannon.
///
/// oGCD weaves: Surpanakha 4-charge dump (strictly consecutive — fury gate) → Feather Rain
/// (ground-placed) → Glass Dance → Both Ends, all off cooldown.
///
/// Unslotted spells are rejected at dispatch and fall through, so a thin loadout degrades
/// instead of stalling. During a freeze→shatter window ALL other damage is suppressed —
/// Deep Freeze breaks on damage (the NIN mudra-block pattern).
/// </summary>
public sealed class DamageModule : IProteusModule
{
    public int Priority => 30;
    public string Name => "Damage";

    public bool TryExecute(IProteusContext context, bool isMoving) => false;
    public void UpdateDebugState(IProteusContext context) { }

    // ── Freeze→shatter state (combat-scoped) ────────────────────────────────
    /// <summary>Injectable clock so tests can age the freeze window.</summary>
    internal Func<DateTime> UtcNow = () => DateTime.UtcNow;

    private DateTime? _freezeArmedUtc;
    private bool _packFreezeImmune;
    private bool _wasInCombat;

    // Mortal Flame once-per-target latch. The status check (3643) is primary; this latch is the
    // safety net so a wrong status id can never chain-cast — one attempt per target per window.
    private readonly Dictionary<ulong, DateTime> _mortalFlameLatch = new();
    private const float MortalFlameRetrySeconds = 60f;

    // Targets that already carry a Moon-Flute-buffed Mortal Flame snapshot. During Waxing an
    // existing UNBUFFED Mortal Flame is deliberately recast once (a recast replaces the snapshot —
    // buffed beats unbuffed); a buffed one is never touched again.
    private readonly HashSet<ulong> _mortalFlameBuffedTargets = new();

    // Pack time-to-kill for the Moon Flute gate (the NIN Trick / MCH Queen lesson at 10× the
    // cost: a Flute on a dying pack burns 15s of Waning for nothing).
    private readonly PackTtkEstimator _packTtk = new();

    /// <summary>Deep Freeze must outlast Ultravibration's 2.0s cast to be shatterable.</summary>
    private const float ShatterMinFreezeRemaining = 2.2f;

    /// <summary>No Deep Freeze this long after Ram's Voice dispatch → the pack is freeze-immune.</summary>
    private const float FreezeImmuneGraceSeconds = 4f;

    /// <summary>Hard cap on a freeze→shatter window (freeze lasts 12s from application).</summary>
    private const float FreezeWindowMaxSeconds = 12f;

    public void CollectCandidates(IProteusContext context, RotationScheduler scheduler, bool isMoving)
    {
        if (!context.InCombat)
        {
            if (_wasInCombat) ResetCombatState();
            context.Debug.DamageState = "Not in combat";
            return;
        }
        _wasInCombat = true;

        if (context.HasDiamondback) { context.Debug.DamageState = "Diamondback (locked)"; return; }
        if (context.HasWaningNocturne) { context.Debug.DamageState = "Waning Nocturne (locked out)"; return; }
        if (context.TargetingService.IsDamageTargetingPaused())
        {
            context.Debug.DamageState = "Paused (no target)";
            return;
        }

        var player = context.Player;
        var target = context.TargetingService.FindEnemy(
            context.Configuration.Targeting.EnemyStrategy,
            FFXIVConstants.CasterTargetingRange,
            player);
        if (target == null) { context.Debug.DamageState = "No target"; return; }

        var cfg = context.Configuration.BlueMage;

        // Feed the pack TTK estimate every combat frame (Moon Flute gate).
        _packTtk.Sample(
            context.TargetingService.SumEnemyCurrentHpInRange(FFXIVConstants.CasterTargetingRange, player),
            UtcNow());

        // ── Freeze→shatter window: while armed, Deep Freeze breaks on ANY damage — the only
        // legal push is the shatter itself (mudra-block pattern). ──
        if (TryHandleFreezeShatter(context, scheduler, isMoving))
            return;

        PushOffensiveOgcds(context, scheduler, player, target);
        PushGcdChain(context, scheduler, player, target, cfg, isMoving);
    }

    private void ResetCombatState()
    {
        _freezeArmedUtc = null;
        _packFreezeImmune = false;
        _wasInCombat = false;
        _mortalFlameLatch.Clear();
        _mortalFlameBuffedTargets.Clear();
        _packTtk.Reset();
    }

    // ── Moon Flute (V2.3, kit-driven) ───────────────────────────────────────
    //
    // The user's kit has none of the constraint-heavy opener pieces (Whistle/Tingle/Triple
    // Trident/Phantom Flurry), so no scripted sequence is needed: once Waxing Nocturne is up the
    // normal priority chain IS the burst (Bristle→DoT re-snapshots, Rose, Matra, Feather Rain/
    // Glass Dance/Both Ends weaves, Surpanakha dump). This method only decides WHEN to start the
    // window: every slotted big piece off cooldown + Surpanakha at 4 charges + the pack living
    // long enough to pay for 15s of Waning lockout.
    private void TryPushMoonFlute(
        IProteusContext context, RotationScheduler scheduler, BlueMageConfig cfg,
        Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter player, bool isMoving)
    {
        if (!cfg.EnableMoonFlute) return;
        if (context.HasWaxingNocturne) return; // window already running
        if (!context.IsSpellUsable(BLUActions.MoonFlute.ActionId)) return;
        if (isMoving || MechanicCastGate.ShouldBlock(context, BLUActions.MoonFlute.CastTime)) return;

        // Every ENABLED+SLOTTED big piece must be ready; at least two pieces must exist at all
        // (a naked Flute is net negative — Waning costs ~6 GCDs).
        var pieces = 0;
        var allReady = true;
        void Piece(uint actionId, bool enabled, Func<bool> ready)
        {
            if (!enabled || !context.IsSpellUsable(actionId)) return;
            pieces++;
            if (!ready()) allReady = false;
        }

        Piece(BLUActions.MatraMagic.ActionId, cfg.EnableMatraMagic,
            () => context.ActionService.GetCooldownRemaining(BLUActions.MatraMagic.ActionId) <= 0f);
        Piece(BLUActions.TheRoseOfDestruction.ActionId, cfg.EnableRoseOfDestruction,
            () => context.ActionService.GetCooldownRemaining(BLUActions.TheRoseOfDestruction.ActionId) <= 0f);
        Piece(BLUActions.BothEnds.ActionId, cfg.EnableOffensiveOgcds,
            () => context.ActionService.GetCooldownRemaining(BLUActions.BothEnds.ActionId) <= 0f);
        Piece(BLUActions.GlassDance.ActionId, cfg.EnableOffensiveOgcds,
            () => context.ActionService.GetCooldownRemaining(BLUActions.GlassDance.ActionId) <= 0f);
        Piece(BLUActions.FeatherRain.ActionId, cfg.EnableOffensiveOgcds,
            () => context.ActionService.GetCooldownRemaining(BLUActions.FeatherRain.ActionId) <= 0f);
        Piece(BLUActions.Surpanakha.ActionId, cfg.EnableSurpanakha,
            () => context.ActionService.GetCurrentCharges(BLUActions.Surpanakha.ActionId) >= 4);

        if (pieces < 2 || !allReady) return;

        // TTK: hold only on positive evidence the pack dies too soon (no estimate = fresh pull /
        // flat HP = fire — the Queen/Trick semantics).
        if (cfg.MoonFluteMinTtkSeconds > 0
            && _packTtk.EstimateTtkSeconds() is { } ttk
            && ttk < cfg.MoonFluteMinTtkSeconds)
            return;

        scheduler.PushGcd(ProteusAbilities.MoonFlute, player.GameObjectId, priority: 6,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = BLUActions.MoonFlute.Name;
                context.Debug.DamageState = "MOON FLUTE (burst window)";
            });
    }

    // ── Freeze→shatter ──────────────────────────────────────────────────────

    /// <summary>Returns true when the module is inside a freeze window and all other damage
    /// pushes must be suppressed this frame.</summary>
    private bool TryHandleFreezeShatter(IProteusContext context, RotationScheduler scheduler, bool isMoving)
    {
        var cfg = context.Configuration.BlueMage;
        if (!cfg.EnableFreezeShatter) return false;
        if (!context.IsSpellUsable(BLUActions.TheRamsVoice.ActionId)
            || !context.IsSpellUsable(BLUActions.Ultravibration.ActionId))
            return false;

        var player = context.Player;
        var now = UtcNow();
        var freezeRemaining = context.TargetingService.GetBestStatusRemainingOnAnyEnemy(
            [BLUActions.StatusIds.DeepFreeze], BLUActions.Ultravibration.Radius, player);

        // Window bookkeeping: immune detection + hard expiry.
        if (_freezeArmedUtc is { } armed)
        {
            var age = (now - armed).TotalSeconds;
            if (age > FreezeWindowMaxSeconds)
            {
                _freezeArmedUtc = null;
            }
            else if (age > FreezeImmuneGraceSeconds && freezeRemaining <= 0f)
            {
                // Ram's Voice landed but froze nothing — bosses/immune packs. Don't retry this combat.
                _packFreezeImmune = true;
                _freezeArmedUtc = null;
                context.Debug.DamageState = "Freeze→shatter: pack immune (no Deep Freeze landed)";
            }
        }

        var ultraReady = context.ActionService.GetCooldownRemaining(BLUActions.Ultravibration.ActionId) <= 0f;

        // Shatter: a frozen enemy is in the 6y radius with enough freeze left to survive the cast.
        if (ultraReady && freezeRemaining > ShatterMinFreezeRemaining && !isMoving)
        {
            scheduler.PushGcd(ProteusAbilities.Ultravibration, player.GameObjectId, priority: 5,
                onDispatched: _ =>
                {
                    _freezeArmedUtc = null;
                    context.Debug.PlannedAction = BLUActions.Ultravibration.Name;
                    context.Debug.DamageState = "ULTRAVIBRATION (shatter)";
                });
            context.Debug.DamageState = "Freeze→shatter: holding damage for the shatter";
            return true; // suppress everything else — damage breaks the freeze
        }

        // Mid-window (freeze pending or live) — keep suppressing even while the shatter can't
        // fire yet (moving / freeze just applied).
        if (_freezeArmedUtc is not null || (ultraReady && freezeRemaining > 0f))
        {
            context.Debug.DamageState = "Freeze→shatter: waiting (freeze pending)";
            return true;
        }

        // Start a window: enough targets in the freeze radius, shatter off cooldown, pack not immune.
        var packCount = context.TargetingService.CountEnemiesInRange(BLUActions.TheRamsVoice.Radius, player);
        if (!_packFreezeImmune
            && ultraReady
            && packCount >= cfg.UltravibrationMinTargets
            && !isMoving
            && !MechanicCastGate.ShouldBlock(context, BLUActions.TheRamsVoice.CastTime))
        {
            var capturedCount = packCount;
            scheduler.PushGcd(ProteusAbilities.TheRamsVoice, player.GameObjectId, priority: 6,
                onDispatched: _ =>
                {
                    _freezeArmedUtc = UtcNow();
                    context.Debug.PlannedAction = BLUActions.TheRamsVoice.Name;
                    context.Debug.DamageState = $"Ram's Voice (freezing {capturedCount})";
                });
            // Not suppressing yet — if Ram's Voice loses the dispatch race this frame the normal
            // chain still plays; suppression starts once the freeze is actually armed.
        }

        return false;
    }

    // ── oGCD weaves ─────────────────────────────────────────────────────────

    private void PushOffensiveOgcds(
        IProteusContext context, RotationScheduler scheduler,
        Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter player,
        Dalamud.Game.ClientState.Objects.Types.IBattleNpc target)
    {
        var cfg = context.Configuration.BlueMage;
        var targetDistance = System.Numerics.Vector3.Distance(player.Position, target.Position)
                             - target.HitboxRadius;

        // Surpanakha: dump all 4 charges strictly back-to-back. Start only at 4 charges; once the
        // fury stack is live the ONLY legal weave is the next Surpanakha (anything else drops it).
        var midDump = context.HasSurpanakhasFury;
        if (cfg.EnableSurpanakha
            && context.IsSpellUsable(BLUActions.Surpanakha.ActionId)
            && targetDistance <= BLUActions.Surpanakha.Radius
            && (midDump || context.ActionService.GetCurrentCharges(BLUActions.Surpanakha.ActionId) >= 4))
        {
            scheduler.PushOgcd(ProteusAbilities.Surpanakha, player.GameObjectId, priority: 1,
                onDispatched: _ => context.Debug.PlannedAction = BLUActions.Surpanakha.Name);
        }
        if (midDump) return; // never weave anything else through a Surpanakha dump

        if (!cfg.EnableOffensiveOgcds) return;

        // Feather Rain — ground-placed AT the target (30y reach). Cooldown gated by the scheduler.
        if (context.IsSpellUsable(BLUActions.FeatherRain.ActionId)
            && targetDistance <= BLUActions.FeatherRain.Range)
        {
            scheduler.PushGroundTargetedOgcd(ProteusAbilities.FeatherRain, target.Position, priority: 2,
                onDispatched: _ => context.Debug.PlannedAction = BLUActions.FeatherRain.Name);
        }

        // Glass Dance — self-anchored 12y.
        if (context.IsSpellUsable(BLUActions.GlassDance.ActionId)
            && targetDistance <= BLUActions.GlassDance.Radius)
        {
            scheduler.PushOgcd(ProteusAbilities.GlassDance, player.GameObjectId, priority: 3,
                onDispatched: _ => context.Debug.PlannedAction = BLUActions.GlassDance.Name);
        }

        // Both Ends — self-anchored 20y, the big 120s hit.
        if (context.IsSpellUsable(BLUActions.BothEnds.ActionId)
            && targetDistance <= BLUActions.BothEnds.Radius)
        {
            scheduler.PushOgcd(ProteusAbilities.BothEnds, player.GameObjectId, priority: 4,
                onDispatched: _ => context.Debug.PlannedAction = BLUActions.BothEnds.Name);
        }
    }

    // ── GCD chain ───────────────────────────────────────────────────────────

    private void PushGcdChain(
        IProteusContext context, RotationScheduler scheduler,
        Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter player,
        Dalamud.Game.ClientState.Objects.Types.IBattleNpc target,
        BlueMageConfig cfg, bool isMoving)
    {
        var now = UtcNow();
        var targetDistance = System.Numerics.Vector3.Distance(player.Position, target.Position)
                             - target.HitboxRadius;

        // Moon Flute trigger (opt-in, default OFF) — the window itself is just this chain buffed.
        TryPushMoonFlute(context, scheduler, cfg, player, isMoving);

        // ── DoT block: Mortal Flame (once per target) and Breath of Magic (60s upkeep), with a
        // Bristle snapshot in front when the boost isn't already armed. ──
        var needMortalFlame = false;
        if (cfg.EnableMortalFlame
            && context.IsSpellUsable(BLUActions.MortalFlame.ActionId)
            && targetDistance <= BLUActions.MortalFlame.Range)
        {
            if (context.HasWaxingNocturne)
            {
                // Inside the Flute window a recast REPLACES the snapshot with the buffed one —
                // do it once per target, then never touch a buffed snapshot again.
                needMortalFlame = !_mortalFlameBuffedTargets.Contains(target.GameObjectId);
            }
            else if (!BaseStatusHelper.HasStatus(target, BLUActions.StatusIds.MortalFlame))
            {
                // Once-latch: one attempt per target per retry window, so a wrong status id (3643
                // is sheet-verified but not yet field-verified) can never chain-cast.
                needMortalFlame = !_mortalFlameLatch.TryGetValue(target.GameObjectId, out var lastTry)
                                  || (now - lastTry).TotalSeconds > MortalFlameRetrySeconds;
            }
        }

        // Breath of Magic is a 10y self-anchored cone — refresh only on the CURRENT target (the
        // cone follows auto-face), never on an off-target FindEnemyNeedingDot pick it might miss.
        // Inside the Flute window the refresh threshold widens: a buffed re-snapshot beats an
        // unbuffed DoT with plenty of time left (unless it was JUST applied in this window).
        var bomRefreshThreshold = context.HasWaxingNocturne ? 45f : 5f;
        var needBreathOfMagic = cfg.EnableBreathOfMagic
            && context.IsSpellUsable(BLUActions.BreathOfMagic.ActionId)
            && targetDistance <= BLUActions.BreathOfMagic.Radius
            && BaseStatusHelper.GetStatusRemaining(target, BLUActions.StatusIds.BreathOfMagic) <= bomRefreshThreshold;

        if ((needMortalFlame || needBreathOfMagic) && !isMoving)
        {
            // Bristle first: +50% into the snapshot. Skipped when the boost is already armed.
            if (cfg.EnableBristle
                && !context.HasBoost
                && context.IsSpellUsable(BLUActions.Bristle.ActionId)
                && !MechanicCastGate.ShouldBlock(context, BLUActions.Bristle.CastTime))
            {
                scheduler.PushGcd(ProteusAbilities.Bristle, player.GameObjectId, priority: 7,
                    onDispatched: _ =>
                    {
                        context.Debug.PlannedAction = BLUActions.Bristle.Name;
                        context.Debug.DamageState = "Bristle (snapshotting DoT)";
                    });
            }

            if (needMortalFlame && !MechanicCastGate.ShouldBlock(context, BLUActions.MortalFlame.CastTime))
            {
                var capturedId = target.GameObjectId;
                var buffedCast = context.HasWaxingNocturne;
                scheduler.PushGcd(ProteusAbilities.MortalFlame, target.GameObjectId, priority: 8,
                    onDispatched: _ =>
                    {
                        _mortalFlameLatch[capturedId] = UtcNow();
                        if (buffedCast) _mortalFlameBuffedTargets.Add(capturedId);
                        context.Debug.PlannedAction = BLUActions.MortalFlame.Name;
                        context.Debug.DamageState = "Mortal Flame (permanent DoT)";
                    });
            }

            if (needBreathOfMagic && !MechanicCastGate.ShouldBlock(context, BLUActions.BreathOfMagic.CastTime))
            {
                scheduler.PushGcd(ProteusAbilities.BreathOfMagic, player.GameObjectId, priority: 9,
                    onDispatched: _ =>
                    {
                        context.Debug.PlannedAction = BLUActions.BreathOfMagic.Name;
                        context.Debug.DamageState = "Breath of Magic (DoT)";
                    });
            }
        }

        // Song of Torment — 30s Bleeding, FindEnemyNeedingDot pattern. NOTE: the duration read is
        // NOT source-aware, and Bleeding 1714 is shared with Nightbloom/Aetherial Spark — fine solo
        // (nothing else applies 1714 to mobs), but in a multi-BLU party another BLU's bleed will
        // suppress our refresh. Ownership tracking is the documented v3 (Coil) work item.
        if (cfg.EnableSongOfTorment
            && context.IsSpellUsable(BLUActions.SongOfTorment.ActionId)
            && !isMoving)
        {
            var dotTarget = context.TargetingService.FindEnemyNeedingDot(
                BLUActions.StatusIds.Bleeding, 3f, BLUActions.SongOfTorment.Range, player);
            if (dotTarget != null && !MechanicCastGate.ShouldBlock(context, BLUActions.SongOfTorment.CastTime))
            {
                scheduler.PushGcd(ProteusAbilities.SongOfTorment, dotTarget.GameObjectId, priority: 10,
                    onDispatched: _ =>
                    {
                        context.Debug.PlannedAction = BLUActions.SongOfTorment.Name;
                        context.Debug.DamageState = "Song of Torment (DoT)";
                    });
            }
        }

        // White Death — INSTANT 400p while Touch of Frost is up: the best filler AND the movement
        // filler (dispatch gate rejects it once the buff drops; ProcBuff double-gates it).
        if (cfg.EnableColdFog
            && context.HasTouchOfFrost
            && context.IsSpellUsable(BLUActions.WhiteDeath.ActionId))
        {
            scheduler.PushGcd(ProteusAbilities.WhiteDeath, target.GameObjectId, priority: 11,
                onDispatched: _ =>
                {
                    context.Debug.PlannedAction = BLUActions.WhiteDeath.Name;
                    context.Debug.DamageState = "White Death (Touch of Frost)";
                });
        }

        // Rose of Destruction — own 30s recast. GetCooldownRemaining, not IsActionReady (the
        // recast-group GCD lesson: IsActionReady reads not-ready mid-global).
        if (cfg.EnableRoseOfDestruction
            && context.IsSpellUsable(BLUActions.TheRoseOfDestruction.ActionId)
            && context.ActionService.GetCooldownRemaining(BLUActions.TheRoseOfDestruction.ActionId) <= 0f
            && !MechanicCastGate.ShouldBlock(context, BLUActions.TheRoseOfDestruction.CastTime)
            && !isMoving)
        {
            scheduler.PushGcd(ProteusAbilities.TheRoseOfDestruction, target.GameObjectId, priority: 12,
                onDispatched: _ =>
                {
                    context.Debug.PlannedAction = BLUActions.TheRoseOfDestruction.Name;
                    context.Debug.DamageState = "Rose of Destruction";
                });
        }

        // Matra Magic — 120s ST nuke (shares its recast group with Dragon Force/Angel's Snack).
        if (cfg.EnableMatraMagic
            && context.IsSpellUsable(BLUActions.MatraMagic.ActionId)
            && context.ActionService.GetCooldownRemaining(BLUActions.MatraMagic.ActionId) <= 0f
            && targetDistance <= BLUActions.MatraMagic.Range
            && !MechanicCastGate.ShouldBlock(context, BLUActions.MatraMagic.CastTime)
            && !isMoving)
        {
            scheduler.PushGcd(ProteusAbilities.MatraMagic, target.GameObjectId, priority: 13,
                onDispatched: _ =>
                {
                    context.Debug.PlannedAction = BLUActions.MatraMagic.Name;
                    context.Debug.DamageState = "Matra Magic";
                });
        }

        // Cold Fog — arm the White Death window when something aggroed is close enough to hit us
        // within the 5s conversion window. Wasted if nothing connects, so gate on real aggro.
        // Never inside a Flute window (a zero-damage cast in a +50% GCD).
        if (cfg.EnableColdFog
            && !context.HasWaxingNocturne
            && !context.HasTouchOfFrost
            && context.IsSpellUsable(BLUActions.ColdFog.ActionId)
            && context.ActionService.GetCooldownRemaining(BLUActions.ColdFog.ActionId) <= 0f
            && context.TargetingService.FindNearestAggroedEnemy(10f, player) != null
            && !MechanicCastGate.ShouldBlock(context, BLUActions.ColdFog.CastTime)
            && !isMoving)
        {
            scheduler.PushGcd(ProteusAbilities.ColdFog, player.GameObjectId, priority: 14,
                onDispatched: _ =>
                {
                    context.Debug.PlannedAction = BLUActions.ColdFog.Name;
                    context.Debug.DamageState = "Cold Fog (arming White Death)";
                });
        }

        // Bad Breath — once per pack: 8y self cone, big debuff spread. Once-marker = Malodorous
        // on the current target.
        var packCount = context.TargetingService.CountEnemiesInRange(BLUActions.Plaincracker.Radius, player);
        context.Debug.AoeRangeEnemies = packCount;

        if (cfg.EnableBadBreath
            && context.IsSpellUsable(BLUActions.BadBreath.ActionId)
            && packCount >= cfg.AoEMinTargets
            && targetDistance <= BLUActions.BadBreath.Radius
            && !BaseStatusHelper.HasStatus(target, BLUActions.StatusIds.Malodorous)
            && !MechanicCastGate.ShouldBlock(context, BLUActions.BadBreath.CastTime)
            && !isMoving)
        {
            scheduler.PushGcd(ProteusAbilities.BadBreath, player.GameObjectId, priority: 15,
                onDispatched: _ =>
                {
                    context.Debug.PlannedAction = BLUActions.BadBreath.Name;
                    context.Debug.DamageState = "Bad Breath (pack debuff)";
                });
        }

        // Plaincracker — point-blank SELF AoE: count MUST be player-anchored (the Dyskrasia rule).
        if (cfg.EnableAoERotation
            && packCount >= cfg.AoEMinTargets
            && context.IsSpellUsable(BLUActions.Plaincracker.ActionId)
            && !isMoving
            && !MechanicCastGate.ShouldBlock(context, BLUActions.Plaincracker.CastTime))
        {
            var capturedCount = packCount;
            scheduler.PushGcd(ProteusAbilities.Plaincracker, player.GameObjectId, priority: 16,
                onDispatched: _ =>
                {
                    context.Debug.PlannedAction = BLUActions.Plaincracker.Name;
                    context.Debug.DamageState = $"Plaincracker ({capturedCount} enemies)";
                });
        }

        // Tank filler: Goblin Punch — INSTANT, 3y. Also the movement filler for any role when in
        // melee reach (the only GCD castable on the move). Range-rejected at dispatch when far.
        if ((context.Role == BluRole.Tank || isMoving) && context.IsSpellUsable(BLUActions.GoblinPunch.ActionId))
        {
            scheduler.PushGcd(ProteusAbilities.GoblinPunch, target.GameObjectId, priority: 17,
                onDispatched: _ =>
                {
                    context.Debug.PlannedAction = BLUActions.GoblinPunch.Name;
                    context.Debug.DamageState = "Goblin Punch";
                });
        }

        // Primary filler: Sonic Boom (1.0s cast, 25y).
        if (!isMoving && !MechanicCastGate.ShouldBlock(context, BLUActions.SonicBoom.CastTime))
        {
            if (context.IsSpellUsable(BLUActions.SonicBoom.ActionId))
            {
                scheduler.PushGcd(ProteusAbilities.SonicBoom, target.GameObjectId, priority: 18,
                    onDispatched: _ =>
                    {
                        context.Debug.PlannedAction = BLUActions.SonicBoom.Name;
                        context.Debug.DamageState = "Sonic Boom";
                    });
            }

            // Fallback when Sonic Boom isn't slotted: the starter spell always exists.
            scheduler.PushGcd(ProteusAbilities.WaterCannon, target.GameObjectId, priority: 19,
                onDispatched: _ =>
                {
                    context.Debug.PlannedAction = BLUActions.WaterCannon.Name;
                    context.Debug.DamageState = "Water Cannon (fallback)";
                });
        }
    }
}
