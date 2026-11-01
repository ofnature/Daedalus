using System;
using System.Collections.Generic;
using Daedalus.Data;
using Daedalus.Models.Action;
using Daedalus.Rotation.ArtemisCore.Abilities;
using Daedalus.Rotation.ArtemisCore.Context;
using Daedalus.Rotation.ArtemisCore.Helpers;
using Daedalus.Rotation.Common.Helpers;
using Daedalus.Rotation.Common.Scheduling;
using Daedalus.Services.Action;

namespace Daedalus.Rotation.ArtemisCore.Modules;

/// <summary>
/// The Beastmaster damage rotation.
///
/// <para>
/// Two independent tracks, which is what makes this job unlike every other melee: the 2.5s combo
/// chain occupies the GCD, while the instinctual skills run on their OWN 5s recast — "Instinctual
/// skills do not share a recast timer with any other actions". They are Weaponskills that do not
/// consume the global, so they are pushed as oGCDs and weave alongside the chain. Spending a GCD
/// slot on one would roughly halve the job's throughput, and the mistake would be invisible in a
/// log; it would just look slow.
/// </para>
///
/// <para>
/// TP is never read. The Beastmaster gauge is absent from ClientStructs and the only route to the
/// number is scraping JobHudXBM0's nodes. It is not needed: instinctual skills are unusable below
/// their TP minimum, so the game's own action status code is the gate — if it says the action can
/// fire, there is enough TP. That also covers recast and range in the same check.
/// </para>
/// </summary>
public sealed class DamageModule : IArtemisModule
{
    public int Priority => 30;
    public string Name => "Damage";

    public bool TryExecute(IArtemisContext context, bool isMoving) => false;

    public void UpdateDebugState(IArtemisContext context) { }

    public void CollectCandidates(IArtemisContext context, RotationScheduler scheduler, bool isMoving)
    {
        if (!context.InCombat)
        {
            context.Debug.DamageState = "Not in combat";
            return;
        }
        if (context.TargetingService.IsDamageTargetingPaused())
        {
            context.Debug.DamageState = "Paused (no target)";
            return;
        }
        if (context.Configuration.Targeting.SuppressDamageOnForcedMovement
            && PlayerSafetyHelper.IsForcedMovementActive(context.Player))
        {
            context.Debug.DamageState = "Paused (forced movement)";
            return;
        }

        var player = context.Player;
        var target = context.TargetingService.FindEnemyForAction(
            context.Configuration.Targeting.EnemyStrategy,
            BSTActions.SmashAxe.ActionId,
            player);

        if (target == null)
        {
            context.Debug.DamageState = "No target";
            return;
        }

        var targetId = target.GameObjectId;
        var level = player.Level;

        PushComboChain(context, scheduler, targetId, level);

        // With a familiar whose Trick colour is known, Trick and the clockwise axe go out as a pair.
        // Otherwise (no familiar, an unidentified one, or below the levels that make the pair) the
        // axes and Trick run on their own, as before.
        // Level 50, 250 TP and half the centre lit: the other half's upgraded skill is Universality, and
        // nothing else on the Compass is worth doing first.
        var finisher = context.Gauge is { } finisherGauge && context.Configuration.Beastmaster.EnableInstinctualSkills
            ? BSTActions.InstinctualFor(ArtemisCooldownRules.FinisherAxe(level, finisherGauge))
            : null;
        var finishing = finisher != null && CanFire(context.ActionService, level, finisher, targetId);

        // The infinitive combo's 7 s window: only Rally and the finisher until it completes or closes.
        var window = context.Gauge is { } windowGauge && context.Configuration.Beastmaster.EnableInstinctualSkills
            && ArtemisCooldownRules.InfinitiveWindowOpen(
                level, windowGauge, CanFire(context.ActionService, level, BSTActions.Rally, player.GameObjectId));

        LogInfinitiveState(context, level, finisher, finishing, targetId);

        var paired = UsesPairing(context, level, out var answer);
        if (finishing)
            Push(context, scheduler, finisher!, targetId, priority: 2, why: "Universality (opposite half)");
        else if (window)
            context.Debug.InstinctState = context.Gauge!.Value.Tp >= ArtemisGauge.MaxTp
                ? "infinitive combo: finisher next (axes recasting)"
                : "infinitive combo: Rally next";
        else if (paired)
            PushPaired(context, scheduler, targetId, level, answer!);
        else
            PushInstinctual(context, scheduler, targetId, level);

        _releaseOwed = false;
        PushFamiliarOrders(context, scheduler, targetId, level, trickHandledByPairing: paired || window,
            targetHpPct: target.MaxHp > 0 ? (float)target.CurrentHp / target.MaxHp : 1f);

        var edgeDistance = System.Numerics.Vector3.Distance(player.Position, target.Position)
                           - target.HitboxRadius - player.HitboxRadius;
        PushCooldowns(context, scheduler, targetId, level, edgeDistance, paired);
        PushCrucibleOrders(context, scheduler, target);
    }

    /// <summary>
    /// The Crucible's two duty actions, which decide who takes the hits: the familiar tanks — Snarl on cooldown, its
    /// Cover takes all the beastmaster's damage — and Challenge only rescues a familiar about to fall.
    /// Only on a Crucible board, and only while the duty has them on its bar. Rules in <see cref="ArtemisCooldownRules"/>.
    /// </summary>
    private void PushCrucibleOrders(
        IArtemisContext context, RotationScheduler scheduler, Dalamud.Game.ClientState.Objects.Types.IBattleChara target)
    {
        if (!Daedalus.Services.Beastmaster.CrucibleTracker.Current.OnBoard)
            return;

        var cfg = context.Configuration.Beastmaster;
        var svc = context.ActionService;
        var player = context.Player;
        var targetId = target.GameObjectId;
        var familiar = Daedalus.Services.Beastmaster.FamiliarLookup.Find(context.ObjectTable, player);
        // A familiar the lookup cannot find while one is out (context.HasFamiliar) used to read as 0 HP, which blocks
        // Snarl and looks exactly like the 20-25% dead band. The game's own status check still gates the press.
        var lookupMissed = familiar == null && context.HasFamiliar;
        var familiarOut = familiar != null || context.HasFamiliar;
        var familiarHpPct = familiar is { MaxHp: > 0 } f ? (float)f.CurrentHp / f.MaxHp : lookupMissed ? 1f : 0f;
        var selfHpPct = player.MaxHp > 0 ? (float)player.CurrentHp / player.MaxHp : 0f;
        var enemyOnFamiliar = familiar != null && target.TargetObjectId == familiar.GameObjectId;

        bool Ready(ActionDefinition action) =>
            DutyActionBar.Contains(action.ActionId)
            && svc.IsActionReady(action.ActionId)
            && svc.GetActionStatusCode(action.ActionId, targetId) == 0;

        var legendary = cfg.CrucibleGoal == CrucibleTeamGoal.Legendary;
        if (legendary && PushSirenOrders(context, scheduler, cfg, familiar, familiarOut, familiarHpPct, selfHpPct))
            return;
        // Every goal clears the Siren's adds in one hit: on a Level or Clear run they wear the familiars down fast
        // (user, 2026-10-31: a roommate kept dying there), and no bonus is at stake. The usual rules go on as well.
        if (!legendary && FindSiren(context, out _, out var sirenAdds) is { } levelSiren)
            KillSirenAdds(context, scheduler, levelSiren, sirenAdds, familiarOut);

        // Legendary, shared tanking: whoever has more HP to spare holds the enemy. The beastmaster's turn is handled here;
        // the familiar's turn is the usual path below (Snarl onto it).
        if (legendary && cfg.CrucibleLegendarySharedTank
            && ArtemisCooldownRules.BeastmasterHoldsShared(selfHpPct, familiarOut, familiarHpPct))
        {
            LogSnarlState($"you hold the enemy (shared tanking): you {selfHpPct:P0}, familiar {familiarHpPct:P0}",
                familiarHpPct, selfHpPct);
            var busterSoon = Daedalus.Services.Beastmaster.CrucibleHints.SecondsUntilTankbuster;
            var raidwideSoon = Daedalus.Services.Beastmaster.CrucibleHints.SecondsUntilRaidwide;
            if (ArtemisCooldownRules.ShouldSnarlWhileHoldingShared(
                    cfg.EnableSnarl, Ready(BSTActions.Snarl), familiarOut, familiarHpPct, _fallingLine, busterSoon, raidwideSoon)
                && TryGate(BSTActions.Snarl.ActionId))
            {
                scheduler.PushOgcd(ArtemisAbilities.Snarl, targetId, priority: 3, onDispatched: _ =>
                    context.Debug.PlannedAction = "Snarl (a tankbuster or raidwide — the familiar takes it)");
                return;
            }

            if (ArtemisCooldownRules.ShouldChallengeToTank(
                    cfg.EnableChallenge, onBoardInBattle: true, Ready(BSTActions.Challenge),
                    target.TargetObjectId == player.GameObjectId, selfHpPct)
                && TryGate(BSTActions.Challenge.ActionId))
            {
                scheduler.PushOgcd(ArtemisAbilities.Challenge, targetId, priority: 6, onDispatched: _ =>
                    context.Debug.PlannedAction = "Challenge (shared tanking — your turn to hold it)");
            }

            return;
        }

        // Legendary with the beastmaster tanking (Meoni's way): Challenge keeps every enemy on you so the familiars end at
        // full HP (Selfless); Snarl only rescues a critical beastmaster.
        if (legendary && cfg.CrucibleLegendaryBeastmasterTanks && !cfg.CrucibleLegendarySharedTank)
        {
            LogSnarlState(selfHpPct <= ArtemisCooldownRules.SelfCriticalHpPct ? null : "the beastmaster tanks (Legendary)",
                familiarHpPct, selfHpPct);
            if (ArtemisCooldownRules.ShouldSnarlWhileTanking(
                    cfg.EnableSnarl, onBoardInBattle: true, Ready(BSTActions.Snarl), familiarOut, familiarHpPct, selfHpPct)
                && TryGate(BSTActions.Snarl.ActionId))
            {
                scheduler.PushOgcd(ArtemisAbilities.Snarl, targetId, priority: 3, onDispatched: _ =>
                    context.Debug.PlannedAction = "Snarl (you are critical — the familiar covers you)");
                return;
            }

            if (ArtemisCooldownRules.ShouldChallengeToTank(
                    cfg.EnableChallenge, onBoardInBattle: true, Ready(BSTActions.Challenge),
                    target.TargetObjectId == player.GameObjectId, selfHpPct)
                && TryGate(BSTActions.Challenge.ActionId))
            {
                scheduler.PushOgcd(ArtemisAbilities.Challenge, targetId, priority: 6, onDispatched: _ =>
                    context.Debug.PlannedAction = "Challenge (you tank — the familiars stay full for Selfless)");
            }

            return;
        }

        LogSnarlState(lookupMissed
            ? "familiar out but not found in the object table — Snarl allowed"
            : ArtemisCooldownRules.SnarlHeldReason(cfg.EnableSnarl, familiarOut, familiarHpPct, selfHpPct, _fallingLine,
                Daedalus.Services.Beastmaster.CrucibleHints.SecondsUntilTankbuster,
                Daedalus.Services.Beastmaster.CrucibleHints.SecondsUntilRaidwide),
            familiarHpPct, selfHpPct);

        var busterIn = Daedalus.Services.Beastmaster.CrucibleHints.SecondsUntilTankbuster;
        var raidwideIn = Daedalus.Services.Beastmaster.CrucibleHints.SecondsUntilRaidwide;
        if (ArtemisCooldownRules.ShouldSnarl(
                cfg.EnableSnarl, onBoardInBattle: true, Ready(BSTActions.Snarl), familiarOut, familiarHpPct, selfHpPct, busterIn,
                _fallingLine, raidwideIn)
            && TryGate(BSTActions.Snarl.ActionId))
        {
            var buster = busterIn <= ArtemisCooldownRules.TankbusterLeadSeconds;
            var raidwide = raidwideIn <= ArtemisCooldownRules.RaidwideLeadSeconds;
            var critical = selfHpPct <= ArtemisCooldownRules.SelfCriticalHpPct;
            scheduler.PushOgcd(ArtemisAbilities.Snarl, targetId, priority: buster || raidwide || critical ? 3 : 6, onDispatched: _ =>
                context.Debug.PlannedAction = buster
                    ? $"Snarl (tankbuster in {busterIn:0.0}s — the familiar takes it)"
                    : raidwide
                        ? $"Snarl (raidwide in {raidwideIn:0.0}s — Cover takes your share)"
                        : critical
                            ? "Snarl (you are critical — the familiar covers you)"
                            : "Snarl (familiar tanks and covers you)");
            return;
        }

        if (ArtemisCooldownRules.ShouldChallenge(
                cfg.EnableChallenge, onBoardInBattle: true, Ready(BSTActions.Challenge), familiar != null,
                enemyOnFamiliar, familiarHpPct, selfHpPct)
            && TryGate(BSTActions.Challenge.ActionId))
        {
            scheduler.PushOgcd(ArtemisAbilities.Challenge, targetId, priority: 7, onDispatched: _ =>
                context.Debug.PlannedAction = "Challenge (save the familiar)");
        }
    }

    /// <summary>
    /// Legendary against the Siren: the familiar holds her through Summon and while a Crawling Piece lives, the
    /// beastmaster the rest of the time (<see cref="ArtemisCooldownRules.SirenFamiliarHolds"/>). Both orders go at the
    /// Siren, whatever the hard target is. False (the usual rules) when she is not here, or the beastmaster is critical.
    /// </summary>
    private bool PushSirenOrders(
        IArtemisContext context, RotationScheduler scheduler, Daedalus.Config.DPS.BeastmasterConfig cfg,
        Dalamud.Game.ClientState.Objects.Types.IBattleChara? familiar, bool familiarOut, float familiarHpPct, float selfHpPct)
    {
        if (FindSiren(context, out var crawlerAlive, out var adds) is not { } siren)
            return false;

        KillSirenAdds(context, scheduler, siren, adds, familiarOut);
        if (selfHpPct <= ArtemisCooldownRules.SelfCriticalHpPct)
            return false;

        var svc = context.ActionService;
        var sirenId = siren.GameObjectId;
        bool ReadyOn(ActionDefinition action) =>
            DutyActionBar.Contains(action.ActionId)
            && svc.IsActionReady(action.ActionId)
            && svc.GetActionStatusCode(action.ActionId, sirenId) == 0;

        var summoning = siren.IsCasting && siren.CastActionId == ArtemisCooldownRules.SirenSummonActionId;
        if (ArtemisCooldownRules.SirenFamiliarHolds(summoning, crawlerAlive) && familiarOut && familiarHpPct >= _fallingLine)
        {
            LogSnarlState($"Siren: {(summoning ? "Summon" : "a crawler is up")} — the familiar holds her", familiarHpPct, selfHpPct);
            if (cfg.EnableSnarl && ReadyOn(BSTActions.Snarl)
                && (familiar == null || siren.TargetObjectId != familiar.GameObjectId)
                && TryGate(BSTActions.Snarl.ActionId))
            {
                scheduler.PushOgcd(ArtemisAbilities.Snarl, sirenId, priority: 3, onDispatched: _ =>
                    context.Debug.PlannedAction = "Snarl (Siren: the familiar holds her while her crawler is up)");
            }

            return true;
        }

        LogSnarlState("Siren: no crawler — you hold her", familiarHpPct, selfHpPct);
        if (ArtemisCooldownRules.ShouldChallengeToTank(
                cfg.EnableChallenge, onBoardInBattle: true, ReadyOn(BSTActions.Challenge),
                siren.TargetObjectId == context.Player.GameObjectId, selfHpPct)
            && TryGate(BSTActions.Challenge.ActionId))
        {
            scheduler.PushOgcd(ArtemisAbilities.Challenge, sirenId, priority: 6, onDispatched: _ =>
                context.Debug.PlannedAction = "Challenge (Siren: you hold her until the next Summon)");
        }

        return true;
    }

    /// <summary>The live Zu's HP (fraction of max) and where its unbroken eggs sit; null HP when no Zu is in the fight.</summary>
    private static float? ReadZu(IArtemisContext context, out List<System.Numerics.Vector2> eggs)
    {
        float? zuHpPct = null;
        eggs = new List<System.Numerics.Vector2>();
        foreach (var obj in context.ObjectTable)
        {
            if (obj is not Dalamud.Game.ClientState.Objects.Types.IBattleNpc npc || npc.IsDead || npc.CurrentHp == 0)
                continue;
            if (npc.NameId == ArtemisCooldownRules.ZuNameId && npc.IsTargetable && npc.MaxHp > 0)
                zuHpPct = (float)npc.CurrentHp / npc.MaxHp; // the targetable one: like the Siren, it has invisible helpers
            else if (FFXIVConstants.NeverAttackNameIds.Contains(npc.NameId))
                eggs.Add(new System.Numerics.Vector2(npc.Position.X, npc.Position.Z));
        }

        return zuHpPct;
    }

    /// <summary>The Siren (the targetable one), whether a Crawling Piece lives, and where her adds stand; null when she is not here.</summary>
    private static Dalamud.Game.ClientState.Objects.Types.IBattleNpc? FindSiren(
        IArtemisContext context, out bool crawlerAlive, out List<(ulong Id, System.Numerics.Vector2 At)> adds)
    {
        Dalamud.Game.ClientState.Objects.Types.IBattleNpc? siren = null;
        crawlerAlive = false;
        adds = new List<(ulong Id, System.Numerics.Vector2 At)>();
        foreach (var obj in context.ObjectTable)
        {
            if (obj is not Dalamud.Game.ClientState.Objects.Types.IBattleNpc npc || npc.IsDead || npc.CurrentHp == 0)
                continue;
            if (npc.NameId == ArtemisCooldownRules.CrawlingPieceNameId)
                crawlerAlive = true;
            else if (npc.NameId == ArtemisCooldownRules.SirenNameId && npc.IsTargetable && (siren == null || npc.MaxHp > siren.MaxHp))
                siren = npc; // the targetable one: she has invisible helpers of the same name
            if (npc.NameId is ArtemisCooldownRules.CrawlingPieceNameId or ArtemisCooldownRules.ShamblingPieceNameId)
                adds.Add((npc.GameObjectId, new System.Numerics.Vector2(npc.Position.X, npc.Position.Z)));
        }

        return siren;
    }

    /// <summary>
    /// Kill the Siren's adds in one hit once they have bunched. Parting Blow first while it is ready (free, and it killed
    /// all five on 2026-10-31); it waits for them inside its 8 yalms. Otherwise put the hard target on one that catches
    /// them all and tell the autopilot a Fang can go
    /// (<see cref="Daedalus.Services.Beastmaster.CrucibleHints.FangTargetId"/>). Every goal: on Legendary the Fang costs
    /// Economic Ethos and is worth it for no crawler on the beastmaster (the user's call); elsewhere it is free.
    /// </summary>
    private void KillSirenAdds(
        IArtemisContext context, RotationScheduler scheduler, Dalamud.Game.ClientState.Objects.Types.IBattleNpc siren,
        List<(ulong Id, System.Numerics.Vector2 At)> adds, bool familiarOut)
    {
        var hard = context.TargetingService.GetGameHardTargetId();
        var candidates = new List<(ulong Id, System.Numerics.Vector2 At)>(adds)
        {
            (siren.GameObjectId, new System.Numerics.Vector2(siren.Position.X, siren.Position.Z)),
        };
        Daedalus.Services.Beastmaster.CrucibleHints.FangAimUtc = DateTime.UtcNow;
        Daedalus.Services.Beastmaster.CrucibleHints.FangTargetId = 0;

        var level = context.Player.Level;
        var svc = context.ActionService;
        var blowReady = familiarOut && ActionAvailability.MeetsLevelAndLearned(level, svc, BSTActions.PartingBlow)
                        && svc.IsActionReady(BSTActions.PartingBlow.ActionId);
        if (blowReady)
        {
            // Release first: it grants Lingering Vantage, which lifts the Blow from 1,000 to 1,500 (user, 2026-10-31).
            // Gone off the instant the adds appear, so the Vantage is up by the time they bunch. Not Wespe's Final
            // Sting, which would send it away before the Blow (ShouldTemperedRelease refuses it with no Parting Blow).
            var hasVantage = false;
            foreach (var status in context.Player.StatusList)
                hasVantage |= status.StatusId == ArtemisCooldownRules.LingeringVantageStatusId;
            var selfHpPct = context.Player.MaxHp > 0 ? (float)context.Player.CurrentHp / context.Player.MaxHp : 1f;
            if (!hasVantage && adds.Count > 0
                && ShouldTemperedRelease(
                    context.Configuration.Beastmaster.EnableTemperedRelease,
                    CanFire(svc, level, BSTActions.TemperedRelease, siren.GameObjectId), context.FamiliarBeast?.BestiaryNo,
                    partingBlowEnabled: false, selfHpPct, double.MaxValue))
            {
                if (TryGate(BSTActions.TemperedRelease.ActionId))
                {
                    scheduler.PushOgcd(ArtemisAbilities.TemperedRelease, siren.GameObjectId, priority: 2, onDispatched: _ =>
                        context.Debug.PlannedAction = "Tempered Release (Siren: Lingering Vantage for the Parting Blow)");
                }

                return;
            }

            var blowAim = CrucibleAutoPilotRules.FangAim(adds, candidates, hard, CrucibleAutoPilotRules.PartingBlowReachYalms);
            if (blowAim != 0 && CanFire(svc, level, BSTActions.PartingBlow, blowAim) && TryGate(BSTActions.PartingBlow.ActionId))
            {
                var count = adds.Count;
                scheduler.PushOgcd(ArtemisAbilities.PartingBlow, blowAim, priority: 2, onDispatched: _ =>
                {
                    context.Debug.PlannedAction = "Parting Blow (Siren: the adds are bunched)";
                    Daedalus.Rotation.Base.RotationServices.DebugLog?.Log(
                        Daedalus.Services.Debug.DebugLogCategory.Action, Daedalus.Services.Debug.DebugLogSeverity.Info,
                        $"[Crucible] Siren: Parting Blow on {count} bunched adds");
                });
            }

            return; // a ready Parting Blow is worth waiting for: no Fang while it can still do the job
        }

        var aim = CrucibleAutoPilotRules.FangAim(adds, candidates, hard);
        if (aim != 0 && aim != hard)
            context.TargetingService.EnsureHardTarget(aim);

        Daedalus.Services.Beastmaster.CrucibleHints.FangTargetId = aim != 0 && aim == hard ? aim : 0;
    }

    private string? _lastSnarlState;
    private bool _retreatHeldLogged;

    /// <summary>One line when a falling familiar is kept out because no other horn could replace it.</summary>
    private void LogRetreatHeld(bool held, float familiarHpPct)
    {
        if (held == _retreatHeldLogged)
            return;
        _retreatHeldLogged = held;
        if (held)
            Daedalus.Rotation.Base.RotationServices.DebugLog?.Log(
                Daedalus.Services.Debug.DebugLogCategory.Action, Daedalus.Services.Debug.DebugLogSeverity.Info,
                $"[Crucible] Parting Blow held: familiar at {familiarHpPct:P0} but no other horn is ready — it stays out so you are not left alone");
    }
    private string? _lastInfinitiveState;

    /// <summary>
    /// One debug-log line whenever the infinitive combo's state changes while a half is lit at level 50: TP, diamonds,
    /// familiar, and whether the finisher is going out or why not. A Rally followed by no Universality (field
    /// 2026-10-25, a second player) had nothing in the log to say which step failed.
    /// </summary>
    private void LogInfinitiveState(
        IArtemisContext context, byte level, ActionDefinition? finisher, bool finishing, ulong targetId)
    {
        if (level < ArtemisCooldownRules.UpgradeLevel || context.Gauge is not { HasHalf: true } gauge)
        {
            _lastInfinitiveState = null;
            return;
        }

        var svc = context.ActionService;
        string step;
        if (finishing)
            step = $"finisher {finisher!.Name} going out";
        else if (finisher != null)
            step = $"finisher {finisher.Name} not usable (status {svc.GetActionStatusCode(finisher.ActionId, targetId)})";
        else if (gauge.MasteredInstinct >= ArtemisCooldownRules.MaxMastered)
            step = CanFire(svc, level, BSTActions.Rally, context.Player.GameObjectId)
                ? "Rally next (makes the 250 TP)"
                : "Rally not ready — no 250 TP for the finisher";
        else
            step = "under 250 TP and under three diamonds — no finisher this window";

        var state = $"half {gauge.Affinity}, TP {gauge.Tp}, diamonds {gauge.MasteredInstinct}, "
                    + $"{(context.HasFamiliar ? "familiar out" : "no familiar")}: {step}";
        if (state == _lastInfinitiveState)
            return;
        _lastInfinitiveState = state;
        Daedalus.Rotation.Base.RotationServices.DebugLog?.Log(
            Daedalus.Services.Debug.DebugLogCategory.Action, Daedalus.Services.Debug.DebugLogSeverity.Info,
            $"[BST] infinitive combo: {state}");
    }

    /// <summary>The familiar's recent HP losses and the falling line they set (the Snarl rule reads it too).</summary>
    private readonly FamiliarHpTrend _familiarTrend = new();
    private float _fallingLine = ArtemisCooldownRules.FamiliarFallingHpPct;

    /// <summary>One debug-log line whenever why-Snarl-is-held changes (and when it is allowed again).</summary>
    private void LogSnarlState(string? heldReason, float familiarHpPct, float selfHpPct)
    {
        var state = heldReason ?? "allowed";
        if (state == _lastSnarlState)
            return;
        _lastSnarlState = state;
        Daedalus.Rotation.Base.RotationServices.DebugLog?.Log(
            Daedalus.Services.Debug.DebugLogCategory.Action,
            heldReason == null ? Daedalus.Services.Debug.DebugLogSeverity.Info : Daedalus.Services.Debug.DebugLogSeverity.Warning,
            heldReason == null
                ? $"[Crucible] Snarl allowed (familiar {familiarHpPct:P0}, you {selfHpPct:P0})"
                : $"[Crucible] Snarl held: {heldReason}");
    }

    /// <summary>
    /// Rally, Rallying Cheer, Shield Charge, Borrow and Beast Mode — the buttons that are neither the axe
    /// chain nor an order to the familiar. Rules in <see cref="ArtemisCooldownRules"/>.
    /// </summary>
    private void PushCooldowns(
        IArtemisContext context, RotationScheduler scheduler, ulong targetId, byte level,
        float edgeDistance, bool paired)
    {
        var svc = context.ActionService;
        var cfg = context.Configuration.Beastmaster;
        var player = context.Player;
        var selfId = player.GameObjectId;
        var selfHpPct = player.MaxHp > 0 ? (float)player.CurrentHp / player.MaxHp : 1f;
        var inMelee = edgeDistance <= ArtemisCooldownRules.MeleeYalms;

        // Out of TP: in reach, an axe is off cooldown, and the game still refuses every one of them.
        var anyAxeOffCooldown = false;
        var anyAxeUsable = false;
        foreach (var affinity in FallbackOrder)
        {
            var axe = BSTActions.InstinctualFor(affinity);
            if (axe == null || !ActionAvailability.MeetsLevelAndLearned(level, svc, axe))
                continue;

            anyAxeOffCooldown |= svc.IsActionReady(axe.ActionId);
            anyAxeUsable |= svc.GetActionStatusCode(axe.ActionId, targetId) == 0;
        }

        var axeStarved = inMelee && anyAxeOffCooldown && !anyAxeUsable;
        if (!axeStarved)
            _axeStarvedSinceUtc = DateTime.MinValue;
        else if (_axeStarvedSinceUtc == DateTime.MinValue)
            _axeStarvedSinceUtc = DateTime.UtcNow;

        // The gauge says how much TP there is and how many diamonds; without it, fall back to "the axes
        // are refused" and our own tally.
        if (context.HasFamiliar)
            _familiarGoneSinceUtc = DateTime.MinValue;
        else if (_familiarGoneSinceUtc == DateTime.MinValue)
            _familiarGoneSinceUtc = DateTime.UtcNow;

        var rallyReady = CanFire(svc, level, BSTActions.Rally, selfId);
        var rally = context.Gauge is { } rallyGauge
            ? ArtemisCooldownRules.ShouldRallyFromGauge(
                cfg.EnableRally, rallyReady, rallyGauge.Tp, rallyGauge.MasteredInstinct, level, context.HasFamiliar,
                rallyGauge.HasHalf, SecondsSince(_familiarGoneSinceUtc))
            : ArtemisCooldownRules.ShouldRally(
                cfg.EnableRally, rallyReady, SecondsSince(_axeStarvedSinceUtc), paired, _masteredEstimate);
        var rallyOpensFinisher = context.Gauge is { HasHalf: true } && level >= ArtemisCooldownRules.UpgradeLevel;
        if (rally && TryGate(BSTActions.Rally.ActionId))
        {
            // Inside the infinitive window Rally goes first: the finisher must follow within the 7 s.
            scheduler.PushOgcd(ArtemisAbilities.Rally, selfId, priority: rallyOpensFinisher ? 1 : 14, onDispatched: _ =>
            {
                _masteredEstimate = 0;
                context.Debug.PlannedAction = BSTActions.Rally.Name;
            });
        }

        // The same reading for the familiar's bar, off Trick.
        var trickStarved = context.HasFamiliar
            && ActionAvailability.MeetsLevelAndLearned(level, svc, BSTActions.Trick)
            && edgeDistance <= BSTActions.Trick.Range
            && svc.IsActionReady(BSTActions.Trick.ActionId)
            && svc.GetActionStatusCode(BSTActions.Trick.ActionId, targetId) != 0;
        if (!trickStarved)
            _trickStarvedSinceUtc = DateTime.MinValue;
        else if (_trickStarvedSinceUtc == DateTime.MinValue)
            _trickStarvedSinceUtc = DateTime.UtcNow;

        var cheerReady = CanFire(svc, level, BSTActions.RallyingCheer, selfId);
        var cheer = context.Gauge is { } cheerGauge
            ? ArtemisCooldownRules.ShouldRallyingCheerFromGauge(
                cfg.EnableRally, cheerReady, context.HasFamiliar, cheerGauge.PetTp)
            : ArtemisCooldownRules.ShouldRallyingCheer(
                cfg.EnableRally, cheerReady, context.HasFamiliar, SecondsSince(_trickStarvedSinceUtc));
        if (cheer && TryGate(BSTActions.RallyingCheer.ActionId))
        {
            scheduler.PushOgcd(ArtemisAbilities.RallyingCheer, selfId, priority: 14,
                onDispatched: _ => context.Debug.PlannedAction = BSTActions.RallyingCheer.Name);
        }

        if (ArtemisCooldownRules.ShouldBorrow(
                cfg.EnableBorrow, CanFire(svc, level, BSTActions.Borrow, selfId), context.HasFamiliar, _releaseOwed)
            && TryGate(BSTActions.Borrow.ActionId))
        {
            scheduler.PushOgcd(ArtemisAbilities.Borrow, selfId, priority: 16,
                onDispatched: _ => context.Debug.PlannedAction = BSTActions.Borrow.Name);
        }

        // Beast Mode is whatever the Kinship has made of it; ask the game about THAT action, at the target
        // it takes.
        var modeId = svc.GetAdjustedActionId(BSTActions.BeastMode.ActionId);
        var use = ArtemisCooldownRules.DecideBeastMode(
            cfg.EnableBorrow, modeId, selfHpPct, edgeDistance,
            context.DebuffDetectionService.FindHighestPriorityDebuff(player).priority
                != Daedalus.Services.Debuff.DebuffPriority.None);
        // Soul Crush interrupts: aimed at a caster the boss engine says to stop, ahead of its free-damage use.
        if (modeId == BSTActions.SoulCrushId && cfg.EnableBorrow
            && ActionAvailability.MeetsLevelAndLearned(level, svc, BSTActions.BeastMode)
            && Daedalus.Services.Beastmaster.CrucibleHints.TargetsToInterrupt.Count > 0)
        {
            var candidates = new System.Collections.Generic.List<(ulong, float, bool)>();
            foreach (var id in Daedalus.Services.Beastmaster.CrucibleHints.TargetsToInterrupt)
            {
                if (context.ObjectTable.SearchById(id) is Dalamud.Game.ClientState.Objects.Types.IBattleChara caster)
                    candidates.Add((id,
                        System.Numerics.Vector3.Distance(player.Position, caster.Position) - caster.HitboxRadius - player.HitboxRadius,
                        caster.IsCasting));
            }

            var interruptId = ArtemisCooldownRules.PickInterruptTarget(candidates);
            if (interruptId != 0 && svc.GetActionStatusCode(modeId, interruptId) == 0 && TryGate(BSTActions.BeastMode.ActionId))
            {
                var casterName = context.ObjectTable.SearchById(interruptId)?.Name.TextValue ?? "enemy";
                scheduler.PushOgcd(ArtemisAbilities.BeastModeAtEnemy, interruptId, priority: 2, onDispatched: _ =>
                {
                    context.Debug.PlannedAction = $"Soul Crush (interrupt {casterName})";
                    Daedalus.Rotation.Base.RotationServices.DebugLog?.Log(
                        Daedalus.Services.Debug.DebugLogCategory.Action, Daedalus.Services.Debug.DebugLogSeverity.Info,
                        $"[BST] Soul Crush: interrupting {casterName}");
                });
                use = BeastModeUse.None; // the button is spent on the interrupt this frame
            }
        }

        if (use != BeastModeUse.None && ActionAvailability.MeetsLevelAndLearned(level, svc, BSTActions.BeastMode))
        {
            var modeTarget = use == BeastModeUse.Enemy ? targetId : selfId;
            if (svc.GetActionStatusCode(modeId, modeTarget) == 0 && TryGate(BSTActions.BeastMode.ActionId))
            {
                scheduler.PushOgcd(
                    use == BeastModeUse.Enemy ? ArtemisAbilities.BeastModeAtEnemy : ArtemisAbilities.BeastMode,
                    modeTarget, priority: 18,
                    onDispatched: _ => context.Debug.PlannedAction = "Beast Mode (Kinship action)");
            }
        }

        var target = context.TargetingService.GetUserEnemyTarget() ?? context.TargetingService.GetRawEnemyHardTarget();
        var dashSafe = target != null && target.GameObjectId == targetId
            && !context.TargetingService.GapCloserSafety.ShouldBlockGapCloser(target, player);
        var charge = ArtemisCooldownRules.DecideShieldCharge(
            cfg.EnableShieldCharge, cfg.EnableShieldChargeGapClose, CanFire(svc, level, BSTActions.ShieldCharge, targetId),
            (int)svc.GetCurrentCharges(BSTActions.ShieldCharge.ActionId), edgeDistance, BSTActions.ShieldCharge.Range, dashSafe);
        var eggInSplash = charge != ShieldChargeUse.None && target != null
            && Daedalus.Services.Beastmaster.CrucibleTracker.Current.OnBoard
            && ReadZu(context, out var eggs) != null
            && ArtemisCooldownRules.SplashBreaksEgg(eggs, new System.Numerics.Vector2(target.Position.X, target.Position.Z),
                ArtemisCooldownRules.ShieldChargeSplashYalms);
        if (eggInSplash)
        {
            context.Debug.DamageState = "Shield Charge held: a Zu egg is in its splash";
        }
        else if (charge != ShieldChargeUse.None && TryGate(BSTActions.ShieldCharge.ActionId))
        {
            scheduler.PushOgcd(ArtemisAbilities.ShieldCharge, targetId, priority: charge == ShieldChargeUse.GapClose ? 4 : 22,
                onDispatched: _ => context.Debug.PlannedAction = charge == ShieldChargeUse.GapClose
                    ? "Shield Charge (gap close)"
                    : BSTActions.ShieldCharge.Name);
        }
        else if (edgeDistance >= ArtemisCooldownRules.ShieldChargeGapYalms && !dashSafe && cfg.EnableShieldChargeGapClose)
        {
            context.Debug.DamageState = $"Shield Charge held: {context.TargetingService.GapCloserSafety.LastBlockReason ?? "no explicit target"}";
        }
    }

    /// <summary>
    /// Pair Trick with the axe when: a familiar is out and identified, Trick is learned, and the axe
    /// that answers its colour is learned (Gale Axe, the Volant answer to an Eldritch familiar, only
    /// arrives at level 16).
    /// </summary>
    private static bool UsesPairing(IArtemisContext context, byte level, out ActionDefinition? answer)
    {
        answer = null;
        var cfg = context.Configuration.Beastmaster;
        if (!cfg.EnableTrick || !cfg.EnableInstinctualSkills || !context.HasFamiliar
            || context.FamiliarBeast is not { } beast)
            return false;

        var svc = context.ActionService;
        if (!ActionAvailability.MeetsLevelAndLearned(level, svc, BSTActions.Trick))
            return false;

        answer = BSTActions.InstinctualFor(BSTActions.NextInCycle(beast.TrickAffinity));
        return answer != null && ActionAvailability.MeetsLevelAndLearned(level, svc, answer);
    }

    /// <summary>What the pairing does this frame. Pure, so the rules can be tested without a game.</summary>
    public enum PairingStep
    {
        /// <summary>A Trick is out and its answer is due: press the clockwise axe.</summary>
        AnswerTrick,
        /// <summary>A Trick is out but the familiar has not acted yet: press nothing.</summary>
        WaitForFamiliar,
        /// <summary>Both halves are ready: send Trick; the axe follows once the familiar acts.</summary>
        SendTrick,
        /// <summary>One half is ready and the other is not: hold it for the pair.</summary>
        Hold,
        /// <summary>The axe waited too long for the Trick: send it alone rather than cap TP.</summary>
        AxeAlone,
        /// <summary>The Trick waited too long for the axe: send it alone rather than cap Pet TP.</summary>
        TrickAlone,
        /// <summary>Neither half is ready.</summary>
        Nothing,
    }

    public static PairingStep DecidePairing(
        bool awaitingAnswer, bool readyToAnswer, bool axeReady, bool trickReady, bool holdExpired)
    {
        if (awaitingAnswer)
            return readyToAnswer ? PairingStep.AnswerTrick : PairingStep.WaitForFamiliar;
        if (axeReady && trickReady)
            return PairingStep.SendTrick;
        if (axeReady)
            return holdExpired ? PairingStep.AxeAlone : PairingStep.Hold;
        if (trickReady)
            return holdExpired ? PairingStep.TrickAlone : PairingStep.Hold;
        return PairingStep.Nothing;
    }

    /// <summary>
    /// Trick first, then the clockwise axe once the familiar has acted: an intentional combo finished by
    /// our own skill, which banks Mastered Instinct for Rally. Pressing the axe straight after Trick
    /// resolves it first (the familiar is slow to act) and the combo comes out backwards.
    /// </summary>
    private void PushPaired(
        IArtemisContext context, RotationScheduler scheduler, ulong targetId, byte level, ActionDefinition answer)
    {
        var svc = context.ActionService;
        var pairing = context.TrickPairing;
        var beast = context.FamiliarBeast!;

        var awaiting = pairing.IsAwaitingAnswer;
        if (!awaiting)
            _gaugeAtTrick = null;

        // With the gauge, the familiar's skill is SEEN landing (the Compass moves) and the answer is the
        // clockwise neighbour of whatever is actually lit — not the colour a table says this beast uses,
        // and not a guess at how long the familiar takes to act.
        var landed = awaiting && context.Gauge is { } now && _gaugeAtTrick is { } before
            && ArtemisCooldownRules.GaugeMovedSinceTrick(before, now);
        var gaugeAnswer = landed ? ArtemisCooldownRules.AnswerFromGauge(context.Gauge!.Value) : InstinctAffinity.None;
        var answerAffinity = gaugeAnswer != InstinctAffinity.None ? gaugeAnswer : pairing.AnswerAffinity;

        var pendingAnswer = awaiting ? BSTActions.InstinctualFor(answerAffinity) : answer;
        var axeReady = pendingAnswer != null && CanFire(svc, level, pendingAnswer, targetId);
        var trickReady = !awaiting && CanFire(svc, level, BSTActions.Trick, targetId);
        var oneSideReady = !awaiting && axeReady != trickReady;
        var holdExpired = oneSideReady && pairing.HoldExpired();
        if (!oneSideReady)
            pairing.ClearHold();

        var step = DecidePairing(awaiting, landed || pairing.ReadyToAnswer, axeReady, trickReady, holdExpired);
        switch (step)
        {
            case PairingStep.AnswerTrick when axeReady:
                Push(context, scheduler, pendingAnswer!, targetId, priority: 10,
                    why: $"{pairing.PetAffinity} Trick → {answerAffinity}" + (landed ? " (seen on the gauge)" : ""),
                    onDispatched: () =>
                    {
                        pairing.OnAnswered();
                        // Trick answered by our own axe is an intentional combo we finished: a diamond.
                        _masteredEstimate = Math.Min(_masteredEstimate + 1, ArtemisCooldownRules.MaxMastered);
                    });
                break;
            case PairingStep.AnswerTrick:
                context.Debug.InstinctState = $"answer {pairing.AnswerAffinity} not ready";
                break;
            case PairingStep.WaitForFamiliar:
                context.Debug.InstinctState = $"waiting for the {beast.Name} Trick to land";
                break;
            case PairingStep.SendTrick:
            case PairingStep.TrickAlone:
                PushTrick(context, scheduler, targetId, beast.TrickAffinity,
                    step == PairingStep.SendTrick
                        ? $"Trick ({beast.TrickAffinity}) → {answer.Name}"
                        : "Trick alone (axe not ready)");
                break;
            case PairingStep.AxeAlone:
                Push(context, scheduler, answer, targetId, priority: 25, why: "alone (Trick not ready)",
                    onDispatched: pairing.ClearHold);
                break;
            case PairingStep.Hold:
                context.Debug.InstinctState = axeReady ? "holding axe for the Trick" : "holding Trick for the axe";
                break;
            default:
                context.Debug.InstinctState = "none ready";
                break;
        }

        context.Debug.InstinctChain = pairing.Describe();
    }

    private void PushTrick(
        IArtemisContext context, RotationScheduler scheduler, ulong targetId, InstinctAffinity petAffinity, string why)
    {
        // The familiar that was just sent off with Parting Blow is still in the object table (RSR 5ffd2205).
        if (context.ActionService.WasLastOgcd(BSTActions.PartingBlow.ActionId)) return;
        scheduler.PushOgcd(ArtemisAbilities.Trick, targetId, priority: 10, onDispatched: _ =>
        {
            _gaugeAtTrick = context.Gauge;
            context.Instinct.Record(petAffinity);
            context.TrickPairing.OnTrickDispatched(petAffinity);
            context.Debug.PlannedAction = BSTActions.Trick.Name;
            context.Debug.InstinctState = why;
            context.Debug.InstinctChain = context.TrickPairing.Describe();
        });
    }

    /// <summary>
    /// Smash Axe to Axeblade Bite to Shieldsplitter. Finishers get a stronger priority than the
    /// starter so a live combo is never restarted from the top, and each is gated on combo step
    /// rather than target count — the chain is single-target only, and there is no AoE combo.
    /// </summary>
    private static void PushComboChain(
        IArtemisContext context, RotationScheduler scheduler, ulong targetId, byte level)
    {
        var svc = context.ActionService;

        if (context.ComboStep == 2
            && ActionAvailability.MeetsLevelAndLearned(level, svc, BSTActions.Shieldsplitter))
        {
            scheduler.PushGcd(ArtemisAbilities.Shieldsplitter, targetId, priority: 10,
                onDispatched: _ => context.Debug.PlannedAction = BSTActions.Shieldsplitter.Name);
            return;
        }

        if (context.ComboStep == 1
            && ActionAvailability.MeetsLevelAndLearned(level, svc, BSTActions.AxebladeBite))
        {
            scheduler.PushGcd(ArtemisAbilities.AxebladeBite, targetId, priority: 11,
                onDispatched: _ => context.Debug.PlannedAction = BSTActions.AxebladeBite.Name);
            return;
        }

        scheduler.PushGcd(ArtemisAbilities.SmashAxe, targetId, priority: 20,
            onDispatched: _ => context.Debug.PlannedAction = BSTActions.SmashAxe.Name);
    }

    /// <summary>
    /// Pick one instinctual skill: first the clockwise successor if it will fire (an intentional
    /// combo), otherwise any instinctual skill at all. Taking an off-order spend over dropping the
    /// chain is deliberate — the gauge description states that the chain length itself raises combo
    /// potency, so breaking a long chain to wait for the "right" affinity loses more than it gains.
    /// </summary>
    private static void PushInstinctual(
        IArtemisContext context, RotationScheduler scheduler, ulong targetId, byte level)
    {
        var cfg = context.Configuration.Beastmaster;
        if (!cfg.EnableInstinctualSkills)
            return;

        var svc = context.ActionService;

        if (cfg.PreferIntentionalCombos)
        {
            var wanted = context.Instinct.PreferredNext;
            var preferred = BSTActions.InstinctualFor(wanted);
            if (preferred != null && CanFire(svc, level, preferred, targetId))
            {
                var combo = context.Instinct.ComboFrom(wanted);
                Push(context, scheduler, preferred, targetId, priority: 10, why: $"{wanted} → {combo}");
                return;
            }
        }

        foreach (var affinity in FallbackOrder)
        {
            var action = BSTActions.InstinctualFor(affinity);
            if (action == null || !CanFire(svc, level, action, targetId))
                continue;

            Push(context, scheduler, action, targetId, priority: 25, why: $"{affinity} (off-order)");
            return;
        }

        context.Debug.InstinctState = "none ready";
    }

    /// <summary>Status code 0 means the game accepts it — which subsumes TP, recast and range.</summary>
    private static bool CanFire(IActionService svc, byte level, ActionDefinition action, ulong targetId)
        => ActionAvailability.MeetsLevelAndLearned(level, svc, action)
           && svc.GetActionStatusCode(action.ActionId, targetId) == 0;

    private static readonly InstinctAffinity[] FallbackOrder =
    [
        InstinctAffinity.Volant, InstinctAffinity.Rampant,
        InstinctAffinity.Durant, InstinctAffinity.Eldritch,
    ];

    private static void Push(
        IArtemisContext context, RotationScheduler scheduler,
        ActionDefinition action, ulong targetId, int priority, string why,
        System.Action? onDispatched = null)
    {
        var behavior = ArtemisAbilities.InstinctualFor(BSTActions.AffinityOf(action.ActionId));
        if (behavior == null)
            return;

        scheduler.PushOgcd(behavior, targetId, priority, onDispatched: ctx =>
        {
            // Record what the game ACTUALLY fired, not the button we pressed. At higher levels the
            // base skills substitute into Brutal Rage / Hawkish Talons / Risen Fall / Calamity,
            // whose affinities are Sunstrider and Moonstalker rather than the four Hearts — so
            // recording the base affinity would corrupt the chain the moment the upgrade unlocks.
            var fired = ctx.ActionService.GetAdjustedActionId(action.ActionId);
            var affinity = BSTActions.AffinityOf(fired);
            if (affinity == InstinctAffinity.None)
                affinity = BSTActions.AffinityOf(action.ActionId);

            context.Instinct.Record(affinity);
            onDispatched?.Invoke();
            context.Debug.PlannedAction = action.Name;
            context.Debug.InstinctState = why;
            context.Debug.InstinctChain = context.Instinct.Describe();
        });
    }

    /// <summary>Wespe's Tempered Release is Final Sting: 2,000-3,000 potency, and the familiar retreats.</summary>
    public const int WespeBestiaryNo = 10;

    /// <summary>
    /// In the Crucible, Final Sting is the kill shot: at or below this much target HP (its potency grows as the target's
    /// HP falls, 2,000 to 3,000). Earlier and the fight drags on without the Wespe (Meoni's Legendary runs).
    /// </summary>
    public const float FinalStingAtTargetHpPct = 0.30f;

    /// <summary>
    /// In the Crucible a Wespe's once-per-summon charge (One with Nature) is kept for Final Sting, so Borrow and a plain
    /// Parting Blow wait for it: Borrow spends the same charge. Korha 2026-10-27, Pas de Seul: the sting was rightly held
    /// above 30%, Borrow went 10s after the summon, and the Wespe never stung.
    /// </summary>
    public static bool SavesFinalSting(bool onBoard, int? bestiaryNo) => onBoard && bestiaryNo == WespeBestiaryNo;

    /// <summary>Sabotender's is Hypodermic Hustle: party +20% damage, paid for with a DoT on the beastmaster.</summary>
    public const int SabotenderBestiaryNo = 22;

    /// <summary>Below this, Hypodermic Hustle's self-damage is not worth the buff.</summary>
    public const float HustleMinHpPct = 0.50f;

    /// <summary>
    /// Gap between tries. The game can say "ready" and still refuse the press, and a refusal changes nothing
    /// the rule can see — so without a gap it is offered again every frame: 30 "unable to cast" lines a
    /// second and an error toast, for as long as the fight lasts (field 2026-10-11).
    /// </summary>
    public const double ReleaseRetrySeconds = 2.0;

    /// <summary>When Tempered Release was last offered to the scheduler.</summary>
    private DateTime _lastReleaseTryUtc = DateTime.MinValue;

    /// <summary>When each of the other cooldowns was last offered — the same every-frame guard, per action.</summary>
    private readonly System.Collections.Generic.Dictionary<uint, DateTime> _lastTryUtc = [];

    /// <summary>A Tempered Release is still owed this summon; Borrow and Parting Blow wait for it.</summary>
    private bool _releaseOwed;

    /// <summary>
    /// Our own count of Mastered Instinct: one per Trick answered with the axe, emptied by Rally. The real
    /// count lives in the job HUD and nowhere readable, so this is an estimate and is only used to keep
    /// Rally from being thrown away on an empty set of diamonds.
    /// </summary>
    private int _masteredEstimate;

    private DateTime _axeStarvedSinceUtc = DateTime.MinValue;
    private DateTime _trickStarvedSinceUtc = DateTime.MinValue;
    private DateTime _familiarGoneSinceUtc = DateTime.MinValue;

    /// <summary>The gauge as it stood when Trick was sent, so the familiar's skill can be seen landing.</summary>
    private ArtemisGauge? _gaugeAtTrick;

    /// <summary>True, and the try is stamped, when this action has not been offered in the last two seconds.</summary>
    private bool TryGate(uint actionId)
    {
        var now = DateTime.UtcNow;
        if (_lastTryUtc.TryGetValue(actionId, out var last) && (now - last).TotalSeconds < ReleaseRetrySeconds)
            return false;

        _lastTryUtc[actionId] = now;
        return true;
    }

    private static double SecondsSince(DateTime since)
        => since == DateTime.MinValue ? 0d : (DateTime.UtcNow - since).TotalSeconds;

    /// <summary>
    /// Whether to order the familiar's signature move. Nearly all fifty are plain gains — damage, a debuff on
    /// the enemy, a party buff — so the rule is "when the game says it is ready". Two carry a cost and are
    /// held back: Final Sting sends the familiar away, which is the same trade as Parting Blow and so follows
    /// that setting, and Hypodermic Hustle hurts the beastmaster, so it waits for healthy HP. An unidentified
    /// familiar (<paramref name="bestiaryNo"/> null) counts as one of the plain ones.
    /// <para>In the Crucible Final Sting also goes as the finisher (<paramref name="stingFinisher"/>: the target at or
    /// below <see cref="FinalStingAtTargetHpPct"/>), whatever the Parting Blow setting.</para>
    /// </summary>
    public static bool ShouldTemperedRelease(
        bool enabled, bool ready, int? bestiaryNo, bool partingBlowEnabled, float selfHpPct,
        double secondsSinceLastTry, bool stingFinisher = false)
    {
        if (!enabled || !ready || secondsSinceLastTry < ReleaseRetrySeconds)
            return false;

        return bestiaryNo switch
        {
            WespeBestiaryNo => partingBlowEnabled || stingFinisher,
            SabotenderBestiaryNo => selfHpPct >= HustleMinHpPct,
            _ => true,
        };
    }

    /// <summary>
    /// Trick and Parting Blow both order the familiar, so neither is worth pushing without one out.
    /// <para>
    /// Parting Blow is opt-in. It is the biggest button in the kit — 1,000 potency across 8y — but
    /// the familiar RETREATS afterwards, which silently ends Trick until the player summons again.
    /// That is a trade the player should make, not one the rotation should make for them.
    /// </para>
    /// </summary>
    private void PushFamiliarOrders(
        IArtemisContext context, RotationScheduler scheduler, ulong targetId, byte level,
        bool trickHandledByPairing, float targetHpPct = 1f)
    {
        if (!context.HasFamiliar)
        {
            context.Debug.DamageState = "No familiar — combo chain only";
            return;
        }

        var svc = context.ActionService;
        var cfg = context.Configuration.Beastmaster;

        // Tempered Release: the beast's own signature move, once per summon. Sent at the enemy, as the
        // hotbar button would be — see BSTActions.TemperedRelease for why not at ourselves.
        var player = context.Player;

        // A familiar about to fall on a board: spend it first — release, then Parting Blow (it retreats instead of
        // being knocked out). See ArtemisCooldownRules.IsFamiliarFalling.
        var familiarObject = Daedalus.Services.Beastmaster.FamiliarLookup.Find(context.ObjectTable, player);
        var familiarHpPct = familiarObject is { MaxHp: > 0 } fam ? (float)fam.CurrentHp / fam.MaxHp : 1f;
        if (familiarObject is { MaxHp: > 0 })
            _familiarTrend.Sample(familiarObject.GameObjectId, familiarHpPct, DateTime.UtcNow);
        _fallingLine = ArtemisCooldownRules.FallingLine(_familiarTrend.BiggestHitPct, _familiarTrend.LossPerSecond);
        var falling = ArtemisCooldownRules.IsFamiliarFalling(
            cfg.CruciblePartingBlowWhenFalling, Daedalus.Services.Beastmaster.CrucibleTracker.Current.OnBoard, familiarHpPct,
            player.MaxHp > 0 ? (float)player.CurrentHp / player.MaxHp : 1f,
            Daedalus.Services.Beastmaster.CrucibleHints.SecondsUntilTankbuster, _fallingLine,
            Daedalus.Services.Beastmaster.CrucibleHints.SecondsUntilRaidwide, replacementReady: !context.NoReplacementHorn);
        LogRetreatHeld(context.NoReplacementHorn
                       && ArtemisCooldownRules.IsFamiliarFalling(
                           cfg.CruciblePartingBlowWhenFalling, Daedalus.Services.Beastmaster.CrucibleTracker.Current.OnBoard,
                           familiarHpPct, player.MaxHp > 0 ? (float)player.CurrentHp / player.MaxHp : 1f,
                           Daedalus.Services.Beastmaster.CrucibleHints.SecondsUntilTankbuster, _fallingLine,
                           Daedalus.Services.Beastmaster.CrucibleHints.SecondsUntilRaidwide),
            familiarHpPct);
        var partingBlowAllowed = ArtemisCooldownRules.AllowsPartingBlow(cfg.EnablePartingBlow, falling);

        var releaseReady = CanFire(svc, level, BSTActions.TemperedRelease, targetId);
        var stingFinisher = Daedalus.Services.Beastmaster.CrucibleTracker.Current.OnBoard
                            && targetHpPct > 0f && targetHpPct <= FinalStingAtTargetHpPct;
        var zuHold = ArtemisCooldownRules.HoldReleaseAtZu(
            Daedalus.Services.Beastmaster.CrucibleTracker.Current.OnBoard, ReadZu(context, out _), falling);
        if (zuHold && releaseReady)
            context.Debug.InstinctState = "Zu: release held (it breaks eggs) until the Zu is nearly dead";
        var release = !zuHold && ShouldTemperedRelease(
            cfg.EnableTemperedRelease, releaseReady, context.FamiliarBeast?.BestiaryNo, partingBlowAllowed,
            player.MaxHp > 0 ? (float)player.CurrentHp / player.MaxHp : 1f,
            (DateTime.UtcNow - _lastReleaseTryUtc).TotalSeconds, stingFinisher);
        if (release)
        {
            _lastReleaseTryUtc = DateTime.UtcNow;
            scheduler.PushOgcd(ArtemisAbilities.TemperedRelease, targetId, priority: 12,
                onDispatched: _ => context.Debug.PlannedAction = BSTActions.TemperedRelease.Name);
        }

        // Parting Blow sends the familiar away and takes an unspent Tempered Release with it, so it waits
        // while a release is still owed (the release is also what grants Lingering Vantage, +500 on the Blow).
        var releaseOwed = cfg.EnableTemperedRelease && releaseReady
            && (ShouldTemperedRelease(true, true, context.FamiliarBeast?.BestiaryNo, partingBlowAllowed,
                    player.MaxHp > 0 ? (float)player.CurrentHp / player.MaxHp : 1f, double.MaxValue, stingFinisher)
                || SavesFinalSting(Daedalus.Services.Beastmaster.CrucibleTracker.Current.OnBoard, context.FamiliarBeast?.BestiaryNo));
        _releaseOwed = releaseOwed;
        // Not straight after a Trick: the familiar is slow to act, and retreating it now loses the Trick (RSR 8f258dbc).
        // The falling retreat is exempt — it saves the familiar.
        var trickInFlight = !falling && svc.WasLastOgcd(BSTActions.Trick.ActionId);
        if (!releaseOwed && partingBlowAllowed && !trickInFlight && CanFire(svc, level, BSTActions.PartingBlow, targetId))
        {
            var line = _fallingLine;
            scheduler.PushOgcd(falling ? ArtemisAbilities.PartingBlowRetreat : ArtemisAbilities.PartingBlow, targetId,
                priority: falling ? 5 : 15,
                onDispatched: _ =>
                {
                    context.Debug.PlannedAction = falling
                        ? "Parting Blow (familiar falling — retreat it)"
                        : BSTActions.PartingBlow.Name;
                    if (falling)
                        Daedalus.Rotation.Base.RotationServices.DebugLog?.Log(
                            Daedalus.Services.Debug.DebugLogCategory.Action, Daedalus.Services.Debug.DebugLogSeverity.Info,
                            $"[Crucible] Parting Blow: familiar at {familiarHpPct:P0} (falling line {line:P0}), retreated");
                });
        }

        if (!trickHandledByPairing && cfg.EnableTrick && !svc.WasLastOgcd(BSTActions.PartingBlow.ActionId)
            && CanFire(svc, level, BSTActions.Trick, targetId))
        {
            scheduler.PushOgcd(ArtemisAbilities.Trick, targetId, priority: 30, onDispatched: _ =>
            {
                // The familiar's instinctual skill counts toward the chain — "if either the
                // beastmaster or familiar executes another instinctual skill within seven seconds".
                // Which of the four Hearts it grants depends on the beast and is not knowable from
                // here, so the chain advances without claiming an affinity.
                context.Instinct.RecordUnknown();
                context.Debug.PlannedAction = BSTActions.Trick.Name;
                context.Debug.InstinctChain = context.Instinct.Describe();
            });
        }

        context.Debug.DamageState = "Familiar out";
    }
}
