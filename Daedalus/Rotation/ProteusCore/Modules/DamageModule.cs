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

    // Mortal Flame once-per-target latch. Status 3643 detection is FIELD-PROVEN (run 1: one cast,
    // stuck, never re-pushed) — the latch now only covers apply latency. It was 60s as a wrong-id
    // fuse, but run 5 showed the cost: a movement-INTERRUPTED cast latched at dispatch and locked
    // Mortal Flame out for a minute ("we skipped the rotation path"). 5s = latency guard + quick
    // retry for interrupted casts.
    private readonly Dictionary<ulong, DateTime> _mortalFlameLatch = new();
    private const float MortalFlameRetrySeconds = 5f;

    // Breath of Magic recent-cast latch: even if the cone misses (or the status read ever lies),
    // one target gets at most one cast per window — the first field run chain-cast it 12× when
    // the toon wasn't facing the mob. The facing gate is the fix; this is the fuse.
    private readonly Dictionary<ulong, DateTime> _breathOfMagicLatch = new();
    // 20s: run 3 recast BoM at +10s on the same target despite the status (sole sheet row 3712,
    // MF detection works, cone counted a hit — cause unresolved; suspect the cone hit a DIFFERENT
    // pack member). The wider latch caps the waste while the field question is open.
    private const float BreathOfMagicRelatchSeconds = 20f;

    // Bristle is a 1.0s cast — the Boost only exists at cast END. Run 4: Glass Dance weaved into
    // Bristle's post-cast tail (pushed while HasBoost was still false) and ate the boost before
    // Mortal Flame. Weaves hold from Bristle DISPATCH, not just from boost-up.
    private DateTime _bristleInFlightUntilUtc = DateTime.MinValue;

    /// <summary>Cold Fog's payoff is 15s of White Death — pointless on a pack that's about to die.</summary>
    private const float ColdFogMinTtkSeconds = 10f;

    // Targets that already carry a Moon-Flute-buffed Mortal Flame snapshot. During Waxing an
    // existing UNBUFFED Mortal Flame is deliberately recast once (a recast replaces the snapshot —
    // buffed beats unbuffed); a buffed one is never touched again.
    private readonly HashSet<ulong> _mortalFlameBuffedTargets = new();

    // Pack time-to-kill for the Moon Flute gate (the NIN Trick / MCH Queen lesson at 10× the
    // cost: a Flute on a dying pack burns 15s of Waning for nothing).
    private readonly PackTtkEstimator _packTtk = new();

    /// <summary>Deep Freeze must outlast Ultravibration's 2.0s cast to be shatterable.</summary>
    private const float ShatterMinFreezeRemaining = 2.2f;

    /// <summary>
    /// v3.6 fleet hold bound: Deep Freeze applies at 12s, so remaining &gt; 7s means the freeze is
    /// younger than 5s — non-owners hold damage only that long. If the owner hasn't shattered by
    /// then (Ultravibration on cooldown, owner dead/desynced), the fleet resumes damage rather
    /// than idling out the full 12s freeze on a shatter that isn't coming.
    /// </summary>
    private const float FleetShatterHoldFloorSeconds = 7f;

    /// <summary>No Deep Freeze this long after Ram's Voice dispatch → the pack is freeze-immune.</summary>
    private const float FreezeImmuneGraceSeconds = 4f;

    /// <summary>Hard cap on a freeze→shatter window (freeze lasts 12s from application).</summary>
    private const float FreezeWindowMaxSeconds = 12f;

    // ── v3.2 synced Moon Flute ──────────────────────────────────────────────
    /// <summary>Re-announce burst readiness at this cadence while pieces stay ready.</summary>
    private const float BurstReadyRebroadcastSeconds = 5f;

    /// <summary>How long after the burst signal (plus stagger delay) the Flute may still fire —
    /// shorter than Waxing's 15s so one signal can never double-fire a window.</summary>
    private const float SyncedFluteGraceSeconds = 12f;

    private DateTime _lastBurstReadySentUtc = DateTime.MinValue;

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

        // Multi-BLU owner election summary (v3.1) — static-backed, refreshed by the Plugin pump.
        context.Debug.Coordination = Daedalus.Services.Blu.BluCoordinationState.CoordinationActive
            ? Daedalus.Services.Blu.BluCoordinationState.Summary
            : "";

        // Feed the pack TTK estimate every combat frame (Moon Flute gate).
        _packTtk.Sample(
            context.TargetingService.SumEnemyCurrentHpInRange(FFXIVConstants.CasterTargetingRange, player),
            UtcNow());

        // ── Freeze→shatter window: while armed, Deep Freeze breaks on ANY damage — the only
        // legal push is the shatter itself (mudra-block pattern). ──
        if (TryHandleFreezeShatter(context, scheduler, isMoving))
            return;

        // GCD chain first — it decides whether a Bristle boost is armed for a DoT snapshot, in
        // which case the offensive weaves must hold too (BLU oGCDs are spells and would EAT the
        // boost — field log: Bristle ×3, Breath of Magic ×0, the boosts died on other casts).
        var holdWeavesForBoost = PushGcdChain(context, scheduler, player, target, cfg, isMoving);
        if (!holdWeavesForBoost)
            PushOffensiveOgcds(context, scheduler, player, target);
    }

    private void ResetCombatState()
    {
        _freezeArmedUtc = null;
        _packFreezeImmune = false;
        _wasInCombat = false;
        _mortalFlameLatch.Clear();
        _mortalFlameBuffedTargets.Clear();
        _breathOfMagicLatch.Clear();
        _bristleInFlightUntilUtc = DateTime.MinValue;
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

        // v3.2 synced windows: with ≥2 BLU on the bus, announce readiness (BurstReady) and wait
        // for the coordinated burst signal so every toon's window starts on the same tick. T13
        // splits the fleet into stagger groups — group B waits +30s after the signal so half the
        // party is never stuck in Waning (unable to re-raise Mighty Guard) at a Gigaflare push.
        if (Daedalus.Services.Blu.BluCoordinationState.CoordinationActive && cfg.SyncMoonFluteWithParty)
        {
            var now = UtcNow();
            if ((now - _lastBurstReadySentUtc).TotalSeconds >= BurstReadyRebroadcastSeconds)
            {
                _lastBurstReadySentUtc = now;
                Daedalus.Services.Blu.BluCoordinationState.SignalBurstReady?.Invoke();
            }

            var sinceSignal = Daedalus.Services.Blu.BluCoordinationState.SecondsSinceBurstFire(now);
            var delay = Daedalus.Services.Blu.BluCoordinationState.MoonFluteStaggerDelaySeconds;
            if (sinceSignal < delay)
            {
                context.Debug.DamageState =
                    $"Moon Flute: staggered start in {delay - sinceSignal:F0}s "
                    + $"(group {Daedalus.Services.Blu.BluCoordinationState.StaggerGroup})";
                return;
            }
            if (sinceSignal > delay + SyncedFluteGraceSeconds)
            {
                context.Debug.DamageState = "Moon Flute: ready — waiting for party burst signal";
                return;
            }
        }

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

        // v3.6 co-op roles: with 2+ BLU on the bus, only the elected ShatterOwner casts
        // Ultravibration and only the FreezeLead starts Ram's Voice — everyone else just honors
        // the damage-hold below whenever a nearby enemy carries Deep Freeze (local status read;
        // the frozen pack IS the signal, no LAN message involved).
        var coopActive = Daedalus.Services.Blu.BluCoordinationState.CoordinationActive;
        var mayShatter = !coopActive || Daedalus.Services.Blu.BluCoordinationState.IsShatterOwner;
        var mayFreeze = !coopActive || Daedalus.Services.Blu.BluCoordinationState.IsFreezeLead;

        // Shatter: a frozen enemy is in the 6y radius with enough freeze left to survive the cast.
        if (mayShatter && ultraReady && freezeRemaining > ShatterMinFreezeRemaining && !isMoving)
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
        // fire yet (moving / freeze just applied). Non-owners in a co-op fleet hold on the
        // freeze status alone: their own Ultravibration cooldown is irrelevant, the OWNER's
        // shatter is what the frozen pack is waiting for.
        if (_freezeArmedUtc is not null
            || (mayShatter && ultraReady && freezeRemaining > 0f)
            || (coopActive && !mayShatter && freezeRemaining > FleetShatterHoldFloorSeconds))
        {
            context.Debug.DamageState = coopActive && !mayShatter
                ? "Freeze→shatter: holding damage (fleet shatter incoming)"
                : "Freeze→shatter: waiting (freeze pending)";
            return true;
        }

        // Start a window: enough targets in the freeze radius, shatter off cooldown, pack not
        // immune — and in a co-op fleet, only the FreezeLead (simultaneous freezes waste GCDs
        // and Deep Freeze re-application builds resistance).
        var packCount = context.TargetingService.CountEnemiesInRange(BLUActions.TheRamsVoice.Radius, player);
        if (mayFreeze
            && !_packFreezeImmune
            && (ultraReady || (coopActive && mayFreeze != mayShatter))
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
            // True weave slot only: a press while the GCD is IDLE (combat open, stall) just delays
            // the next cast — a clip for 200p (field, run 7). Mid-roll presses ride free.
            && context.ActionService.GcdRemaining > 0.6f
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

    /// <summary>Returns true when a Bristle boost is armed for a pending DoT snapshot — the
    /// caller must then hold the offensive weaves too (they're spells; they'd consume it).</summary>
    private bool PushGcdChain(
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

        // ── v3.4 fleet Final Sting: an armed order from the coordinator. Stingers keep their
        // normal rotation until their staggered slot arrives, then the sting outranks everything.
        // Every stinger re-checks the boss each frame — dead boss = order cleared, later slots
        // never fire (they're the safety margin), everyone resumes. 3y melee: the dispatch
        // range-gate holds the cast until the toon is in reach (positioning is on the operator).
        if (Daedalus.Services.Blu.BluFleetStingCommand.TryGetMyOrder(now, out var stingTargetId, out var fireAtUtc, out var stingSlot))
        {
            var stingObj = context.ObjectTable.SearchById(stingTargetId);
            if (stingObj is not Dalamud.Game.ClientState.Objects.Types.IBattleChara stingBoss
                || stingBoss.IsDead || stingBoss.CurrentHp == 0)
            {
                Daedalus.Services.Blu.BluFleetStingCommand.Clear();
                context.Debug.DamageState = "Fleet sting: target down — order cleared";
            }
            else if (now < fireAtUtc)
            {
                context.Debug.DamageState = $"Fleet sting: slot {stingSlot} in {(fireAtUtc - now).TotalSeconds:F0}s";
            }
            else if (context.IsSpellUsable(BLUActions.FinalSting.ActionId)
                     && !BaseStatusHelper.HasStatus(player, BLUActions.StatusIds.BrushWithDeath)
                     && !isMoving
                     && !MechanicCastGate.ShouldBlock(context, BLUActions.FinalSting.CastTime))
            {
                scheduler.PushGcd(ProteusAbilities.FleetFinalSting, stingBoss.GameObjectId, priority: 3,
                    onDispatched: _ =>
                    {
                        context.Debug.PlannedAction = BLUActions.FinalSting.Name;
                        context.Debug.DamageState = $"FLEET STING (slot {stingSlot} — this kills us)";
                    });
            }
        }

        // Final Sting — solo execute: ~2000p, KILLS THE CASTER, 10min lockout (Brush with Death).
        // Only on the LAST engaged enemy (nobody fights on after you're dead) at/below the HP
        // threshold. 3y melee — dispatch range-rejects until the toon is in reach.
        if (cfg.EnableFinalSting
            && context.Role == BluRole.Solo
            && context.IsSpellUsable(BLUActions.FinalSting.ActionId)
            && !BaseStatusHelper.HasStatus(player, BLUActions.StatusIds.BrushWithDeath)
            && target.MaxHp > 0
            && (float)target.CurrentHp / target.MaxHp * 100f <= cfg.FinalStingTargetHpPercent
            && context.TargetingService.CountEngagedEnemies(30f, player) <= 1
            && !isMoving
            && !MechanicCastGate.ShouldBlock(context, BLUActions.FinalSting.CastTime))
        {
            scheduler.PushGcd(ProteusAbilities.FinalSting, target.GameObjectId, priority: 4,
                onDispatched: _ =>
                {
                    context.Debug.PlannedAction = BLUActions.FinalSting.Name;
                    context.Debug.DamageState = "FINAL STING (execute — this kills us)";
                });
        }

        // ── Missile chain: 50% of CURRENT HP per cast on death-vulnerable bosses (one shared
        // immunity flag across Missile/Tail Screw/Launcher/L5D/Ultravibration). Unknown enemies
        // get ONE probe — its observed outcome feeds the persistent death-immunity ledger, so
        // clearing dungeons on BLU builds the susceptibility list automatically. Immune bosses
        // fall through to the normal chain (and the Final Sting execute, if armed). Missile
        // outprioritizes the DoT block: halving early dwarfs everything, and Bristle never
        // fires while Missile is pushing, so the boost-hold can't collide with the chain.
        if (cfg.EnableMissileCheese
            && context.IsSpellUsable(BLUActions.Missile.ActionId)
            && PlayerSafetyHelper.IsInInstancedDuty()
            && target.MaxHp >= (uint)cfg.MissileMinTargetMaxHp
            && (float)target.CurrentHp / target.MaxHp * 100f >= cfg.MissileHpFloorPercent
            && context.DeathLedger?.GetVerdict(target.NameId) != Daedalus.Services.Blu.DeathImmunityVerdict.Immune
            && !MechanicCastGate.ShouldBlock(context, BLUActions.Missile.CastTime)
            && !isMoving)
        {
            var probeTarget = target;
            scheduler.PushGcd(ProteusAbilities.Missile, target.GameObjectId, priority: 6,
                onDispatched: _ =>
                {
                    context.DeathLedger?.NotifyProbeCast(
                        probeTarget.GameObjectId, probeTarget.NameId,
                        probeTarget.Name?.TextValue ?? "", probeTarget.MaxHp, probeTarget.CurrentHp);
                    context.Debug.PlannedAction = BLUActions.Missile.Name;
                    context.Debug.DamageState = "MISSILE (death cheese)";
                });
        }

        // ── DoT block: Mortal Flame (once per target) and Breath of Magic (60s upkeep), with a
        // Bristle snapshot in front when the boost isn't already armed. With ≥2 BLU on the bus
        // (v3.1) each DoT has exactly ONE elected owner — non-owners hard-skip so a second toon
        // can never clobber the owner's snapshot (any Mortal Flame recast REPLACES it, and the
        // bleed status 1714 is shared across the whole family).
        var coordActive = Daedalus.Services.Blu.BluCoordinationState.CoordinationActive;

        var needMortalFlame = false;
        if (cfg.EnableMortalFlame
            && (!coordActive || Daedalus.Services.Blu.BluCoordinationState.IsMortalFlameOwner)
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

        // Breath of Magic is a 10y self-anchored cone dispatched on SELF — the game never
        // auto-faces for it, so a wrongly-faced toon fires it into nothing (first field run:
        // 12 straight misses). Gate on ACTUAL facing; targeted casts (SoT/Sonic Boom/Mortal
        // Flame) rotate the toon when client auto-face is on, so a blocked frame self-heals.
        // Inside the Flute window the refresh threshold widens: a buffed re-snapshot beats an
        // unbuffed DoT with plenty of time left (unless it was JUST applied in this window).
        var bomRefreshThreshold = context.HasWaxingNocturne ? 45f : 5f;
        var bomLatched = _breathOfMagicLatch.TryGetValue(target.GameObjectId, out var bomLast)
                         && (now - bomLast).TotalSeconds < BreathOfMagicRelatchSeconds;
        var bomWanted = cfg.EnableBreathOfMagic
            && (!coordActive || Daedalus.Services.Blu.BluCoordinationState.IsBreathOfMagicOwner)
            && context.IsSpellUsable(BLUActions.BreathOfMagic.ActionId)
            && !bomLatched
            && targetDistance <= BLUActions.BreathOfMagic.Radius
            && BaseStatusHelper.GetStatusRemaining(target, BLUActions.StatusIds.BreathOfMagic) <= bomRefreshThreshold;
        var facingTarget = Helpers.ConeFacingHelper.IsFacing(player, target);
        var needBreathOfMagic = bomWanted && facingTarget;
        if (bomWanted && !facingTarget)
        {
            // ACTIVELY turn toward the target (throttled hard-target + rotation write). The cone
            // is self-cast, so no rejection ever fires the passive recovery — and with the boost
            // armed everything else is held, so nothing else turns us either: without this nudge
            // a BMR dodge parked the toon mis-faced for 8 dead seconds (field, run 6).
            context.ActionService.NotifyFacingRejection(target.GameObjectId);
            context.Debug.DamageState = "Breath of Magic: turning to face target";
        }

        // Song of Torment, coordinated (v3.1b owner rule): the BleedOwner is the ONLY toon that
        // casts the bleed family, and it refreshes with a Bristle snapshot — inside a Flute
        // window a buffed re-snapshot is worth taking early (≤25s), outside it only when the
        // bleed would otherwise drop (≤5s; an unbuffed refresh over a live buffed snapshot is
        // pure loss). Solo/single-BLU keeps the plain v1 refresh further below.
        Dalamud.Game.ClientState.Objects.Types.IBattleNpc? sotSnapshotTarget = null;
        if (coordActive
            && cfg.EnableSongOfTorment
            && Daedalus.Services.Blu.BluCoordinationState.IsBleedOwner
            && context.IsSpellUsable(BLUActions.SongOfTorment.ActionId)
            && !isMoving)
        {
            var sotThreshold = context.HasWaxingNocturne ? 25f : 5f;
            sotSnapshotTarget = context.TargetingService.FindEnemyNeedingDot(
                BLUActions.StatusIds.Bleeding, sotThreshold, BLUActions.SongOfTorment.Range, player);
        }
        var needSongOfTormentBuffed = sotSnapshotTarget != null;

        if ((needMortalFlame || needBreathOfMagic || needSongOfTormentBuffed) && !isMoving)
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
                        _bristleInFlightUntilUtc = UtcNow().AddSeconds(4); // cast + one GCD of cover
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
                var capturedBomTarget = target.GameObjectId;
                scheduler.PushGcd(ProteusAbilities.BreathOfMagic, player.GameObjectId, priority: 9,
                    onDispatched: _ =>
                    {
                        _breathOfMagicLatch[capturedBomTarget] = UtcNow();
                        context.Debug.PlannedAction = BLUActions.BreathOfMagic.Name;
                        context.Debug.DamageState = "Breath of Magic (DoT)";
                    });
            }

            if (needSongOfTormentBuffed
                && !MechanicCastGate.ShouldBlock(context, BLUActions.SongOfTorment.CastTime))
            {
                scheduler.PushGcd(ProteusAbilities.SongOfTorment, sotSnapshotTarget!.GameObjectId, priority: 10,
                    onDispatched: _ =>
                    {
                        context.Debug.PlannedAction = BLUActions.SongOfTorment.Name;
                        context.Debug.DamageState = "Song of Torment (owner snapshot)";
                    });
            }
        }

        // ── Boost hold: Bristle's +50% is consumed by the NEXT offensive spell, whatever it is —
        // while it's armed (or Bristle is still casting: the boost doesn't exist until cast END,
        // and a weave in the cast tail ate one in the field) for a DoT snapshot, every other
        // damage cast must wait. The DoT candidates above fire the moment facing/movement allow.
        var bristleInFlight = UtcNow() < _bristleInFlightUntilUtc;
        if ((context.HasBoost || bristleInFlight) && (bomWanted || needMortalFlame || needSongOfTormentBuffed))
        {
            if (!needBreathOfMagic && !needMortalFlame && !needSongOfTormentBuffed)
                context.Debug.DamageState = "Holding damage (Bristle boost armed, waiting to face/stand)";
            return true; // caller holds the offensive weaves too
        }

        // Song of Torment, solo/single-BLU — 30s Bleeding, FindEnemyNeedingDot pattern. NOTE: the
        // duration read is NOT source-aware, and Bleeding 1714 is shared with Nightbloom/Aetherial
        // Spark — fine solo (nothing else applies 1714 to mobs). With ≥2 BLU on the bus this path
        // is replaced by the owner-snapshot rule above; non-owners never cast the bleed family.
        if (!coordActive
            && cfg.EnableSongOfTorment
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
        // within the 5s conversion window. Wasted if nothing connects, so gate on real aggro AND
        // on the pack living long enough to spend the 15s payoff (first field run burned the 90s
        // cooldown on a mob that died 2s later). Never inside a Flute window.
        if (cfg.EnableColdFog
            && !context.HasWaxingNocturne
            && !context.HasTouchOfFrost
            && context.IsSpellUsable(BLUActions.ColdFog.ActionId)
            && context.ActionService.GetCooldownRemaining(BLUActions.ColdFog.ActionId) <= 0f
            && context.TargetingService.FindNearestAggroedEnemy(10f, player) != null
            && !(_packTtk.EstimateTtkSeconds() is { } coldFogTtk && coldFogTtk < ColdFogMinTtkSeconds)
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
            && facingTarget // 8y self cone — same no-auto-face trap as Breath of Magic
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

        // Tank/Solo filler: Goblin Punch — INSTANT, 3y, and 400p while Mighty Guard is up (the
        // solo BI+MG combo makes it the best melee filler). Also the movement filler for any
        // role in melee reach. Range-rejected at dispatch when far.
        if ((context.Role == BluRole.Tank || context.Role == BluRole.Solo || isMoving)
            && context.IsSpellUsable(BLUActions.GoblinPunch.ActionId))
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

        return false; // no boost hold — weaves may flow
    }
}
