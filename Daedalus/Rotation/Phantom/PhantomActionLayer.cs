using System;
using System.Collections.Generic;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using Daedalus.Data;
using Daedalus.Rotation.Common;
using Daedalus.Rotation.Common.Scheduling;
using Daedalus.Services;
using Daedalus.Services.Action;
using Daedalus.Services.Occult;
using Daedalus.Timeline;
using FFXIVClientStructs.FFXIV.Client.Game.Event;

namespace Daedalus.Rotation.Phantom;

/// <summary>
/// Occult Crescent phantom duty-action executor (Phase 3 of docs/occult-phantom-plan.md:
/// survival / mitigation / interrupt / MP / party-buff bands — no damage band yet).
///
/// One shared instance runs from BaseRotation.ExecuteInternal AFTER the job's own
/// modules, so it only ever consumes leftover GCD/weave capacity — the main rotation
/// always outranks it. Its own scheduler enforces the standard dispatch gates; every
/// push is additionally gated on phantom level, duty-bar slot presence (fail closed)
/// and the rotation-lockout status list (never stomp a burst/combo window).
/// </summary>
public sealed class PhantomActionLayer
{
    // Ascending scheduler priorities within the layer's own queues.
    private const int PrioEmergencySustain = 10;
    private const int PrioRaise = 15;
    private const int PrioSelfMit = 20;
    private const int PrioInterrupt = 30;
    private const int PrioMpRestore = 40;
    private const int PrioPartyBuff = 50;
    private const int PrioDamage = 300;

    private const float InterruptMeleeRangeYalms = 5f;
    private const float RangeBufferYalms = 0.5f;

    // Raise wiring — mirrors VariantActionLayer, which solved the same problem.
    private const ushort RaisePendingStatusId = 148;
    private const float RaiseRangeSquared = 900f; // 30y cast range
    private const int RaiseCastMs = 8000;

    /// <summary>Chemist's Revive — a hard cast, phantom Lv3.</summary>
    private const uint ChemistReviveId = 41634;

    /// <summary>Phantom White Mage's Occult Raise — INSTANT, and works under Resurrection Restricted.</summary>
    private const uint OccultRaiseId = 49070;

    private readonly ActionService _actionService;
    private readonly Configuration _configuration;
    private readonly PhantomJobService _phantomJobs;
    private readonly IBurstWindowService? _burstWindows;
    private readonly RotationScheduler _scheduler;
    private readonly IPluginLog _log;

    private readonly Dictionary<uint, AbilityBehavior> _behaviorCache = [];
    private readonly List<string> _pushRejects = [];

    /// <summary>
    /// Deliberate, healthy reasons an action was not pushed — as opposed to <see cref="_pushRejects"/>,
    /// which are things standing in the way. Kept apart so a party buff that is simply still up
    /// never reads as "blocked", and reported only when nothing is actually blocked.
    ///
    /// <para>
    /// Exists because "Bard is not casting Offensive Aria" was undiagnosable: holding a buff that
    /// is already running returned silently, so the Duty tab said "idle — nothing eligible" and
    /// gave no way to tell a held buff from an unslotted action or a broken id.
    /// </para>
    /// </summary>
    private readonly List<string> _pushHolds = [];

    /// <summary>
    /// When each corpse was first seen dead, so the phantom raise can stop deferring to a living
    /// healer that is not actually acting. Cleared as bodies get up or leave.
    /// </summary>
    private readonly Dictionary<ulong, DateTime> _deadSince = [];
    private bool _dispatchedThisFrame;

    /// <summary>
    /// A raise went into the queue this frame, so it may pre-empt a job weave instead of waiting
    /// for leftovers. Everything else the layer queues still yields to the job.
    /// </summary>
    private bool _raiseQueuedThisFrame;

    /// <summary>Set per frame: a buff is up whose GCD should not be spent on a phantom action.</summary>
    private uint? _gcdHoldStatusThisFrame;

    /// <summary>Phantom Red Mage's Dualcast is up: the next phantom spell is instant.</summary>
    private bool _dualcastThisFrame;

    /// <summary>Set per frame — TryPush needs config but is not handed it.</summary>
    private Config.PhantomConfig? _configThisFrame;

    /// <summary>Who the queued raise is aimed at, so the dispatch can pre-face them.</summary>
    private ulong _raiseTargetIdThisFrame;

    /// <summary>
    /// Consecutive pre-modules checks where a raise sat queued but the GCD was sampled busy.
    /// A handful is normal (Enpi rolling); an ever-growing streak means the window is NEVER
    /// sampled open from this layer, which is a finding in itself.
    /// </summary>
    private int _raiseGcdBusySamples;
    private bool _framePrepared;
    private bool _isMovingThisFrame;

    public PhantomActionLayer(
        ActionService actionService,
        IJobGauges jobGauges,
        Configuration configuration,
        PhantomJobService phantomJobs,
        ITimelineService? timelineService,
        IErrorMetricsService? errorMetrics,
        IPluginLog log,
        IBurstWindowService? burstWindows = null)
    {
        _actionService = actionService;
        _configuration = configuration;
        _phantomJobs = phantomJobs;
        _burstWindows = burstWindows;
        _log = log;
        _scheduler = new RotationScheduler(actionService, jobGauges, configuration, timelineService, errorMetrics);
    }

    /// <summary>
    /// Runs BEFORE the job's modules: collects every band and pre-empts the GCD window
    /// for phantom GCDs (damage GCDs and emergency heal GCDs would otherwise starve —
    /// the job rotation wins every window; RSR checks duty actions first the same way).
    /// Never throws.
    /// </summary>
    public void ExecutePreModules(IRotationContext ctx, bool isMoving, bool inCombat)
    {
        try
        {
            _framePrepared = false;
            PreModulesCore(ctx, isMoving, inCombat);
        }
        catch (Exception ex)
        {
            // Surface the fault. Without this the layer silently does nothing and the Duty tab
            // keeps its stale "idle — nothing eligible", because a throw here leaves
            // _framePrepared false and the post-modules pass returns before updating the status.
            // An exception reading as "nothing to do" has already cost real debugging time.
            ReportFault("pre-modules", ex);
        }
    }

    /// <summary>
    /// Runs AFTER the job's modules: dispatches queued phantom oGCDs into leftover
    /// weave slots (job weaves outrank phantom weaves). Never throws.
    /// </summary>
    public void ExecutePostModules(IRotationContext ctx, bool isMoving, bool inCombat)
    {
        try
        {
            if (!_framePrepared)
                return;

            // Live ActionService state — the context's CanExecute flags are frozen at
            // context creation, before the job's modules consumed their slots.
            if (_actionService.CanExecuteOgcd)
                _scheduler.DispatchOgcd(ctx);
            if (_actionService.CanExecuteGcd)
                _scheduler.DispatchGcd(ctx);

            // Name the queued action and WHICH capability refused it. "no free slot" was true
            // but useless: a raise sat queued for minutes and the line could not distinguish a
            // busy GCD from a full weave window from a scheduler-level rejection.
            var gcdQueue = _scheduler.InspectGcdQueue();
            var ogcdQueue = _scheduler.InspectOgcdQueue();
            var queued = gcdQueue.Count + ogcdQueue.Count;

            _phantomJobs.LayerLastEvent = _dispatchedThisFrame
                ? "dispatched"
                : queued > 0
                    ? DescribeQueueStall(gcdQueue.Count, ogcdQueue.Count)
                    : _pushRejects.Count > 0
                        ? $"blocked — {_pushRejects[0]}"
                        : _pushHolds.Count > 0
                            ? $"holding — {_pushHolds[0]}"
                            : "idle — nothing eligible";

            // The one-line summary only ever names the FIRST reason, which is useless when the
            // question is "why has nothing fired for four minutes" — the answer is usually the
            // combination, not the first entry. Publish the whole set for the Duty tab.
            _phantomJobs.LayerBlockedReasons = [.. _pushRejects];
            _phantomJobs.LayerHoldReasons = [.. _pushHolds];
            _phantomJobs.LayerIsMoving = _isMovingThisFrame;
            _phantomJobs.LayerDualcast = _dualcastThisFrame;
        }
        catch (Exception ex)
        {
            ReportFault("post-modules", ex);
        }
    }

    /// <summary>
    /// Why a queued phantom action is not going out. Distinguishes "the GCD is rolling" from
    /// "no weave slot" from "the scheduler refused it", which the old single message conflated.
    /// </summary>
    private string DescribeQueueStall(int gcdQueued, int ogcdQueued)
    {
        if (gcdQueued > 0 && !_actionService.CanExecuteGcd)
            return $"waiting — {gcdQueued} GCD queued, GCD not ready";
        if (ogcdQueued > 0 && !_actionService.CanExecuteOgcd)
            return $"waiting — {ogcdQueued} oGCD queued, no weave slot";

        // Capability said yes and the dispatch still did not happen, so the scheduler itself
        // rejected it — range, line of sight, facing, or an action-level gate.
        return $"waiting — {gcdQueued + ogcdQueued} queued, scheduler refused (range/LoS/facing?)";
    }

    /// <summary>
    /// Put a fault where it can be seen. The Debug Log gets the stack trace; the Duty tab gets
    /// enough to know the layer is broken rather than idle.
    /// </summary>
    private void ReportFault(string stage, Exception ex)
    {
        _log.Warning(ex, $"PhantomActionLayer: {stage} failed");
        _phantomJobs.LayerLastEvent = $"FAULTED in {stage} — {ex.GetType().Name}: {ex.Message}";
    }

    private void PreModulesCore(IRotationContext ctx, bool isMoving, bool inCombat)
    {
        var cfg = _configuration.Occult;

        // Say which. These two used to return silently, leaving _framePrepared false so the
        // post pass never updated the status either — the Duty tab kept whatever it last said,
        // or "—" if the layer had never run. An inert layer read exactly like a working one,
        // which is the same trap the fault handler above was added for.
        if (!cfg.EnablePhantomActions)
        {
            _phantomJobs.LayerLastEvent = "off — phantom actions disabled in settings";
            return;
        }

        if (!_phantomJobs.IsInOccultCrescent)
        {
            _phantomJobs.LayerLastEvent = "off — not in the Occult Crescent";
            return;
        }

        var (job, level) = _phantomJobs.GetActiveJob();
        if (job == PhantomJob.None || level == 0)
        {
            _phantomJobs.LayerLastEvent = "held — no phantom job";
            return;
        }

        if (inCombat && FindLockoutStatus() is { } lockoutId)
        {
            _phantomJobs.LayerLastEvent = $"held — lockout status {lockoutId}";
            return;
        }

        // Buff-preservation statuses are NOT locks: they only mean "don't spend a GCD elsewhere".
        // oGCDs (Occult Libra above all) must still fire, or a Warrior's Inner Release silently
        // stops the weakness table improving for 15s of every minute.
        _gcdHoldStatusThisFrame = inCombat ? FindGcdHoldStatus() : null;

        // Phantom Red Mage Lv.5 trait: any cast-time spell makes the NEXT spell instant for 15s.
        // The catch is the cancel clause — "canceled upon execution of any action other than an
        // ability" — so on a main job whose GCDs are weaponskills, the very next auto-rotation
        // GCD destroys it about 2.5s later. Unless something spends it deliberately it is simply
        // lost, which is exactly what the field report was: cast a heal, get the buff, watch it go.
        //
        // NOT applied on an actual Red Mage main job: that job's Dualcast belongs to its own
        // rotation, and telling the status rows apart is less reliable than just standing back.
        _dualcastThisFrame = job == PhantomJob.PhantomRedMage
            && ctx.Player.ClassJob.RowId != Daedalus.Data.JobRegistry.RedMage
            && HasAnyStatus(ctx.Player, PhantomActions.DualcastStatusIds);

        var player = ctx.Player;
        var selfHpPct = player.MaxHp > 0 ? (float)player.CurrentHp / player.MaxHp : 1f;

        _scheduler.Reset();
        _dispatchedThisFrame = false;
        _raiseQueuedThisFrame = false;
        _isMovingThisFrame = isMoving;
        _configThisFrame = cfg;
        _pushRejects.Clear();
        _pushHolds.Clear();
        _framePrepared = true;

        PushSurvival(ctx, cfg, job, level, selfHpPct, inCombat);
        PushSelfMit(ctx, job, level, selfHpPct, inCombat);
        PushInterrupts(ctx, job, level, inCombat);
        PushMpRestore(ctx, cfg, job, level, inCombat);
        PushPhantomRaise(ctx, cfg, job, level);
        PushPartyBuffs(ctx, job, level, inCombat);
        PushDamage(ctx, cfg, job, level, inCombat);
        PushStateMachines(ctx, cfg, job, level, selfHpPct, inCombat);

        // Weave pre-empt for a RAISE only. Phantom oGCDs normally wait for whatever weave slots
        // the job leaves over, which is right for mitigation and buffs and wrong for a raise:
        // field 2026-08-02 showed "raising Rosa Discord (instant)" sat at "1 queued, no free
        // slot" because the job's own weaves took every slot first. A body on the floor beats
        // any single job weave, and the Occult death timer does not wait for one to come free.
        if (_raiseQueuedThisFrame && _actionService.CanExecuteOgcd)
        {
            var prevOgcd = ctx.TargetingService.SwapHardTargetForSubmit(_raiseTargetIdThisFrame);
            _actionService.FaceTarget(_raiseTargetIdThisFrame);
            _scheduler.DispatchOgcd(ctx);
            ctx.TargetingService.RestoreHardTargetAfterSubmit(prevOgcd, _raiseTargetIdThisFrame);
        }

        // GCD pre-empt: phantom GCDs (emergency heals first, then damage) claim the GCD
        // window before the job's filler. Big phantom cooldowns pace this to ~1 GCD per
        // 30-60s. The oGCD queue waits for post-modules leftover weave slots.
        //
        // ...but NOT while a body is waiting on a raise. Raise is a GCD, and this pre-empt runs
        // before the job's modules, so a phantom heal or nuke taking the window stops a healer
        // ever casting it. Field 2026-08-02: Sage raises worked everywhere EXCEPT the Horns —
        // which is exactly where this layer runs. A phantom cast is worth a fraction of getting
        // a player back on their feet, so the window goes to the job.
        // ...unless the thing WE queued is itself the raise. Occult Raise is ActionCategory 2
        // (Spell), so despite being instant-cast it goes in the GCD queue — meaning the yield
        // above, added to stop phantom casts starving a healer's Raise, was starving the
        // phantom's OWN raise instead. Field 2026-08-02: "raising Rosa Discord (instant)" stuck
        // at "1 queued, no free slot" while a body lay in front of it.
        if (!_raiseQueuedThisFrame)
            _raiseGcdBusySamples = 0;

        if (_actionService.CanExecuteGcd && (_raiseQueuedThisFrame || !RaisePendingForJob(ctx)))
        {
            // Face the corpse in the instant before our own submit — the only slot where it can
            // stick. Client auto-face turns you toward your HARD target (the enemy), so an
            // ally-targeted raise pre-fails facing, and post-failure recovery loses the race:
            // the job's next GCD auto-faces you straight back within the same frame. Field
            // 2026-08-02 — the raise failed the facing/LoS gate on every attempt while the
            // recovery and Enpi fought over the character's rotation.
            // Swap-fire-restore, all inside this frame before the job's modules run. Rotation
            // write alone did NOT beat the facing gate in the field, and the recovery hook
            // cannot help: EnsureHardTarget resolves through the enemy filters, which silently
            // drop a corpse — so the game's auto-face never had the right target to work with.
            // The swap gives it one for exactly the duration of the submit.
            var prevTarget = 0ul;
            if (_raiseQueuedThisFrame)
            {
                prevTarget = ctx.TargetingService.SwapHardTargetForSubmit(_raiseTargetIdThisFrame);
                _actionService.FaceTarget(_raiseTargetIdThisFrame);
            }

            var gcdResult = _scheduler.DispatchGcd(ctx);

            if (_raiseQueuedThisFrame)
                ctx.TargetingService.RestoreHardTargetAfterSubmit(prevTarget, _raiseTargetIdThisFrame);

            // The post-modules stall line reads "GCD not ready" almost tautologically — by then
            // the job has just consumed the window. THIS is the attempt that matters: the raise
            // gets first crack here, and if the scheduler rejects it, it said why. Stop
            // discarding the answer.
            if (_raiseQueuedThisFrame)
                ReportRaiseDispatchOutcome(gcdResult);
        }
        else if (_raiseQueuedThisFrame)
        {
            _raiseGcdBusySamples++;
            _phantomJobs.RaiseState += $" — GCD busy at check ({_raiseGcdBusySamples} in a row)";
        }
    }

    private void PushSurvival(IRotationContext ctx, Config.PhantomConfig cfg, PhantomJob job, byte level, float selfHpPct, bool inCombat)
    {
        var potionCount = _phantomJobs.GetItemCount(PhantomJobData.OccultPotionItemId);

        if (job == PhantomJob.Chemist)
        {
            if (PhantomBandRules.ShouldUsePotion(cfg, selfHpPct, potionCount, inCombat))
                TryPush(ctx, 41631, job, level, PrioEmergencySustain);

            var elixirCount = _phantomJobs.GetItemCount(PhantomJobData.OccultElixirItemId);
            if (PhantomBandRules.ShouldUseElixir(cfg, selfHpPct, elixirCount, inCombat))
                TryPush(ctx, 41635, job, level, PrioEmergencySustain + 1);
        }

        if (job == PhantomJob.Monk && PhantomBandRules.ShouldUseChakraForHp(cfg, selfHpPct, inCombat))
            TryPush(ctx, 41598, job, level, PrioEmergencySustain + 2);

        if (job == PhantomJob.Freelancer && PhantomBandRules.ShouldResuscitate(cfg, selfHpPct))
            TryPush(ctx, 41650, job, level, PrioEmergencySustain);

        // Pledge (Knight): a genuine invulnerability, so it leads everything else in the band —
        // an invuln that fires after the heal has already failed to save you is worthless.
        if (job == PhantomJob.Knight && PhantomBandRules.ShouldPledge(cfg, selfHpPct, inCombat))
            TryPush(ctx, 41591, job, level, PrioEmergencySustain - 1, ctx.Player.GameObjectId);

        // Occult Heal (Knight): the job's actual heal, and it was wired to no band at all —
        // field 2026-08-11, a Lv.4 Knight with it slotted never healed once. Instant oGCD, 5s
        // recast, self-targeted here. Leads Pray because Pray is a WEAPONSKILL: this one costs a
        // weave slot, Pray costs a GCD, which is why Pray stays opt-in behind its own toggle.
        if (job == PhantomJob.Knight && PhantomBandRules.ShouldOccultHeal(cfg, selfHpPct, inCombat))
            TryPush(ctx, 41590, job, level, PrioEmergencySustain + 2, ctx.Player.GameObjectId);

        if (job == PhantomJob.Knight && PhantomBandRules.ShouldPray(cfg, selfHpPct))
            TryPush(ctx, 41589, job, level, PrioEmergencySustain + 3);

        // Occult Cure II: 40,000 cure potency for 1,500 MP on a 2.5s recast — by far the
        // strongest self-heal in the phantom kits, so it is worth the GCD it costs. Red Mage
        // and White Mage each have their own copy (49093 / 49067 — the sheet really does
        // carry the name twice).
        if (PhantomBandRules.ShouldOccultCure(cfg, selfHpPct, inCombat))
        {
            if (job == PhantomJob.PhantomRedMage)
                TryPush(ctx, 49093, job, level, PrioEmergencySustain + 1, ctx.Player.GameObjectId);
            else if (job == PhantomJob.PhantomWhiteMage)
                TryPush(ctx, 49067, job, level, PrioEmergencySustain + 1, ctx.Player.GameObjectId);
        }

        // Occult Cure III (White Mage): 30,000 cure in a 15y AoE around the caster. One hurt
        // body is Cure II's job — this waits for two injured AND a real dent in the party
        // average, so the 3,000 MP buys more than a single-target heal would have.
        if (job == PhantomJob.PhantomWhiteMage
            && PhantomBandRules.ShouldOccultCureIII(
                ctx.PartyHealthMetrics.avgHpPercent, ctx.PartyHealthMetrics.injuredCount, inCombat))
        {
            TryPush(ctx, 49068, job, level, PrioEmergencySustain + 2, ctx.Player.GameObjectId);
        }

        // Earthen Wall (Summoner): a 40,000-potency barrier over the whole party on a 120s
        // timer. Party-wide, so it uses the self-mit threshold rather than a heal threshold.
        if (job == PhantomJob.PhantomSummoner && PhantomBandRules.ShouldSelfMit(selfHpPct, inCombat))
            TryPush(ctx, 49082, job, level, PrioSelfMit);

        // Phantom Ninja defensives (both Abilities). Image nullifies most PHYSICAL attacks for
        // 30s on a 120s timer — save it for real trouble. Smoke is +20% evasion for 90s on a
        // 5s recast, so it is simply kept up whenever it has lapsed.
        if (job == PhantomJob.PhantomNinja && inCombat)
        {
            if (PhantomBandRules.ShouldSelfMit(selfHpPct, inCombat))
                TryPush(ctx, 49066, job, level, PrioSelfMit);
            if (!_actionService.PlayerHasStatus(PhantomActions.StatusIds.Smoke))
                TryPush(ctx, 49063, job, level, PrioSelfMit + 1);
        }

        // Occult White Wind (Blue Mage): heals self and nearby party for the caster's CURRENT
        // HP. So it is worth MORE the healthier the caster is — the trigger is the PARTY being
        // hurt, with only a self floor so the copied amount is worth the 150s recast. Gating on
        // the caster's own band left a full-HP Blue Mage watching a dying party.
        if (job == PhantomJob.PhantomBlueMage
            && PhantomBandRules.ShouldWhiteWind(ctx.PartyHealthMetrics.avgHpPercent, selfHpPct, inCombat))
        {
            TryPush(ctx, 49090, job, level, PrioEmergencySustain + 1);
        }
    }

    private void PushSelfMit(IRotationContext ctx, PhantomJob job, byte level, float selfHpPct, bool inCombat)
    {
        if (!PhantomBandRules.ShouldSelfMit(selfHpPct, inCombat))
            return;

        if (job == PhantomJob.Knight)
            TryPush(ctx, 41588, job, level, PrioSelfMit); // Phantom Guard
        if (job == PhantomJob.Gladiator)
            TryPush(ctx, 46595, job, level, PrioSelfMit); // Defend

        // Occult Mighty Guard (Blue Mage): 20% off self AND nearby party for 15s on a 120s
        // recast. Cheap enough to treat as a self-mit rather than hoarding it for a raidwide
        // we cannot see coming — the layer has no timeline awareness here.
        if (job == PhantomJob.PhantomBlueMage && inCombat && selfHpPct < PhantomBandRules.SelfMitHpPct)
            TryPush(ctx, 49088, job, level, PrioSelfMit);
    }

    private void PushInterrupts(IRotationContext ctx, PhantomJob job, byte level, bool inCombat)
    {
        if (!inCombat)
            return;
        if (ctx.Player.TargetObject is not IBattleChara target || target.IsDead)
            return;

        var distance = System.Numerics.Vector3.Distance(ctx.Player.Position, target.Position)
                       - target.HitboxRadius;

        if (job == PhantomJob.Samurai
            && PhantomBandRules.ShouldInterrupt(target.IsCasting, target.IsCastInterruptible, distance, InterruptMeleeRangeYalms))
            TryPush(ctx, 41603, job, level, PrioInterrupt, target.GameObjectId); // Mineuchi

        if (job == PhantomJob.Bard
            && PhantomBandRules.ShouldInterrupt(target.IsCasting, target.IsCastInterruptible, distance, 25f))
            TryPush(ctx, 41609, job, level, PrioInterrupt, target.GameObjectId); // Romeo's Ballad
    }

    private void PushMpRestore(IRotationContext ctx, Config.PhantomConfig cfg, PhantomJob job, byte level, bool inCombat)
    {
        var player = ctx.Player;

        if (job == PhantomJob.Monk
            && PhantomBandRules.ShouldUseChakraForMp(cfg, player.CurrentMp, player.MaxMp, inCombat))
            TryPush(ctx, 41598, job, level, PrioMpRestore);

        if (job == PhantomJob.Chemist)
        {
            var potionCount = _phantomJobs.GetItemCount(PhantomJobData.OccultPotionItemId);
            if (PhantomBandRules.ShouldUseEther(cfg, player.CurrentMp, player.MaxMp, potionCount, inCombat))
                TryPush(ctx, 41633, job, level, PrioMpRestore + 1);
        }
    }

    /// <summary>
    /// Raise with the phantom job's own spell. Independent of Swiftcast, which is what makes it
    /// worth having alongside a real healer: a healer with Swiftcast down may not reach a corpse
    /// before the Occult death timer returns it to base.
    /// </summary>
    private void PushPhantomRaise(IRotationContext ctx, Config.PhantomConfig cfg, PhantomJob job, byte level)
    {
        var raiseId = job switch
        {
            PhantomJob.Chemist => ChemistReviveId,
            PhantomJob.PhantomWhiteMage => OccultRaiseId,
            _ => 0u,
        };
        if (raiseId == 0u)
        {
            _phantomJobs.RaiseState = "job has no raise";
            return;
        }

        if (!cfg.UsePhantomRaise)
        {
            _phantomJobs.RaiseState = "disabled in settings";
            return;
        }

        var (deadHealer, deadOther, livingHealer) = ScanPartyForRaise(ctx);

        // Occult Raise is INSTANT (Cast100ms 0) though it is ActionCategory 2 and so occupies
        // the GCD — an earlier note here claimed it was an oGCD costing only a weave, which was
        // wrong. It still should not wait: the cast is instantaneous, the recast is 5s, and the
        // Occult death timer can return a body to base inside any grace period. Chemist's Revive
        // is a genuine hardcast and does compete, so that one still waits its turn.
        var instantRaise = raiseId == OccultRaiseId;
        if (instantRaise)
        {
            livingHealer = false;
        }
        else if (livingHealer && deadOther is not null
            && SecondsDown(deadOther.GameObjectId) > PhantomBandRules.LivingHealerGraceSeconds)
        {
            livingHealer = false;
            _pushRejects.Add("healer has not raised in time — stepping in");
        }

        var decision = PhantomBandRules.DecideRaise(cfg, deadHealer != null, deadOther != null, livingHealer);
        if (decision == PhantomRaiseDecision.None)
        {
            _phantomJobs.RaiseState = deadHealer is null && deadOther is null
                ? DescribeNoRaiseTarget(ctx)
                : $"holding — waiting on the healer ({SecondsDown(deadOther!.GameObjectId):F0}s)";
            return;
        }

        var target = decision == PhantomRaiseDecision.RaiseHealer ? deadHealer! : deadOther!;
        var targetId = (uint)target.GameObjectId;

        // Shared raise buffer: never double-cast a corpse another toon is already raising.
        if (PartyCoordination?.IsRaiseTargetReservedByOther(targetId) == true)
        {
            _pushRejects.Add("raise target reserved by another toon");
            _phantomJobs.RaiseState = $"reserved by another toon — {target.Name?.TextValue}";
            return;
        }

        // Occult Raise is instant, so it reserves for a moment rather than a full cast.
        var castMs = raiseId == OccultRaiseId ? 0 : RaiseCastMs;

        _raiseTargetIdThisFrame = targetId;

        // Take the flag from what the push actually DID. Setting it true beforehand and never
        // correcting it meant a raise refused on cooldown still counted as pending, so the
        // GCD-busy streak climbed for the whole cooldown and read as a stalled raise — while the
        // cast had in fact just gone out. Field 2026-08-09: "Occult Raise on cooldown" and
        // "GCD busy at check (14 in a row)" on screen at the same time, which cannot both be true.
        _raiseQueuedThisFrame = TryPush(ctx, raiseId, job, level, PrioRaise, target.GameObjectId, target,
            onExtraDispatched: () =>
                PartyCoordination?.ReserveRaiseTarget(targetId, raiseId, castMs, usingSwiftcast: false));

        var who = target.Name?.TextValue ?? "ally";
        _phantomJobs.RaiseState = _raiseQueuedThisFrame
            ? $"raising {who}{(instantRaise ? " (instant)" : string.Empty)}"
            // TryPush already recorded exactly why; repeat it here rather than inventing a reason.
            : $"cannot raise {who} — {(_pushRejects.Count > 0 ? _pushRejects[^1] : "push refused")}";
    }

    /// <summary>
    /// Dead party members in raise range, split by role. Mirrors the Variant layer's scan —
    /// corpses with Raise already pending are skipped so two toons don't stack casts.
    /// </summary>
    private (IBattleChara? DeadHealer, IBattleChara? DeadOther, bool LivingHealerPresent) ScanPartyForRaise(
        IRotationContext ctx)
    {
        IBattleChara? deadHealer = null;
        IBattleChara? deadOther = null;
        var livingHealer = false;

        foreach (var member in ctx.PartyList)
        {
            if (member?.GameObject is not IBattleChara chara || chara.GameObjectId == ctx.Player.GameObjectId)
                continue;

            var jobId = Daedalus.Rotation.Common.Helpers.TrustPartyRoleHelper.ResolveJobId(chara, ctx.PartyList);
            var isHealer = JobRegistry.IsHealer(jobId);

            if (!chara.IsDead)
            {
                if (isHealer)
                    livingHealer = true;
                continue;
            }

            if (HasStatus(chara, RaisePendingStatusId))
                continue;
            if (System.Numerics.Vector3.DistanceSquared(ctx.Player.Position, chara.Position) > RaiseRangeSquared)
                continue;

            _deadSince.TryAdd(chara.GameObjectId, DateTime.UtcNow);

            if (isHealer)
                deadHealer ??= chara;
            else
                deadOther ??= chara;
        }

        PruneDeadSince(ctx);

        // Nobody in the party needs it — look for a stranger. A CE floor is mostly other
        // people's bodies, and an instant raise costs us nothing but the recast.
        if (deadHealer is null && deadOther is null && _configuration.Occult.RaiseNonPartyPlayers)
            deadOther = FindDeadBystander(ctx);

        return (deadHealer, deadOther, livingHealer);
    }

    /// <summary>
    /// What actually happened when the queued raise met the scheduler. Either it fired, or the
    /// scheduler names the gate that refused it, or something else outbid it for the window.
    /// </summary>
    private void ReportRaiseDispatchOutcome(SchedulerDispatchResult result)
    {
        _raiseGcdBusySamples = 0;

        if (result.Dispatched
            && result.Winner is { } winner
            && (winner.Action.ActionId == OccultRaiseId || winner.Action.ActionId == ChemistReviveId))
        {
            _phantomJobs.RaiseState += " — dispatched";
            return;
        }

        foreach (var reason in result.GateFailReasons)
        {
            if (reason.StartsWith("Occult Raise", StringComparison.Ordinal)
                || reason.StartsWith("Revive", StringComparison.Ordinal))
            {
                _phantomJobs.RaiseState += $" — scheduler: {reason}";
                return;
            }
        }

        if (result.Dispatched)
            _phantomJobs.RaiseState += $" — lost the window to {result.Winner?.Action.Name}";
    }

    /// <summary>
    /// A raisable body is waiting and this job can do something about it, so the phantom layer
    /// must not take the GCD. Deliberately broad: any dead ally in raise range without a raise
    /// already pending, on a job that can raise at all.
    /// </summary>
    private bool RaisePendingForJob(IRotationContext ctx)
    {
        var jobCanRaise = JobRegistry.IsHealer(ctx.Player.ClassJob.RowId);
        if (!jobCanRaise)
            return false;

        foreach (var member in ctx.PartyList)
        {
            if (member?.GameObject is not IBattleChara chara || chara.GameObjectId == ctx.Player.GameObjectId)
                continue;
            if (!chara.IsDead)
                continue;
            if (HasStatus(chara, RaisePendingStatusId))
                continue;
            if (System.Numerics.Vector3.DistanceSquared(ctx.Player.Position, chara.Position) > RaiseRangeSquared)
                continue;

            return PhantomBandRules.ShouldYieldGcdForRaise(jobCanRaise, raisableCorpseInRange: true);
        }

        return false;
    }

    /// <summary>
    /// A dead player outside the party, within raise range. Only players — never a downed NPC —
    /// and never one who already has a raise pending, so two raisers do not stack on one body.
    /// </summary>
    private IBattleChara? FindDeadBystander(IRotationContext ctx)
    {
        if (ctx.ObjectTable is null)
            return null;

        IBattleChara? nearest = null;
        var nearestDistance = float.MaxValue;

        foreach (var obj in ctx.ObjectTable)
        {
            if (obj is not Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter pc)
                continue;
            if (pc.GameObjectId == ctx.Player.GameObjectId || !pc.IsDead)
                continue;
            if (HasStatus(pc, RaisePendingStatusId))
                continue;

            var distanceSquared = System.Numerics.Vector3.DistanceSquared(ctx.Player.Position, pc.Position);
            if (distanceSquared > RaiseRangeSquared || distanceSquared >= nearestDistance)
                continue;

            nearest = pc;
            nearestDistance = distanceSquared;
        }

        return nearest;
    }

    /// <summary>
    /// Why the scan found nothing. "Nobody down in range" hid three different situations — an
    /// actually-empty party, a body beyond the 30y cast range, and a body whose object we cannot
    /// read at all — and nothing moves a phantom job toward a corpse, so the middle case can
    /// persist indefinitely while reading as though there were nothing to do.
    /// </summary>
    private string DescribeNoRaiseTarget(IRotationContext ctx)
    {
        var nearest = float.MaxValue;
        var unreadable = 0;

        foreach (var member in ctx.PartyList)
        {
            if (member is null || member.GameObject is null)
            {
                unreadable++;
                continue;
            }

            if (member.GameObject is not IBattleChara chara
                || chara.GameObjectId == ctx.Player.GameObjectId
                || !chara.IsDead)
            {
                continue;
            }

            if (HasStatus(chara, RaisePendingStatusId))
                return $"{chara.Name?.TextValue} already has a raise pending";

            var distance = System.Numerics.Vector3.Distance(ctx.Player.Position, chara.Position);
            if (distance < nearest)
                nearest = distance;
        }

        if (nearest < float.MaxValue)
            return $"dead ally {nearest:F0}y away — out of 30y range";

        if (unreadable > 0)
            return $"nobody down ({unreadable} party member(s) not readable from here)";

        return _configuration.Occult.RaiseNonPartyPlayers
            ? "nobody down (party or nearby)"
            : "nobody down in party (bystander raising is off)";
    }

    /// <summary>How long this corpse has been down, or 0 when it has only just been seen.</summary>
    private double SecondsDown(ulong gameObjectId)
        => _deadSince.TryGetValue(gameObjectId, out var since)
            ? (DateTime.UtcNow - since).TotalSeconds
            : 0d;

    /// <summary>Forget anyone who got up, so a later death starts its clock fresh.</summary>
    private void PruneDeadSince(IRotationContext ctx)
    {
        if (_deadSince.Count == 0)
            return;

        var stillDown = new HashSet<ulong>();
        foreach (var member in ctx.PartyList)
        {
            if (member?.GameObject is IBattleChara chara && chara.IsDead)
                stillDown.Add(chara.GameObjectId);
        }

        if (stillDown.Count == _deadSince.Count)
            return;

        var gone = new List<ulong>();
        foreach (var id in _deadSince.Keys)
        {
            if (!stillDown.Contains(id))
                gone.Add(id);
        }

        foreach (var id in gone)
            _deadSince.Remove(id);
    }

    private void PushPartyBuffs(IRotationContext ctx, PhantomJob job, byte level, bool inCombat)
    {
        if (!inCombat)
            return;

        // Paced by the BUFF, not the recast — see TryPushBuff. Status ids are the same-named
        // statuses in the phantom block (XIVAPI-verified 2026-07-31).
        switch (job)
        {
            case PhantomJob.Bard:
                TryPushBuff(ctx, 41608, job, level, PrioPartyBuff);     // Offensive Aria
                TryPushBuff(ctx, 41607, job, level, PrioPartyBuff + 1); // Mighty March
                TryPushBuff(ctx, 41610, job, level, PrioPartyBuff + 2); // Hero's Rime
                break;
            case PhantomJob.Geomancer:
                TryPushBuff(ctx, 41611, job, level, PrioPartyBuff);     // Battle Bell
                TryPushBuff(ctx, 41619, job, level, PrioPartyBuff + 1); // Ringing Respite
                break;
            case PhantomJob.Ranger:
                TryPushBuff(ctx, 41599, job, level, PrioPartyBuff);     // Phantom Aim
                break;
            case PhantomJob.MysticKnight:
                TryPushBuff(ctx, 46590, job, level, PrioPartyBuff);     // Magic Shell
                break;
        }
    }

    /// <summary>Jobs with entries in the damage band below — the burst hold only concerns these.</summary>
    private static readonly HashSet<PhantomJob> DamageBandJobs =
    [
        PhantomJob.Berserker, PhantomJob.Samurai, PhantomJob.Cannoneer, PhantomJob.MysticKnight,
        PhantomJob.Gladiator, PhantomJob.Monk, PhantomJob.TimeMage, PhantomJob.Thief,
        // North Horn jobs — MISSING until 2026-08-01, which made the hold invisible for every
        // one of them: PushDamage returned silently and the Duty tab read "idle — nothing
        // eligible" instead of "damage held for burst window". Field report: a Lv4 Phantom Red
        // Mage on an ice-weak mob, Blizzard slotted, firing nothing but Cure II all fight (Cure
        // is survival, and survival ignores the hold — which is exactly why it looked like the
        // damage band was broken rather than held).
        // Keep this in step with the case labels in PushDamage below.
        PhantomJob.PhantomDragoon, PhantomJob.PhantomSummoner, PhantomJob.PhantomWhiteMage,
        PhantomJob.PhantomBlackMage, PhantomJob.PhantomRedMage, PhantomJob.PhantomNinja,
        PhantomJob.PhantomBlueMage,
        // NOT Necromancer: its only cataloged action (Drain Touch) fires pre-hold like Steal,
        // so the "damage held" line would be a lie for it.
    ];

    private void PushDamage(IRotationContext ctx, Config.PhantomConfig cfg, PhantomJob job, byte level, bool inCombat)
    {
        if (!inCombat)
            return;

        var target = ctx.TargetingService.GetUserEnemyTarget() ?? ctx.Player.TargetObject as IBattleChara;
        if (target is null || target.IsDead)
            return;

        var targetHpPct = target.MaxHp > 0 ? (float)target.CurrentHp / target.MaxHp : 1f;
        var distance = System.Numerics.Vector3.Distance(ctx.Player.Position, target.Position) - target.HitboxRadius;

        // Occult Libra (Red Mage): utility, not damage — it must fire regardless of the burst
        // hold, or "save damage for burst" would also stop us identifying anything. 5s recast
        // Ability, reveals the affinity for 120s, and every reveal boosts the whole party's
        // elemental damage AND fills the weakness table. Only at enemies we can't name yet.
        if (job == PhantomJob.PhantomRedMage
            && target is IBattleNpc libraTarget
            && TargetWeakness?.Invoke(libraTarget.NameId, libraTarget.Name?.TextValue) is null)
        {
            TryPush(ctx, 49094, job, level, PrioPartyBuff, target.GameObjectId, target);
        }

        // Occult Slowga (Time Mage): also pre-hold, for the same reason as Libra — it deals no
        // damage at all, so "save damage for burst" has nothing to save. It is the only action a
        // Lv.1 Time Mage owns (Comet needs Lv.2), and the 30s Slow it hangs is worth a GCD every
        // half-minute. Ranked behind Comet, which wins the queue on the turns it is up.
        if (job == PhantomJob.TimeMage)
        {
            // The CE exclusion is not a nicety. Slowga is paced on the target NOT already being
            // slowed, and something that cannot be slowed never acquires the status — so without
            // this the gate passes every frame and a zero-damage 2.5s GCD spell is re-cast for
            // the whole encounter. RSR excludes critical-encounter mobs for the same reason.
            var slowgaCe = IsCriticalEncounterMob(target);
            if (PhantomBandRules.ShouldSlowga(
                    cfg, inCombat, HasAnyStatus(target, PhantomActions.SlowStatusIds), slowgaCe))
            {
                TryPush(ctx, 41621, job, level, PrioDamage + 1, target.GameObjectId, target);
            }
            else if (slowgaCe)
            {
                _pushRejects.Add("Occult Slowga — critical-encounter enemies cannot be slowed");
            }
        }

        // Executes / non-scaling utility fire regardless of the burst hold (RSR parity).
        if (job == PhantomJob.Thief && PhantomBandRules.ShouldSteal(targetHpPct))
            TryPush(ctx, 41645, job, level, PrioDamage, target.GameObjectId, target); // Steal

        // Drain Touch is a 40s sustain drain, not a burst nuke — holding it for a burst
        // window left a Lv1 Necromancer firing NOTHING (field 2026-07-30, Duty tab:
        // "damage held", Last fired: none). Fires on cooldown like the Steal exemption.
        // It also grants the HP-floor buff Deep Freeze leans on, so it always leads.
        if (job == PhantomJob.Necromancer)
        {
            TryPush(ctx, 49097, job, level, PrioDamage, target.GameObjectId, target); // Drain Touch
            PushNecromancerDoomNukes(ctx, cfg, job, level, target);
        }

        // Dualcast is on a 15s clock that the main job's very next weaponskill cuts short, so a
        // burst hold can outlast it and the buff is simply thrown away. Spend it now.
        var hold = !_dualcastThisFrame && PhantomBandRules.ShouldHoldDamage(
            cfg.SaveDamageForBurst,
            _burstWindows?.IsInBurstWindow ?? false,
            _burstWindows?.SecondsSinceLastBurstStart ?? -1f);
        if (hold)
        {
            // Only report the hold for jobs that HAVE damage-band actions. The state-machine
            // jobs (Dancer/Oracle/Geomancer) run in a separate pass the hold never touches —
            // showing "damage held" on a Dancer read as the whole layer being stalled while
            // Jitterbug kept firing fine (field 2026-07-30 Duty-tab screenshot).
            if (DamageBandJobs.Contains(job))
                _pushRejects.Add("damage held for burst window");
            return;
        }

        switch (job)
        {
            case PhantomJob.Berserker:
                TryPush(ctx, 41592, job, level, PrioDamage, target.GameObjectId, target);     // Rage
                TryPush(ctx, 41594, job, level, PrioDamage + 1, target.GameObjectId, target); // Deadly Blow
                break;

            case PhantomJob.Samurai:
                if (_phantomJobs.GetItemCount(PhantomJobData.ZeninageCofferItemId) > 0)
                    TryPush(ctx, 41606, job, level, PrioDamage, target.GameObjectId, target); // Zeninage
                TryPush(ctx, 41605, job, level, PrioDamage + 1, target.GameObjectId, target); // Iainuki
                break;

            case PhantomJob.Cannoneer:
                TryPush(ctx, 41630, job, level, PrioDamage, target.GameObjectId, target);     // Silver Cannon
                if (cfg.CannoneerPreferDarkCannon)
                {
                    TryPush(ctx, 41628, job, level, PrioDamage + 1, target.GameObjectId, target); // Dark
                    TryPush(ctx, 41629, job, level, PrioDamage + 2, target.GameObjectId, target); // Shock
                }
                else
                {
                    TryPush(ctx, 41629, job, level, PrioDamage + 1, target.GameObjectId, target);
                    TryPush(ctx, 41628, job, level, PrioDamage + 2, target.GameObjectId, target);
                }
                TryPush(ctx, 41627, job, level, PrioDamage + 3, target.GameObjectId, target); // Holy Cannon
                TryPush(ctx, 41626, job, level, PrioDamage + 4, target.GameObjectId, target); // Phantom Fire
                break;

            case PhantomJob.MysticKnight:
                TryPush(ctx, 46593, job, level, PrioDamage, target.GameObjectId, target);     // Blazing Spellblade
                TryPush(ctx, 46592, job, level, PrioDamage + 1, target.GameObjectId, target); // Holy Spellblade
                TryPush(ctx, 46591, job, level, PrioDamage + 2, target.GameObjectId, target); // Sundering Spellblade
                break;

            case PhantomJob.Gladiator:
                TryPush(ctx, 46594, job, level, PrioDamage, target.GameObjectId, target);     // Finisher
                TryPush(ctx, 46596, job, level, PrioDamage + 1, target.GameObjectId, target); // Long Reach
                TryPush(ctx, 46597, job, level, PrioDamage + 2, target.GameObjectId, target); // Bladeblitz
                break;

            case PhantomJob.Monk:
                // Occult Counter leads: "can only be executed immediately after parrying an
                // attack", so its window is a single parry, while the Kick is a 30s cooldown that
                // loses nothing by waiting a weave. Both are oGCD Abilities, so neither costs the
                // other a GCD. The game owns the parry gate — GetActionStatus reads 0 only inside
                // that window, which is how RSR checks it too (checkActionManager: true).
                if (_actionService.GetActionStatusCode(41596, target.GameObjectId) == 0)
                    TryPush(ctx, 41596, job, level, PrioDamage, target.GameObjectId, target);     // Occult Counter
                else
                    _pushHolds.Add("Occult Counter — waiting on a parry (Counterstance raises the rate)");

                if (PhantomBandRules.ShouldPhantomKick(distance, cfg.MonkKickMaxRangeYalms))
                {
                    // It is a leap, not a step: no floor under the target means a pit, and the
                    // flight path is not covered by the stand-still safety the casts use.
                    if (IsDashSafe(ctx, target))
                        TryPush(ctx, 41595, job, level, PrioDamage + 1, target.GameObjectId, target); // Phantom Kick
                    else
                        _pushRejects.Add("Phantom Kick — the leap lands in a pit or a telegraph");
                }
                break;

            case PhantomJob.TimeMage:
                TryPush(ctx, 41623, job, level, PrioDamage, target.GameObjectId, target);     // Occult Comet
                break;

            case PhantomJob.Thief:
                TryPush(ctx, 41649, job, level, PrioDamage + 5, target.GameObjectId, target); // Pilfer Weapon
                break;

            case PhantomJob.PhantomDragoon:
                // Lance first — 300 plus a drain that overheals into a 60s barrier, so it is
                // damage AND sustain. Occult Jump is the bigger hit (500 with the Lv.4 trait)
                // and carries a 90% damage cut for its 2s, but it is a Weaponskill and takes
                // the GCD, so it follows.
                TryPush(ctx, 49079, job, level, PrioDamage, target.GameObjectId, target);
                TryPush(ctx, 49077, job, level, PrioDamage + 1, target.GameObjectId, target);
                break;

            case PhantomJob.PhantomSummoner:
                // Megaflare leads: unaspected 1,000 on its own 90s timer, no weakness applies.
                TryPush(ctx, 49084, job, level, PrioDamage, target.GameObjectId, target);

                // All three of the shared-recast trio, best match first. Pushing only the best
                // pick left a Lv.3 Summoner firing nothing at a wind-weak target, because its
                // only wind nuke (Thunderstorm) is Lv.4.
                var smnOrder = PhantomBandRules.SummonerNukeOrder(
                    target is IBattleNpc smnTarget ? TargetWeakness?.Invoke(smnTarget.NameId, smnTarget.Name?.TextValue) : null);
                for (var i = 0; i < smnOrder.Length; i++)
                    TryPush(ctx, smnOrder[i], job, level, PrioDamage + 1 + i, target.GameObjectId, target);
                break;

            case PhantomJob.PhantomWhiteMage:
                // Occult Holy: unaspected 500 (750 vs undead), 8y, own 60s timer.
                TryPush(ctx, 49071, job, level, PrioDamage, target.GameObjectId, target);
                break;

            case PhantomJob.PhantomBlackMage:
                // Flare leads: unaspected 500 on its own 60s timer, so no weakness applies.
                TryPush(ctx, 49076, job, level, PrioDamage, target.GameObjectId, target);
                var blmOrder = PhantomBandRules.BlackMageNukeOrder(
                    target is IBattleNpc blmTarget ? TargetWeakness?.Invoke(blmTarget.NameId, blmTarget.Name?.TextValue) : null);
                for (var i = 0; i < blmOrder.Length; i++)
                    TryPush(ctx, blmOrder[i], job, level, PrioDamage + 1 + i, target.GameObjectId, target);
                break;

            case PhantomJob.PhantomBlueMage:
                // Aqua Breath first: 300 unaspected in a 5y splash beats Aero's single-target
                // 150 whenever more than one thing is standing there, and it ignores weakness.
                TryPush(ctx, 49087, job, level, PrioDamage, target.GameObjectId, target);
                // Missile is a coin flip for 75% of CURRENT hp — worthless on a nearly-dead
                // target and enormous on a fresh one, so it leads only while the pack is healthy.
                // The tooltip's "with some exceptions" means critical-encounter and FATE enemies:
                // they shrug it off, so spending the GCD there is pure loss.
                if (targetHpPct > 0.5f)
                {
                    if (!PhantomBandRules.ShouldMissile(IsCriticalEncounterMob(target), IsFateMob(target)))
                    {
                        _pushRejects.Add("Occult Missile — no effect on critical-encounter or FATE enemies");
                    }
                    else if (PartyCoordination?.IsPhantomActionReservedByOther(target.EntityId, 49086) == true)
                    {
                        // A fleet of Phantom Blue Mages shares one target and one frame, so
                        // without this all four spend a 30s recast on the same mob at once. The
                        // hold is brief by design: Missile misses about two thirds of the time,
                        // and once it lapses the health gate above tells the two cases apart —
                        // a hit leaves the target at a quarter health and out of scope, a miss
                        // leaves it fair game for the next toon.
                        _pushHolds.Add("Occult Missile — another toon just fired one at this enemy");
                    }
                    else
                    {
                        var missileTargetEntityId = target.EntityId;
                        TryPush(ctx, 49086, job, level, PrioDamage + 1, target.GameObjectId, target,
                            onExtraDispatched: () =>
                                PartyCoordination?.ReservePhantomAction(missileTargetEntityId, 49086));
                    }
                }
                // Aero grades are one button, but which grade is LEARNED depends on enemies,
                // not phantom level — push best-first and let the duty-bar gate pick the one
                // actually known, so an unlearned top grade cannot silence the lower ones.
                for (var i = 0; i < PhantomBandRules.AeroGradesDescending.Length; i++)
                    TryPush(ctx, PhantomBandRules.AeroGradesDescending[i], job, level, PrioDamage + 2 + i,
                        target.GameObjectId, target);
                break;

            case PhantomJob.PhantomRedMage:
                PushRedMage(ctx, cfg, job, level, target);
                break;

            case PhantomJob.PhantomNinja:
                // All weaves (Abilities), so none of this competes for the GCD. Scrolls have
                // independent 60s recasts — lead with the target's weak element, fire both.
                TryPush(ctx, 49062, job, level, PrioDamage, target.GameObjectId, target); // Fuma Shuriken
                var scroll = PhantomBandRules.PreferredScroll(
                    target is IBattleNpc scrollTarget ? TargetWeakness?.Invoke(scrollTarget.NameId, scrollTarget.Name?.TextValue) : null);
                var otherScroll = scroll == PhantomBandRules.FlameScrollId
                    ? PhantomBandRules.LightningScrollId
                    : PhantomBandRules.FlameScrollId;
                TryPush(ctx, scroll, job, level, PrioDamage + 1, target.GameObjectId, target);
                TryPush(ctx, otherScroll, job, level, PrioDamage + 2, target.GameObjectId, target);
                break;
        }
    }

    /// <summary>
    /// Deep Freeze (Necromancer Lv.2): 30y line nuke that costs 10% max HP and DOOMS the
    /// caster for 10s — cleared only by a heal to FULL. Gated by <see cref="PhantomBandRules
    /// .ShouldDeepFreeze"/>; every refusal names itself in the Duty tab so an unfired Deep
    /// Freeze is never a mystery.
    /// </summary>
    private void PushNecromancerDoomNukes(
        IRotationContext ctx, Config.PhantomConfig cfg, PhantomJob job, byte level, IBattleChara target)
    {
        if (!cfg.NecromancerUseDeepFreeze)
        {
            // Reported rather than silent, unlike the other opt-ins. This one toggle gates FOUR
            // actions and is named after one of them, so a Necromancer wondering why Hell Wind
            // never fires has nothing to find — the setting they need does not mention it
            // (field 2026-09-03, reported exactly that way).
            _pushHolds.Add(
                "Doom nukes off — Deep Freeze, Hell Wind, Chaos Drive and Doomsday all sit behind "
                + "the \"Use Deep Freeze\" toggle");
            return;
        }

        var maxHp = ctx.Player.MaxHp;
        var selfHpPct = maxHp > 0 ? (float)ctx.Player.CurrentHp / maxHp : 1f;
        var hasDoom = _actionService.PlayerHasStatus(PhantomActions.StatusIds.DoomDispelledByFullHeal);
        var hasDrainTouch = _actionService.PlayerHasStatus(PhantomActions.StatusIds.DrainTouch);

        // A healer must be present and listening: Deep Freeze's Doom is only survivable
        // because someone tops us to 100% inside 10s. Solo, this action is suicide, so the
        // gate is hard regardless of the other settings (user call, 2026-07-31).
        if (HealerAvailable?.Invoke() != true)
        {
            _pushRejects.Add("Doom nukes held (Deep Freeze / Hell Wind / Chaos Drive) — no healer in party, the Doom would be lethal");
            return;
        }

        if (!PhantomBandRules.ShouldFireDoomNuke(cfg, selfHpPct, hasDoom, hasDrainTouch))
        {
            _pushRejects.Add(hasDoom
                ? "Doom nuke held — Doom already ticking"
                : selfHpPct < cfg.NecromancerDeepFreezeMinHpPercent
                    ? $"Doom nuke held — HP {selfHpPct:P0} below the {cfg.NecromancerDeepFreezeMinHpPercent:P0} floor"
                    : "Doom nuke held — needs the Drain Touch buff first");
            return;
        }

        // Element choice: the trio share one recast, so fire the one the target is weak to.
        var weakness = cfg.NecromancerMatchElementalWeakness && target is IBattleNpc npcTarget
            ? TargetWeakness?.Invoke(npcTarget.NameId, npcTarget.Name?.TextValue)
            : null;
        var nukeOrder = PhantomBandRules.NecromancerNukeOrder(weakness);
        var selfName = ctx.Player.Name?.TextValue ?? string.Empty;

        // The three share ONE 40s recast, so whichever dispatches first spends it and the other
        // two are refused "on cooldown". That makes the pick the whole story, and an unrecorded
        // weakness is indistinguishable from a wrong one unless it says so: the table only knows
        // what something has revealed (Occult Libra), not what the player can see in game.
        if (cfg.NecromancerMatchElementalWeakness && weakness is null)
            _pushHolds.Add($"{NukeName(nukeOrder[0])} leads — no elemental weakness recorded for this enemy yet");
        else if (weakness is { } matched)
            _pushHolds.Add($"{NukeName(nukeOrder[0])} leads — this enemy is recorded weak to {matched}");

        // Doomsday when enabled: own 120s timer, biggest hit (500 under Drain Touch) and it
        // strips a buff. Only one can land — the Doom gate stops whichever loses the race.
        if (cfg.NecromancerUseDoomsday)
        {
            TryPush(ctx, 49101, job, level, PrioDamage + 1, target.GameObjectId, target,
                onExtraDispatched: () => AnnounceDoom(selfName, "Doomsday"));
        }

        // All three, best match first — they share one recast, so the extras cost nothing and one
        // refusal can no longer mean zero damage. The Doom announcement names whichever fires.
        for (var i = 0; i < nukeOrder.Length; i++)
        {
            var nukeId = nukeOrder[i];
            TryPush(ctx, nukeId, job, level, PrioDamage + 2 + i, target.GameObjectId, target,
                onExtraDispatched: () => AnnounceDoom(selfName, NukeName(nukeId)));
        }
    }

    private static string NukeName(uint id) => id switch
    {
        PhantomBandRules.HellWindId => "Hell Wind",
        PhantomBandRules.ChaosDriveId => "Chaos Drive",
        _ => "Deep Freeze",
    };

    /// <summary>Announce BEFORE the Doom lands so healers are already prioritising us.</summary>
    private void AnnounceDoom(string selfName, string actionName)
    {
        Daedalus.Services.Occult.DoomTopOffWatch.RequestTopOff(selfName);
        DebugLog?.Log(Daedalus.Services.Debug.DebugLogCategory.Action,
            Daedalus.Services.Debug.DebugLogSeverity.Warning,
            $"{actionName} cast — DOOM on self, top-off requested for {selfName}");
    }

    /// <summary>
    /// "Is a healer present who can top this toon to full?" — wired by Plugin from the live
    /// party plus the LAN roster. Deep Freeze refuses to fire without one.
    /// </summary>
    public Func<bool>? HealerAvailable { get; set; }

    /// <summary>Debug-log sink (wired by Plugin) for the Deep Freeze / Doom announcements.</summary>
    public Daedalus.Services.Debug.DebugLogService? DebugLog { get; set; }

    /// <summary>
    /// Learned elemental weaknesses for an enemy NameId (wired by Plugin from the weakness
    /// log). Null = never revealed, which is not evidence of absence — the nuke picker just
    /// falls back to its default element.
    /// </summary>
    public Func<uint, string?, Daedalus.Services.Occult.OccultElement?>? TargetWeakness { get; set; }

    private readonly OracleDeckTracker _oracleDeck = new();

    /// <summary>
    /// Shared raise reservation so a fleet never stacks raises on one corpse. Optional — set by
    /// the plugin alongside the other injected hooks; null in tests.
    /// </summary>
    public Daedalus.Services.Party.IPartyCoordinationService? PartyCoordination { get; set; }

    private static bool HasStatus(IBattleChara chara, uint statusId)
    {
        if (chara.StatusList == null)
            return false;

        foreach (var status in chara.StatusList)
        {
            if (status != null && status.StatusId == statusId)
                return true;
        }

        return false;
    }

    /// <summary>Occult Cure II — the Dualcast primer as well as the heal.</summary>
    private const uint OccultCureIIId = 49093;

    /// <summary>
    /// Phantom Red Mage's GCD line. The three nukes share one 30s recast, so they are all pushed
    /// at descending priority — the first the gates accept fires and the rest cost nothing.
    /// Pushing only the best match meant one refusal produced no damage at all.
    /// </summary>
    private void PushRedMage(IRotationContext ctx, Config.PhantomConfig cfg, PhantomJob job, byte level, IBattleChara target)
    {
        var weakness = target is IBattleNpc rdmTarget ? TargetWeakness?.Invoke(rdmTarget.NameId, rdmTarget.Name?.TextValue) : null;
        var order = PhantomBandRules.RedMageNukeOrder(weakness);

        var plan = PhantomBandRules.PlanRedMage(
            hasDualcast: _dualcastThisFrame,
            phantomLevel: level,
            weaknessKnown: weakness is not null,
            nukeReady: order.Length > 0 && _actionService.IsActionReady(order[0]),
            cureReady: _actionService.IsActionReady(OccultCureIIId),
            currentMp: (int)ctx.Player.CurrentMp,
            primeEnabled: cfg.RedMagePrimeDualcastWithCure,
            mpFloor: cfg.RedMagePrimeMpFloor);

        // Say why it stopped priming. Field 2026-08-11: "worked like a charm, just sometimes
        // during the same fight it would straight cast damage instead" — which is exactly what a
        // silently-enforced budget looks like from the outside.
        if (plan == PhantomBandRules.RedMagePlan.HardcastNuke && cfg.RedMagePrimeDualcastWithCure
            && PhantomBandRules.DescribePrimeBlock(
                level, weakness is not null, (int)ctx.Player.CurrentMp, cfg.RedMagePrimeMpFloor) is { } why)
        {
            _pushHolds.Add($"hard-casting — not priming Dualcast: {why}");
        }

        if (plan == PhantomBandRules.RedMagePlan.PrimeWithCure)
        {
            // Cure II leads: it earns Dualcast, and next window the matched nuke lands instantly.
            // The nukes still queue behind it, so if the Cure is refused for any reason the
            // ordinary hard-cast line is still there and we never end up doing nothing.
            TryPush(ctx, OccultCureIIId, job, level, PrioDamage, ctx.Player.GameObjectId);
            _pushHolds.Add("Occult Cure II — priming Dualcast for the matched nuke");
        }

        var basePriority = plan == PhantomBandRules.RedMagePlan.PrimeWithCure ? PrioDamage + 1 : PrioDamage;
        for (var i = 0; i < order.Length; i++)
            TryPush(ctx, order[i], job, level, basePriority + i, target.GameObjectId, target);
    }

    /// <summary>
    /// Which director owns this enemy, read live off the object rather than inferred.
    /// <para>
    /// <c>PublicContentDirector</c> means it belongs to a critical encounter, <c>FateDirector</c>
    /// to a FATE. RSR reads exactly this for its Occult target gates, and it is strictly better
    /// than the weakness table's encounter stamp, which records whatever happened to be running
    /// when the enemy was last seen — that is how the escort pot picked up a CE it had nothing to
    /// do with. This asks the enemy itself.
    /// </para>
    /// </summary>
    private static unsafe bool HasEventType(IGameObject? obj, EventHandlerContent want)
    {
        if (obj is null || obj.Address == nint.Zero)
            return false;

        try
        {
            var native = (FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)obj.Address;
            return native != null && native->EventId.ContentId == want;
        }
        catch
        {
            // A bad read must not take the layer down, and it must fail OPEN: "no director known"
            // means no exclusion, which is the behaviour we had before these gates existed.
            return false;
        }
    }

    private static bool IsCriticalEncounterMob(IGameObject? obj)
        => HasEventType(obj, EventHandlerContent.PublicContentDirector);

    private static bool IsFateMob(IGameObject? obj)
        => HasEventType(obj, EventHandlerContent.FateDirector);

    private static bool HasAnyStatus(IBattleChara chara, IReadOnlyList<uint> statusIds)
    {
        if (chara.StatusList == null)
            return false;

        foreach (var status in chara.StatusList)
        {
            if (status == null)
                continue;

            for (var i = 0; i < statusIds.Count; i++)
            {
                if (status.StatusId == statusIds[i])
                    return true;
            }
        }

        return false;
    }

    private void PushStateMachines(IRotationContext ctx, Config.PhantomConfig cfg, PhantomJob job, byte level, float selfHpPct, bool inCombat)
    {
        switch (job)
        {
            case PhantomJob.Oracle:
                PushOracle(ctx, cfg, job, level, selfHpPct, inCombat);
                break;
            case PhantomJob.Dancer:
                PushDancer(ctx, job, level, inCombat);
                break;
            case PhantomJob.Geomancer:
                PushGeomancer(ctx, cfg, job, level, inCombat);
                break;
        }
    }

    private DateTime? _oracleWindowStart;

    private void PushOracle(IRotationContext ctx, Config.PhantomConfig cfg, PhantomJob job, byte level, float selfHpPct, bool inCombat)
    {
        // Which card is the game currently offering? (Predict morphs the slot per card.)
        var activeCard =
            _actionService.PlayerHasStatus(PhantomActions.StatusIds.PredictionOfJudgment) ? OracleCardPolicy.JudgmentCard
            : _actionService.PlayerHasStatus(PhantomActions.StatusIds.PredictionOfCleansing) ? OracleCardPolicy.CleansingCard
            : _actionService.PlayerHasStatus(PhantomActions.StatusIds.PredictionOfBlessing) ? OracleCardPolicy.BlessingCard
            : _actionService.PlayerHasStatus(PhantomActions.StatusIds.PredictionOfStarfall) ? OracleCardPolicy.StarfallCard
            : 0u;

        _oracleDeck.Update(activeCard);

        if (activeCard == 0)
        {
            _oracleWindowStart = null;
            if (inCombat)
                TryPush(ctx, 41636, job, level, PrioDamage, onExtraDispatched: _oracleDeck.OnPredictDispatched); // Predict
            return;
        }

        // Window timer — the safety net even if the deck tracker desynced (manual Predict,
        // plugin reload mid-window): past ForceCommitSeconds a card is ALWAYS played, or
        // False Prediction kills the player (50,000 potency; field death 2026-07-25).
        _oracleWindowStart ??= DateTime.UtcNow;
        var elapsed = (float)(DateTime.UtcNow - _oracleWindowStart.Value).TotalSeconds;

        var decision = OracleCardPolicy.Decide(
            activeCard,
            cfg,
            _oracleDeck.IsLastCard(activeCard),
            elapsed,
            selfHpPct,
            ctx.PartyHealthMetrics.avgHpPercent,
            invulnBuffUp: _actionService.PlayerHasStatus(PhantomActions.StatusIds.Invulnerability),
            invulnReady: level >= 6 && _actionService.IsActionReady(41644));

        switch (decision)
        {
            case OracleDecision.PlayCard:
                TryPush(ctx, activeCard, job, level, PrioDamage);
                break;
            case OracleDecision.CastInvulnerability:
                // Make Starfall safe first: Invulnerability on self, Starfall next window.
                TryPush(ctx, 41644, job, level, PrioEmergencySustain);
                break;
        }
    }

    private void PushDancer(IRotationContext ctx, PhantomJob job, byte level, bool inCombat)
    {
        if (!inCombat)
            return;

        // Steps morph off Dance; each is proc-gated by its status (scheduler enforces).
        TryPushProc(ctx, 46599, job, level, PrioDamage, PhantomActions.StatusIds.PoisedToSwordDance);
        TryPushProc(ctx, 46600, job, level, PrioDamage + 1, PhantomActions.StatusIds.TemptedToTango);
        TryPushProc(ctx, 46601, job, level, PrioDamage + 2, PhantomActions.StatusIds.Jitterbugged);
        TryPushProc(ctx, 46602, job, level, PrioDamage + 3, PhantomActions.StatusIds.WillingToWaltz);

        // No proc up → open the chain (Dance is the party buff/opener in the buff band).
        var anyProc = _actionService.PlayerHasStatus(PhantomActions.StatusIds.PoisedToSwordDance)
            || _actionService.PlayerHasStatus(PhantomActions.StatusIds.TemptedToTango)
            || _actionService.PlayerHasStatus(PhantomActions.StatusIds.Jitterbugged)
            || _actionService.PlayerHasStatus(PhantomActions.StatusIds.WillingToWaltz);
        if (!anyProc)
            TryPush(ctx, 46598, job, level, PrioPartyBuff);
    }

    private void PushGeomancer(IRotationContext ctx, Config.PhantomConfig cfg, PhantomJob job, byte level, bool inCombat)
    {
        if (inCombat)
        {
            // The six Lv.2 buffs are weather-gated — only the matching one is executable,
            // so each push carries an executability gate on top of the recast check.
            TryPushWeather(ctx, 41613, job, level); // Sunbath
            TryPushWeather(ctx, 41614, job, level); // Cloudy Caress
            TryPushWeather(ctx, 41615, job, level); // Blessed Rain
            TryPushWeather(ctx, 41616, job, level); // Misty Mirage
            TryPushWeather(ctx, 41617, job, level); // Hasty Mirage
            TryPushWeather(ctx, 41618, job, level); // Aetherial Gain
        }

        var suspendWanted = inCombat ? cfg.GeomancerSuspendInCombat : cfg.GeomancerSuspendOutOfCombat;
        if (suspendWanted && ctx.Player.TargetObject is IBattleChara { IsDead: false } target)
            TryPush(ctx, 41620, job, level, PrioPartyBuff + 5, target.GameObjectId, target); // Suspend
    }

    private void TryPushProc(IRotationContext ctx, uint actionId, PhantomJob job, byte level, int priority, uint procStatusId)
    {
        if (_actionService.PlayerHasStatus(procStatusId))
            TryPush(ctx, actionId, job, level, priority);
    }

    /// <summary>
    /// Party buffs: push only while the buff is DOWN. Their recasts are far shorter than their
    /// durations (Offensive Aria: 5s recast, 70s buff), so pacing on the recast alone re-fires
    /// them every few seconds — the cooldown gate in <see cref="TryPush"/> cannot prevent that.
    /// Re-application lands within one recast of expiry, which is a negligible gap on a 70s buff.
    /// </summary>
    private void TryPushBuff(IRotationContext ctx, uint actionId, PhantomJob job, byte level, int priority)
    {
        // Fail-open: an action with no mapped status falls back to recast pacing.
        if (PhantomActions.PartyBuffStatusByAction.TryGetValue(actionId, out var buffStatusId)
            && _actionService.PlayerHasStatus(buffStatusId))
        {
            // Note it. This is the intended steady state, but it is indistinguishable from a
            // broken action unless it says so — including when the buff came from someone
            // else's Bard, which is a perfectly good reason for yours to stay quiet.
            _pushHolds.Add($"{NameOf(actionId)} held — buff already up");
            return;
        }

        TryPush(ctx, actionId, job, level, priority);
    }

    private void TryPushWeather(IRotationContext ctx, uint actionId, PhantomJob job, byte level)
    {
        if (_actionService.CanExecuteActionId(actionId))
            TryPush(ctx, actionId, job, level, PrioPartyBuff);
    }

    /// <summary>
    /// Common per-action gates (catalog membership, phantom level, duty-bar slot,
    /// cooldown, range, cast-while-moving) and the actual scheduler push. Target 0 = self.
    /// </summary>
    /// <returns>
    /// Whether the action actually reached the scheduler. Callers that track a pending action
    /// across frames MUST use this: latching "queued" before calling and never correcting it
    /// made a raise rejected on cooldown keep counting GCD-busy samples, which read as a stalled
    /// raise when the cast had in fact just succeeded.
    /// </returns>
    private bool TryPush(IRotationContext ctx, uint actionId, PhantomJob job, byte level, int priority,
        ulong targetId = 0, IBattleChara? rangeTarget = null, Action? onExtraDispatched = null)
    {
        PhantomActionDef? found = null;
        foreach (var def in PhantomActions.All)
        {
            if (def.ActionId == actionId)
            {
                found = def;
                break;
            }
        }

        if (found is not { } action || action.Job != job)
            return false;
        // Below the phantom-level unlock. Reported as a HOLD rather than staying silent: a
        // wrong RequiredLevel in the catalog is indistinguishable from "nothing to do" otherwise,
        // and that is exactly how Occult Thunder II sat unusable for 13 minutes behind an
        // "idle — nothing eligible" readout (field 2026-08-11, catalog said 6, real answer 5).
        if (level < action.RequiredLevel)
        {
            _pushHolds.Add($"{action.Name} needs phantom Lv.{action.RequiredLevel} (you are {level})");
            return false;
        }
        if (!IsOnDutyBar(actionId))
        {
            _pushRejects.Add($"{action.Name} not on duty bar");
            return false;
        }
        if (!_actionService.IsActionReady(actionId))
        {
            _pushRejects.Add($"{action.Name} on cooldown");
            return false;
        }

        if (!_behaviorCache.TryGetValue(actionId, out var behavior))
        {
            behavior = new AbilityBehavior { Action = _phantomJobs.GetActionDefinition(action) };
            _behaviorCache[actionId] = behavior;
        }

        // Under Dualcast the spell has no cast time at all, so none of the standing-still
        // machinery below applies — the catalog's CastTime is the hardcast value and is simply
        // wrong for this one cast. Without this the gate refuses a FREE INSTANT nuke for "moving".
        var instantFromDualcast = _dualcastThisFrame && behavior.Action.IsGCD;

        if (behavior.Action.CastTime > 0 && _isMovingThisFrame && !instantFromDualcast)
        {
            // Don't stop for a window we can't use yet — keep moving until the GCD is nearly up.
            var gcdRemaining = _actionService.GcdRemaining;
            if (PhantomBandRules.ShouldKeepMovingUntilGcd(gcdRemaining, behavior.Action.IsGCD))
            {
                _pushHolds.Add($"{action.Name} — moving until the GCD comes up ({gcdRemaining:0.0}s)");
                return false;
            }

            // Ask to stand still for it, the way a hardcast raise does — but only when BossMod
            // says the spot is safe for the WHOLE stand, which is the wait for the GCD plus the
            // cast, not the cast alone. The hold is expiry-driven and the Plugin-side watcher
            // releases it the instant the ground turns dangerous, so a mechanic always beats a
            // nuke. The cast lands on a later frame, once actually still.
            var still = PhantomBandRules.StillSecondsForCast(gcdRemaining, behavior.Action.IsGCD, behavior.Action.CastTime);
            if (RequestCastHoldIfSafe(ctx, still))
                _pushHolds.Add($"{action.Name} — pausing movement to cast ({still:0.0}s)");
            else
                _pushRejects.Add($"{action.Name} needs a hard cast (moving)");

            return false;
        }

        // GCD-only hold: the job is sitting on a buff worth a weaponskill. oGCDs cost it nothing.
        if (_gcdHoldStatusThisFrame is { } gcdHoldId && behavior.Action.IsGCD)
        {
            _pushHolds.Add($"{action.Name} — GCD reserved (status {gcdHoldId})");
            return false;
        }

        if (rangeTarget is not null && behavior.Action.Range > 0)
        {
            var dist = System.Numerics.Vector3.Distance(ctx.Player.Position, rangeTarget.Position)
                       - rangeTarget.HitboxRadius;
            if (dist > behavior.Action.Range + RangeBufferYalms)
            {
                _pushRejects.Add($"{action.Name} out of range");
                return false;
            }
        }

        var name = action.Name;
        Action<IRotationContext> onDispatched = _ =>
        {
            _dispatchedThisFrame = true;
            _phantomJobs.LayerLastDispatch = $"{DateTime.Now:HH:mm:ss} {name}";
            onExtraDispatched?.Invoke();
        };

        if (behavior.Action.IsGCD)
            _scheduler.PushGcd(behavior, targetId, priority, onDispatched);
        else
            _scheduler.PushOgcd(behavior, targetId, priority, onDispatched);

        return true;
    }

    /// <summary>Catalog name for an action id, falling back to the id so a note is never blank.</summary>
    private static string NameOf(uint actionId)
    {
        foreach (var def in PhantomActions.All)
        {
            if (def.ActionId == actionId)
                return def.Name;
        }

        return actionId.ToString();
    }

    /// <summary>
    /// Duty-bar membership, morph-aware: Oracle cards, Dancer steps and Geomancer
    /// weather variants replace their base action on the slot, so the slot's adjusted
    /// ID must match too. Fail closed on empty slot reads.
    /// </summary>
    private bool IsOnDutyBar(uint actionId)
    {
        foreach (var slotId in _phantomJobs.GetDutySlotIds())
        {
            if (slotId == 0)
                continue;
            if (slotId == actionId || _actionService.GetAdjustedActionId(slotId) == actionId)
                return true;
        }

        return false;
    }

    private uint? FindLockoutStatus()
    {
        foreach (var statusId in PhantomActions.LockoutStatusIds)
        {
            if (_actionService.PlayerHasStatus(statusId))
                return statusId;
        }

        return null;
    }

    /// <summary>
    /// Requests the shared movement hold so a hard cast can land, if it is safe to stand still
    /// for the whole cast. Returns whether the hold was taken.
    /// <para>
    /// Reuses <see cref="Daedalus.Services.Positional.RaiseCastHold"/> deliberately rather than
    /// adding a second mechanism: the Plugin already pauses BMR steering while it is active AND
    /// releases it the moment the caster's ground turns unsafe, so phantom casts inherit that
    /// mid-cast bail for free.
    /// </para>
    /// </summary>
    private bool RequestCastHoldIfSafe(IRotationContext ctx, float stillSeconds)
    {
        if (_configThisFrame?.PauseMovementForPhantomCasts != true)
            return false;
        if (CastSafety is null)
            return false;

        // A slip margin on top of the stand: finishing the cast exactly as something lands is
        // not "safe", and the animation lock outlasts the cast bar.
        var window = stillSeconds + PhantomCastSafetyMarginSeconds;
        if (!CastSafety(ctx.Player.Position, window))
            return false;

        Daedalus.Services.Positional.RaiseCastHold.Request(stillSeconds + PhantomCastHoldSlackSeconds);
        return true;
    }

    /// <summary>Nothing may land within the cast plus this, or standing still is not safe.</summary>
    private const float PhantomCastSafetyMarginSeconds = 1.0f;

    /// <summary>Hold a little past the cast so the tail of the animation lock is covered.</summary>
    private const float PhantomCastHoldSlackSeconds = 0.6f;

    /// <summary>
    /// Is it safe to stand at this position for the given window? Supplied by Plugin from
    /// BossModSafetyService; null (or BMR absent) means no pause is attempted.
    /// </summary>
    public Func<System.Numerics.Vector3, float, bool>? CastSafety { get; set; }

    /// <summary>
    /// Is a leap from here to there safe — floor at the landing, no telegraph on the way?
    /// Supplied by Plugin from <see cref="Rotation.Common.Helpers.TargetedDashGuard"/>; null means
    /// nothing is known, and a leap with no information behind it is allowed rather than blocked.
    /// </summary>
    public Func<System.Numerics.Vector3, System.Numerics.Vector3, bool>? DashSafety { get; set; }

    private bool IsDashSafe(IRotationContext ctx, IBattleChara target)
        => DashSafety is null || DashSafety(ctx.Player.Position, target.Position);

    /// <summary>A buff whose GCD should not be spent on a phantom action. oGCDs are unaffected.</summary>
    private uint? FindGcdHoldStatus()
    {
        foreach (var statusId in PhantomActions.GcdHoldStatusIds)
        {
            if (_actionService.PlayerHasStatus(statusId))
                return statusId;
        }

        return null;
    }
}
