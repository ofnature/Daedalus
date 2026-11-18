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
    /// <summary>The clock. Replaced by the rotation simulator, which runs a fight faster than real time.</summary>
    internal Func<DateTime> UtcNow = () => DateTime.UtcNow;

    public int Priority => 30;
    public string Name => "Damage";

    public bool TryExecute(IArtemisContext context, bool isMoving) => false;

    public void UpdateDebugState(IArtemisContext context) { }

    public void CollectCandidates(IArtemisContext context, RotationScheduler scheduler, bool isMoving)
    {
        SwapOutWanted = false; // set again below when the familiar orders run this frame
        SpareForBusterWanted = false;
        if (!context.InCombat)
        {
            _combatSinceUtc = default;
            _bustersCast = 0;
            _busterCasting = false;
            _leftOnce.Clear();
            _leavingNo = 0;
            _heldForBuster = false;
            context.Debug.DamageState = "Not in combat";
            return;
        }

        // A tankbuster being cast at the beastmaster: the autopilot may answer it with an item.
        if (Daedalus.Services.Beastmaster.CrucibleTracker.Current.OnBoard)
        {
            var buster = FindCasting(context, CrucibleAutoPilotRules.TankbusterCastIds);
            if (buster != null && !_busterCasting)
                _bustersCast++; // a new tankbuster cast: the plan's tankbuster beast counts them
            _busterCasting = buster != null;
            var hers = buster != null && ArtemisCooldownRules.BusterIsTheBeastmasters(
                buster.CastTargetObjectId == context.Player.GameObjectId, CoverLeft(context.Player),
                context.FamiliarBeast != null,
                BeastOutIsKept(context, context.Configuration.Beastmaster.CrucibleGoal == CrucibleTeamGoal.Legendary, out _));
            Daedalus.Services.Beastmaster.CrucibleHints.BusterCastOnMe = hers ? buster!.CastActionId : 0;
            Daedalus.Services.Beastmaster.CrucibleHints.BusterCastUtc = UtcNow();
        }

        if (_combatSinceUtc == default)
            _combatSinceUtc = UtcNow();
        _weaveOpen = context.ActionService.CanExecuteOgcd;
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

        // Before the melee target: Parting Blow reaches 25 yalms, and the boss engine often has us out of melee just
        // when the adds bunch (Korha 2026-11-01 00:33: four Seedlings bunched on the familiar for 4 s, Korha 6-11 yalms
        // off, no Smash Axe target, so nothing past here ran and the Blow never went).
        _ymir = ReadYmir(context);
        _bombPush = ReadBombPush(context);
        _bombPushReadUtc = UtcNow();
        LogBombPush(context);
        _ogreWisps = ReadOgreWisps(context);
        PushPartingBlows(context, scheduler);
        PushBoneKnightGuard(context, scheduler);

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

        var holdInstincts = ArtemisCooldownRules.HoldsInstinctsAtYmirShell(_ymir.Active, _ymir.Shelled) || _bombPush.Push;
        ActionDefinition? answer = null;
        var paired = !holdInstincts && UsesPairing(context, level, out answer);
        if (_bombPush.Push)
        {
            // Only the 1-2-3 combo at the bomb: everything else waits for the worm (ArtemisCooldownRules.HoldsForBombPush).
            context.Debug.InstinctState = "bomb push: the chain is saved for the worm";
            return;
        }
        if (holdInstincts)
            context.Debug.InstinctState = "Ymir: instinct skills saved for the Ymir (no chain on the shell)";
        else if (finishing)
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
    /// The Crucible's Parting Blows that do not need a melee target, on every goal: the bunched adds of the Siren, the
    /// Morbol (Seedling poison outlives them) and the Ogre (wisps during Burning Ward), and sending the familiar away
    /// from a cast that kills it (<see cref="ArtemisCooldownRules.SendFamiliarAwayCastIds"/>).
    /// </summary>
    private void PushPartingBlows(IArtemisContext context, RotationScheduler scheduler)
    {
        if (!Daedalus.Services.Beastmaster.CrucibleTracker.Current.OnBoard)
            return;

        var familiarOut = context.HasFamiliar
                          || Daedalus.Services.Beastmaster.FamiliarLookup.Find(context.ObjectTable, context.Player) != null;

        // The bomb push: the familiar would follow the beastmaster onto the bomb and push it off line, so it goes.
        // Parting Blow is aimed at Loosefrox, never at a bomb.
        if (_bombPush.Push)
        {
            if (familiarOut && _bombPush.LoosefroxId != 0
                && CanFire(context.ActionService, context.Player.Level, BSTActions.PartingBlow, _bombPush.LoosefroxId)
                && TryGate(BSTActions.PartingBlow.ActionId))
            {
                scheduler.PushOgcd(ArtemisAbilities.PartingBlowRetreat, _bombPush.LoosefroxId, priority: 1, onDispatched: _ =>
                    context.Debug.PlannedAction = "Parting Blow (bomb push: the familiar would push the bomb off line)");
            }

            return;
        }

        if (FindCasting(context, ArtemisCooldownRules.SendFamiliarAwayCastIds) is { } caster)
        {
            if (familiarOut && CanFire(context.ActionService, context.Player.Level, BSTActions.PartingBlow, caster.GameObjectId)
                && TryGate(BSTActions.PartingBlow.ActionId))
            {
                var cast = caster.CastActionId;
                scheduler.PushOgcd(ArtemisAbilities.PartingBlowRetreat, caster.GameObjectId, priority: 1, onDispatched: _ =>
                {
                    context.Debug.PlannedAction = "Parting Blow (send the familiar away from the cast)";
                    Daedalus.Rotation.Base.RotationServices.DebugLog?.Log(
                        Daedalus.Services.Debug.DebugLogCategory.Action, Daedalus.Services.Debug.DebugLogSeverity.Info,
                        $"[Crucible] {caster.Name.TextValue} casts {cast}: Parting Blow sends the familiar away");
                });
            }

            return;
        }

        // The Siren's adds wear the familiars down fast (user, 2026-10-31: a roommate kept dying there).
        if (FindSiren(context, out _, out var sirenAdds) is { } siren)
            KillBunchedAdds(context, scheduler, siren, sirenAdds, familiarOut, "Siren");
        // The Morbol's Seedling Pieces poison whatever they hit, stacking, and it outlives them (user, 2026-10-31).
        if (familiarOut && FindMorbol(context, out var seedlings) is { } morbol)
            TryPartingBlowOnBunched(context, scheduler, seedlings, morbol, "Morbol: the seedlings are bunched");
        // The Great Wisp's blast covers the whole arena: kill it first with the familiar's Parting Blow (user, 2026-11-07).
        if (_ogreWisps.GreatWispId != 0 && TryPartingBlowGreatWisp(context, scheduler, familiarOut))
            return;
        // The Ogre's wisp waves march to the centre during Burning Ward and blow up there (14 deaths in 17 Ogre fights).
        // Not on Legendary: there the familiar stays out so its Cover takes their explosions (see RaidwideIn), and no
        // items are used (user, 2026-11-01).
        // Nor on a degree (user, 2026-11-15): the beasts stay out until the Ogre is active, each swapped out before it
        // falls, so the wisps' Vulnerability Up lands on them and not on the beastmaster.
        if (context.Configuration.Beastmaster.CrucibleGoal != CrucibleTeamGoal.Legendary
            && Daedalus.Services.Beastmaster.CrucibleHints.Degree == 0
            && FindAdds(context, ArtemisCooldownRules.OgrePieceNameId, WispNameIds, out var wisps) is { } ogre)
            KillBunchedAdds(context, scheduler, ogre, wisps, familiarOut, "Ogre");
        // The Treant's Slug Pieces, six at a time, crawl in on us (user, 2026-11-01: Fang them while levelling or
        // clearing). Not on Legendary, where the Fang would cost Economic Ethos.
        if (context.Configuration.Beastmaster.CrucibleGoal != CrucibleTeamGoal.Legendary
            && FindAdds(context, ArtemisCooldownRules.TreantPieceNameId, SlugNameIds, out var slugs) is { } treant)
            KillBunchedAdds(context, scheduler, treant, slugs, familiarOut, "Treant");
        // Pas de Seul's succubi: the board's free Fang takes them all at once (user, 2026-11-06). Not on Legendary.
        if (context.Configuration.Beastmaster.CrucibleGoal != CrucibleTeamGoal.Legendary
            && FindAdds(context, ArtemisCooldownRules.PasDeSeulNameId, SuccubusNameIds, out var succubi) is { } pas)
        {
            // On a degree the Behemoth's Meteor waits for them to close in before it and the Parting Blow go.
            var state = ReadSuccubi(context);
            KillBunchedAdds(context, scheduler, pas, succubi, familiarOut, "Pas de Seul",
                holdRelease: ArtemisCooldownRules.HoldMeteorForSuccubi(
                    Daedalus.Services.Beastmaster.CrucibleHints.Degree, context.FamiliarBeast?.BestiaryNo, state.Fight,
                    _succubiSeen, state.Up, state.Bunched, state.SecondsUp, familiarFalling: false));
        }
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
        // Legendary or a degree, the Bone Knight's Forward Guard: PushBoneKnightGuard holds him, so no other Snarl or Challenge.
        if ((legendary || Daedalus.Services.Beastmaster.CrucibleHints.Degree > 0) && FindGuardedBoneKnight(context) != null)
        {
            LogSnarlState("the Bone Knight's Forward Guard is up — the familiar holds him so you can hit his back, no Challenge",
                familiarHpPct, selfHpPct);
            return;
        }

        // A cast the familiar must not be out for (PushPartingBlows sends it away): no Snarl until it is done.
        if (FindCasting(context, ArtemisCooldownRules.SendFamiliarAwayCastIds) is { } caster)
        {
            LogSnarlState($"{caster.Name.TextValue} casts {caster.CastActionId} — the familiar is sent away, no Snarl",
                familiarHpPct, selfHpPct);
            return;
        }

        // Ymir Plan A on a degree: the familiar holds the Sahagin, the beastmaster takes the shell and the Ymir. Never a
        // Snarl at the Ymir (its Head Snatch cone), and no Challenge.
        if (_ymir.Active && _ymir.SahaginId != 0)
        {
            var sahagin = FindObject(context, _ymir.SahaginId);
            var sahaginOnFamiliar = familiar != null && sahagin is Dalamud.Game.ClientState.Objects.Types.IBattleNpc s
                                    && s.TargetObjectId == familiar.GameObjectId;
            var snarlReady = DutyActionBar.Contains(BSTActions.Snarl.ActionId) && svc.IsActionReady(BSTActions.Snarl.ActionId)
                             && svc.GetActionStatusCode(BSTActions.Snarl.ActionId, _ymir.SahaginId) == 0;
            if (ArtemisCooldownRules.SnarlsTheSahagin(cfg.EnableSnarl, familiarOut, sahaginOnFamiliar, snarlReady)
                && TryGate(BSTActions.Snarl.ActionId))
                scheduler.PushOgcd(ArtemisAbilities.Snarl, _ymir.SahaginId, priority: 3, onDispatched: _ =>
                    context.Debug.PlannedAction = "Snarl (Ymir: the familiar holds the Sahagin)");
            LogSnarlState("Ymir: the familiar holds the Sahagin, you take the shell and the Ymir", familiarHpPct, selfHpPct);
            return;
        }

        // Legendary: the beastmaster tanks on every board (every Japanese Legendary clear does): Challenge keeps every
        // enemy on you so the familiars end full (Selfless) and Unbeastable is kept by dodging; Snarl only for a wisp, a
        // Second Master's tankbuster, or a critical beastmaster. A degree with Briar Armor tanks the same way.
        var counterTank = !legendary && ArtemisCooldownRules.BeastmasterTanksForCounterArmor(
            Daedalus.Services.Beastmaster.CrucibleHints.Degree, Daedalus.Services.Beastmaster.CrucibleHints.CounterArmorHeld);
        var degree = Daedalus.Services.Beastmaster.CrucibleHints.Degree;
        // The beast out is one a later battle of this board plans to use (or there is no plan to say it is not).
        var beastOut = context.FamiliarBeast;
        var neededLater = BeastOutIsKept(context, legendary, out var keptForSuccubi);
        var degreeTank = !legendary && ArtemisCooldownRules.BeastmasterTanksOnDegree(degree, neededLater);
        // Kept whole for a later battle: no tankbuster, no debuff cast, no covering a critical beastmaster.
        var protectBeast = degree > 0 && !legendary && neededLater && beastOut != null;
        if (counterTank || legendary || degreeTank)
        {
            // A degree, this frame: a tankbuster coming (the beastmaster's, unless the beast out can be spent), or a
            // debuff cast the familiar should be the one to take.
            var degreeBuster = degree > 0 && ArtemisCooldownRules.BeastmasterTakesTheBuster(
                degree, Daedalus.Services.Beastmaster.CrucibleHints.SecondsUntilTankbuster);
            var familiarTakesBuster = degreeBuster && ArtemisCooldownRules.FamiliarTakesTheBusterOnDegree(
                cfg.EnableSnarl, familiarOut, familiarHpPct, neededLater, !context.NoReplacementHorn);
            var debuffCaster = degree > 0 && !protectBeast ? FindCasting(context, ArtemisCooldownRules.FamiliarTakesCastIds) : null;
            // Not for the succubi's beast: a swap would take the Behemoth and its Meteor off the field.
            SpareForBusterWanted = ArtemisCooldownRules.WantsSpareForBuster(
                degree, degreeBuster, protectBeast && !keptForSuccubi, selfHpPct);
            var keptFor = keptForSuccubi ? "kept for the succubi" : "needed in a later battle";

            LogSnarlState(selfHpPct <= ArtemisCooldownRules.SelfCriticalHpPct
                    ? (protectBeast ? $"you are critical, but {beastOut!.Name} is {keptFor} — no Cover" : null)
                : familiarTakesBuster ? $"a tankbuster is coming — {beastOut?.Name ?? "the familiar"} takes it (not needed again on this board)"
                : degreeBuster ? "a tankbuster is coming — you take it"
                : debuffCaster != null ? $"{debuffCaster.Name.TextValue} casts {debuffCaster.CastActionId} — the familiar takes it"
                : counterTank ? "the beastmaster tanks (Briar Armor hits back)"
                : legendary ? "the beastmaster tanks (Legendary)"
                : $"the beastmaster tanks ({beastOut?.Name ?? "the next beast"} is {keptFor})",
                familiarHpPct, selfHpPct);

            var wispIn = WispExplosionIn(context);
            if (ArtemisCooldownRules.ShouldSnarlForWisp(
                    cfg.EnableSnarl, Ready(BSTActions.Snarl), familiarOut, familiarHpPct, wispIn, CoverLeft(player))
                && TryGate(BSTActions.Snarl.ActionId))
            {
                scheduler.PushOgcd(ArtemisAbilities.Snarl, targetId, priority: 3, onDispatched: _ =>
                    context.Debug.PlannedAction = $"Snarl (a wisp blows up in {wispIn:0.0}s — Cover takes the Vulnerability Up)");
                return;
            }

            // The Second Master's Board: the familiar takes the tankbusters; no Challenge until the one coming has landed.
            var legendaryBusterIn = Daedalus.Services.Beastmaster.CrucibleHints.SecondsUntilTankbuster;
            if (legendary && ArtemisCooldownRules.LegendaryFamiliarTakesBuster(
                    Daedalus.Services.Beastmaster.CrucibleTracker.Current.Board?.Index ?? 0, legendaryBusterIn))
            {
                if (ArtemisCooldownRules.ShouldSnarlForLegendaryBuster(
                        cfg.EnableSnarl, Ready(BSTActions.Snarl), familiarOut, familiarHpPct, _fallingLine)
                    && TryGate(BSTActions.Snarl.ActionId))
                    scheduler.PushOgcd(ArtemisAbilities.Snarl, targetId, priority: 3, onDispatched: _ =>
                        context.Debug.PlannedAction = $"Snarl (tankbuster in {legendaryBusterIn:0.0}s — the familiar takes it)");
                return;
            }

            // A degree: the one tankbuster the familiar takes — the beast out is not planned for any later battle.
            if (familiarTakesBuster && selfHpPct > ArtemisCooldownRules.SelfCriticalHpPct)
            {
                if (!enemyOnFamiliar && Ready(BSTActions.Snarl) && TryGate(BSTActions.Snarl.ActionId))
                    scheduler.PushOgcd(ArtemisAbilities.Snarl, targetId, priority: 3, onDispatched: _ =>
                        context.Debug.PlannedAction = $"Snarl (a tankbuster — {beastOut?.Name ?? "the familiar"} takes it, not needed again on this board)");
                return; // and no Challenge to take the enemy back before it lands
            }

            // A degree: a cast that leaves a debuff and is no tankbuster goes to the familiar.
            if (debuffCaster != null && !degreeBuster && selfHpPct > ArtemisCooldownRules.SelfCriticalHpPct)
            {
                var casterId = debuffCaster.GameObjectId;
                var casterOnFamiliar = familiar != null && debuffCaster.TargetObjectId == familiar.GameObjectId;
                var snarlAtCaster = DutyActionBar.Contains(BSTActions.Snarl.ActionId) && svc.IsActionReady(BSTActions.Snarl.ActionId)
                                    && svc.GetActionStatusCode(BSTActions.Snarl.ActionId, casterId) == 0;
                if (ArtemisCooldownRules.ShouldSnarlForDebuffCast(
                        cfg.EnableSnarl, snarlAtCaster, familiarOut, familiarHpPct, _fallingLine, casterOnFamiliar)
                    && TryGate(BSTActions.Snarl.ActionId))
                {
                    var cast = debuffCaster.CastActionId;
                    scheduler.PushOgcd(ArtemisAbilities.Snarl, casterId, priority: 3, onDispatched: _ =>
                        context.Debug.PlannedAction = $"Snarl (cast {cast} leaves a debuff — the familiar takes it)");
                }

                if (casterOnFamiliar || familiarHpPct >= _fallingLine)
                    return; // no Challenge to take the caster back while the cast is going
            }

            if (!protectBeast
                && ArtemisCooldownRules.ShouldSnarlWhileTanking(
                    cfg.EnableSnarl, onBoardInBattle: true, Ready(BSTActions.Snarl), familiarOut, familiarHpPct, selfHpPct)
                && TryGate(BSTActions.Snarl.ActionId))
            {
                scheduler.PushOgcd(ArtemisAbilities.Snarl, targetId, priority: 3, onDispatched: _ =>
                    context.Debug.PlannedAction = "Snarl (you are critical — the familiar covers you)");
                return;
            }

            // A degree with two or more enemies and a beast that can be spent: it holds one, the beastmaster the rest.
            // Not at the Ogre, whose wisps have their own rule above.
            if (degree > 0 && !legendary && !protectBeast && !_ogreWisps.OgreFight)
            {
                var familiarHoldsOne = false;
                ulong other = 0;
                uint otherHp = 0;
                foreach (var obj in context.ObjectTable)
                {
                    if (obj is not Dalamud.Game.ClientState.Objects.Types.IBattleNpc { IsDead: false, IsTargetable: true, CurrentHp: > 0 } npc
                        || npc.GameObjectId == familiar?.GameObjectId)
                        continue;
                    if (familiar != null && npc.TargetObjectId == familiar.GameObjectId)
                        familiarHoldsOne = true;
                    else if (npc.TargetObjectId == player.GameObjectId && npc.GameObjectId != targetId && npc.CurrentHp > otherHp)
                        (other, otherHp) = (npc.GameObjectId, npc.CurrentHp);
                }

                var snarlAtOther = other != 0 && DutyActionBar.Contains(BSTActions.Snarl.ActionId)
                                   && svc.IsActionReady(BSTActions.Snarl.ActionId)
                                   && svc.GetActionStatusCode(BSTActions.Snarl.ActionId, other) == 0;
                if (ArtemisCooldownRules.ShouldSnarlToHoldOne(
                        cfg.EnableSnarl, snarlAtOther, familiarOut, familiarHpPct, _fallingLine, familiarHoldsOne, other != 0)
                    && TryGate(BSTActions.Snarl.ActionId))
                {
                    scheduler.PushOgcd(ArtemisAbilities.Snarl, other, priority: 5, onDispatched: _ =>
                        context.Debug.PlannedAction = "Snarl (two enemies on you — the familiar holds one)");
                    return;
                }
            }

            // Challenge keeps the target on the beastmaster — and, on a degree, ends a Cover that has nothing left to take.
            var coverSpent = degree > 0 && !legendary && ArtemisCooldownRules.CoverNoLongerWanted(
                CoverLeft(player), wispIn, debuffCaster != null, selfHpPct);
            if (ArtemisCooldownRules.ShouldChallengeToTank(
                    cfg.EnableChallenge, onBoardInBattle: true, Ready(BSTActions.Challenge),
                    target.TargetObjectId == player.GameObjectId && !coverSpent, selfHpPct)
                && TryGate(BSTActions.Challenge.ActionId))
            {
                scheduler.PushOgcd(ArtemisAbilities.Challenge, targetId, priority: 6, onDispatched: _ =>
                    context.Debug.PlannedAction = counterTank ? "Challenge (you tank — Briar Armor hits back)"
                        : legendary ? "Challenge (you tank — Legendary)"
                        : coverSpent ? "Challenge (you tank — and the familiar's Cover ends)"
                        : $"Challenge (you tank — {beastOut?.Name ?? "the next beast"} is needed in a later battle)");
            }

            return;
        }

        var busterIn = Daedalus.Services.Beastmaster.CrucibleHints.SecondsUntilTankbuster;
        var raidwideIn = RaidwideIn(context);

        LogSnarlState(lookupMissed
            ? "familiar out but not found in the object table — Snarl allowed"
            : ArtemisCooldownRules.SnarlHeldReason(cfg.EnableSnarl, familiarOut, familiarHpPct, selfHpPct, _fallingLine,
                busterIn, raidwideIn),
            familiarHpPct, selfHpPct);

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

        // The plan's tankbuster beast, low: you hold the enemy until the tankbuster it is kept for.
        if (ArtemisCooldownRules.ChallengesForBusterBeast(
                cfg.EnableChallenge, Ready(BSTActions.Challenge), _heldForBuster, enemyOnFamiliar, familiarHpPct, _fallingLine,
                selfHpPct, busterIn)
            && TryGate(BSTActions.Challenge.ActionId))
        {
            var held = context.FamiliarBeast?.Name ?? "the familiar";
            scheduler.PushOgcd(ArtemisAbilities.Challenge, targetId, priority: 6, onDispatched: _ =>
            {
                context.Debug.PlannedAction = $"Challenge ({held} is kept for the next tankbuster)";
                Daedalus.Rotation.Base.RotationServices.DebugLog?.Log(
                    Daedalus.Services.Debug.DebugLogCategory.Action, Daedalus.Services.Debug.DebugLogSeverity.Info,
                    $"[Crucible] Challenge: {held} is at {familiarHpPct:P0} and kept for the next tankbuster — you hold the enemy");
            });
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

    /// <summary>The Ogre fight's wisps this frame: whether it is on, whether their phase is over, and the Great Wisp.</summary>
    private readonly record struct OgreWispState(
        bool OgreFight, bool PhaseOver, ulong GreatWispId, uint GreatWispHp);

    private OgreWispState _ogreWisps;
    private bool _ogreWispsSeen;

    /// <summary>
    /// Reads the Ogre fight: wisps seen this fight, the phase over once they are all gone with the Ogre targetable again,
    /// and the Great Wisp a Parting Blow is likeliest to finish (the lowest HP).
    /// </summary>
    private OgreWispState ReadOgreWisps(IArtemisContext context)
    {
        Dalamud.Game.ClientState.Objects.Types.IBattleNpc? ogre = null;
        var anyWisp = false;
        ulong greatId = 0;
        var greatHp = uint.MaxValue;
        foreach (var obj in context.ObjectTable)
        {
            if (obj is not Dalamud.Game.ClientState.Objects.Types.IBattleNpc npc || npc.IsDead || npc.CurrentHp == 0)
                continue;
            if (npc.NameId == ArtemisCooldownRules.OgrePieceNameId)
                ogre = npc;
            else if (npc.IsTargetable && Array.IndexOf(WispNameIds, npc.NameId) >= 0)
            {
                anyWisp = true;
                if (npc.NameId != ArtemisCooldownRules.GreatWispPieceNameId)
                    continue;
                if (npc.CurrentHp < greatHp)
                {
                    greatId = npc.GameObjectId;
                    greatHp = npc.CurrentHp;
                }
            }
        }

        if (ogre == null)
        {
            _ogreWispsSeen = false;
            return default;
        }

        _ogreWispsSeen |= anyWisp;
        return new OgreWispState(true, _ogreWispsSeen && !anyWisp && ogre.IsTargetable, greatId, greatId == 0 ? 0 : greatHp);
    }

    private BombPushState _bombPush;
    private bool _bombPushLogged;

    private DateTime _bombPushReadUtc;

    /// <summary>
    /// The bomb push is on: Artemis does not call a familiar back until it ends. Only a fresh reading counts, so a module
    /// that stopped running (out of combat, a forced-movement pause) never leaves the familiar held.
    /// </summary>
    /// <summary>
    /// The card Artemis borrowed before the fight (Beastskin / Vileskin), kept on Beast Mode until it is used
    /// (<see cref="ArtemisCooldownRules.KeepsPreBorrowedCard"/>); 0 once the button shows something else.
    /// </summary>
    public uint PreBorrowedCardId { get; set; }

    public bool HoldsFamiliarForBombPush => _bombPush.Push && (UtcNow() - _bombPushReadUtc).TotalSeconds < 1;

    /// <summary>Second Board's final boss during the bomb push hold; default anywhere else.</summary>
    private readonly record struct BombPushState(bool Push, ulong LoosefroxId);

    private DateTime _barrageCastSeenUtc = DateTime.MinValue;

    /// <summary>
    /// The bomb push hold (<see cref="ArtemisCooldownRules.HoldsForBombPush"/>): from Loosefrox's small-bomb Barrage
    /// until the bombs are gone or Chewchum heads the engine's priority list. Loosefrox is found for the familiar's
    /// Parting Blow.
    /// </summary>
    private BombPushState ReadBombPush(IArtemisContext context)
    {
        var onBoard = Daedalus.Services.Beastmaster.CrucibleTracker.Current.OnBoard;
        if (!onBoard)
            return default;

        var targeting = context.TargetingService;
        var first = targeting.FirstFightPriority;
        ulong loosefrox = 0;
        var bombsUp = false;
        var wormFirst = false;
        foreach (var o in context.ObjectTable)
        {
            if (o is not Dalamud.Game.ClientState.Objects.Types.IBattleNpc npc || npc.IsDead)
                continue;
            switch (npc.BaseId)
            {
                case ArtemisCooldownRules.LoosefroxBaseId when npc.IsTargetable:
                    loosefrox = npc.GameObjectId;
                    if (npc.IsCasting && npc.CastActionId == ArtemisCooldownRules.SmallBombBarrageCastId)
                        _barrageCastSeenUtc = UtcNow();
                    break;
                case ArtemisCooldownRules.SmallBombBaseId when npc.IsTargetable:
                    bombsUp = true;
                    break;
                case ArtemisCooldownRules.ChewchumBaseId:
                    wormFirst |= npc.GameObjectId == first;
                    break;
            }
        }

        if (loosefrox == 0)
            return default;

        var sinceBarrage = _barrageCastSeenUtc == DateTime.MinValue
            ? double.MaxValue
            : (UtcNow() - _barrageCastSeenUtc).TotalSeconds;
        return ArtemisCooldownRules.HoldsForBombPush(onBoard, targeting.ActiveFightModule, sinceBarrage, bombsUp, wormFirst)
            ? new BombPushState(true, loosefrox)
            : default;
    }

    /// <summary>One debug-log line when the bomb push starts and one when it ends.</summary>
    private void LogBombPush(IArtemisContext context)
    {
        if (_bombPush.Push == _bombPushLogged)
            return;
        _bombPushLogged = _bombPush.Push;
        Daedalus.Rotation.Base.RotationServices.DebugLog?.Log(
            Daedalus.Services.Debug.DebugLogCategory.Action, Daedalus.Services.Debug.DebugLogSeverity.Info,
            _bombPush.Push
                ? "[Crucible] bomb push: familiar sent away, only the 1-2-3 combo at the bomb, chain saved for the worm"
                : "[Crucible] bomb push over: the familiar comes back, normal rotation");
    }

    private bool _eggHoldLogged;
    private DateTime _combatSinceUtc;
    private DateTime _mitelingsUpSinceUtc;
    private bool _meteorHoldLogged;

    // Pas de Seul's succubi: seen this fight, since when they have been up, and the hold logged once.
    private bool _succubiSeen;
    private DateTime _succubiUpSinceUtc;
    private bool _succubiHoldLogged;

    /// <summary>
    /// The Pas de Seul fight this frame: whether it is on, the succubi up, and whether they have closed in on her.
    /// Keeps <see cref="_succubiSeen"/> and <see cref="_succubiUpSinceUtc"/>; both clear when she is not in the fight.
    /// </summary>
    private (bool Fight, int Up, bool Bunched, double SecondsUp) ReadSuccubi(IArtemisContext context)
    {
        List<(ulong Id, System.Numerics.Vector2 At)> succubi = [];
        var pas = Daedalus.Services.Beastmaster.CrucibleTracker.Current.OnBoard
            ? FindAdds(context, ArtemisCooldownRules.PasDeSeulNameId, SuccubusNameIds, out succubi)
            : null;
        if (pas == null)
        {
            _succubiSeen = false;
            _succubiUpSinceUtc = default;
            _succubiHoldLogged = false;
            return (false, 0, false, 0);
        }

        var now = UtcNow();
        if (succubi.Count == 0)
            _succubiUpSinceUtc = default;
        else if (_succubiUpSinceUtc == default)
            _succubiUpSinceUtc = now;
        _succubiSeen |= succubi.Count > 0;
        var at = new System.Numerics.Vector2(pas.Position.X, pas.Position.Z);
        var bunched = succubi.Count > 0
                      && succubi.TrueForAll(s => System.Numerics.Vector2.Distance(s.At, at) <= ArtemisCooldownRules.MitelingsBunchedYalms);
        return (true, succubi.Count, bunched, _succubiUpSinceUtc == default ? 0 : (now - _succubiUpSinceUtc).TotalSeconds);
    }

    /// <summary>
    /// Whether the beast out is one to keep whole: a later battle of this board plans to use it, there is no plan to
    /// say it does not, or it is Pas de Seul's before her succubi have come and died (<paramref name="keptForSuccubi"/>).
    /// True with no beast out.
    /// </summary>
    private bool BeastOutIsKept(IArtemisContext context, bool legendary, out bool keptForSuccubi)
    {
        var succubi = ReadSuccubi(context);
        // The Behemoth's Meteor is what the beast is kept for: a plan with no Behemoth here (the video's) spends its beasts.
        keptForSuccubi = !legendary && ArtemisCooldownRules.KeepsBeastForSuccubi(
            Daedalus.Services.Beastmaster.CrucibleHints.Degree, succubi.Fight, _succubiSeen, succubi.Up)
            && Daedalus.Services.Beastmaster.CrucibleTracker.Current.Enemy is { } pas
            && CrucibleDegreeTeams.For(Daedalus.Services.Beastmaster.CrucibleHints.Degree, pas.Battle) is { } pasTeam
            && System.Linq.Enumerable.Contains(pasTeam, ArtemisCooldownRules.BehemothBestiaryNo);
        var beastOut = context.FamiliarBeast;
        return beastOut == null
               || keptForSuccubi
               || !Daedalus.Services.Beastmaster.CrucibleHints.BeastsNeededLaterKnown
               || System.Linq.Enumerable.Contains(Daedalus.Services.Beastmaster.CrucibleHints.BeastsNeededLater, beastOut.BestiaryNo);
    }

    /// <summary>Whether the Behemoth's Meteor waits for Pas de Seul's succubi this frame (<see cref="ArtemisCooldownRules.HoldMeteorForSuccubi"/>); logs once a hold.</summary>
    private bool HoldMeteorForSuccubi(IArtemisContext context, bool falling)
    {
        var (fight, up, bunched, secondsUp) = ReadSuccubi(context);
        var hold = context.Configuration.Beastmaster.CrucibleGoal != CrucibleTeamGoal.Legendary
                   && ArtemisCooldownRules.HoldMeteorForSuccubi(
                       Daedalus.Services.Beastmaster.CrucibleHints.Degree, context.FamiliarBeast?.BestiaryNo, fight,
                       _succubiSeen, up, bunched, secondsUp, falling);
        if (hold)
            context.Debug.InstinctState = up > 0
                ? "Behemoth: Meteor waits for the succubi to close in"
                : "Behemoth: Meteor held for the succubi";
        if (hold && !_succubiHoldLogged)
            Daedalus.Rotation.Base.RotationServices.DebugLog?.Log(
                Daedalus.Services.Debug.DebugLogCategory.Action, Daedalus.Services.Debug.DebugLogSeverity.Info,
                $"[Crucible] Behemoth: Meteor held for the succubi ({up} up, {(UtcNow() - _combatSinceUtc).TotalSeconds:0}s in)");
        _succubiHoldLogged = hold;
        return hold;
    }
    private YmirState _ymir;

    /// <summary>The Ymir fight on a degree (Plan A); default (inactive) anywhere else.</summary>
    private readonly record struct YmirState(ulong YmirId, ulong ShellId, ulong SahaginId, bool Shelled)
    {
        public bool Active => YmirId != 0;
    }

    /// <summary>
    /// The Ymir, its shell and the Sahagin, on a degree only (Standard and Legendary keep their own play). Shelled is
    /// the Ymir's Vulnerability Down, which it carries in every shell it crawls into.
    /// </summary>
    private static YmirState ReadYmir(IArtemisContext context)
    {
        if (Daedalus.Services.Beastmaster.CrucibleHints.Degree <= 0 || !Daedalus.Services.Beastmaster.CrucibleTracker.Current.OnBoard)
            return default;

        ulong ymir = 0, shell = 0, sahagin = 0;
        var shelled = false;
        foreach (var obj in context.ObjectTable)
        {
            if (obj is not Dalamud.Game.ClientState.Objects.Types.IBattleNpc npc || npc.IsDead || npc.CurrentHp == 0)
                continue;
            if (npc.NameId == ArtemisCooldownRules.YmirPieceNameId && (ymir == 0 || npc.IsTargetable))
            {
                ymir = npc.GameObjectId;
                foreach (var status in npc.StatusList)
                    shelled |= status.StatusId == ArtemisCooldownRules.YmirShelledStatusId;
            }
            else if (npc.NameId == ArtemisCooldownRules.YmirShellNameId && npc.IsTargetable)
                shell = npc.GameObjectId;
            else if (npc.NameId == ArtemisCooldownRules.SahaginPieceNameId && npc.IsTargetable)
                sahagin = npc.GameObjectId;
        }

        return ymir == 0 ? default : new YmirState(ymir, shell, sahagin, shelled);
    }

    /// <summary>
    /// The Banemite fight: whether the Behemoth's Meteor waits for the Mitelings
    /// (<see cref="ArtemisCooldownRules.HoldMeteorForMitelings"/>). Logs once each time a hold starts.
    /// </summary>
    private bool HoldMeteorForMitelings(IArtemisContext context, ulong releaseTarget, bool falling)
    {
        var onBoard = Daedalus.Services.Beastmaster.CrucibleTracker.Current.OnBoard;
        List<(ulong Id, System.Numerics.Vector2 At)> mitelings = [];
        var banemite = onBoard && ArtemisCooldownRules.ReleaseIsForAdds(context.FamiliarBeast?.BestiaryNo)
            ? FindAdds(context, ArtemisCooldownRules.BanemitePieceNameId, MitelingNameIds, out mitelings)
            : null;
        if (banemite == null || banemite.MaxHp == 0)
        {
            _mitelingsUpSinceUtc = default;
            _meteorHoldLogged = false;
            return false;
        }

        var now = UtcNow();
        if (mitelings.Count == 0)
            _mitelingsUpSinceUtc = default;
        else if (_mitelingsUpSinceUtc == default)
            _mitelingsUpSinceUtc = now;
        var at = FindObject(context, releaseTarget) is { } target
            ? new System.Numerics.Vector2(target.Position.X, target.Position.Z)
            : new System.Numerics.Vector2(banemite.Position.X, banemite.Position.Z);
        var bunched = mitelings.Count > 0
                      && mitelings.TrueForAll(m => System.Numerics.Vector2.Distance(m.At, at) <= ArtemisCooldownRules.MitelingsBunchedYalms);
        var secondsIn = (now - _combatSinceUtc).TotalSeconds;
        var banemiteHpPct = (float)banemite.CurrentHp / banemite.MaxHp;
        var hold = ArtemisCooldownRules.HoldMeteorForMitelings(
            onBoard, context.FamiliarBeast?.BestiaryNo, banemiteHpPct, mitelings.Count, bunched,
            _mitelingsUpSinceUtc == default ? 0 : (now - _mitelingsUpSinceUtc).TotalSeconds, secondsIn, falling);
        if (hold)
            context.Debug.InstinctState = mitelings.Count > 0
                ? "Behemoth: Meteor waits for the Mitelings to close in"
                : $"Behemoth: Meteor held for the Mitelings (next wave in {ArtemisCooldownRules.SecondsToNextMitelingWave(secondsIn):0}s)";
        if (hold && !_meteorHoldLogged)
            Daedalus.Rotation.Base.RotationServices.DebugLog?.Log(
                Daedalus.Services.Debug.DebugLogCategory.Action, Daedalus.Services.Debug.DebugLogSeverity.Info,
                $"[Crucible] Behemoth: Meteor held for the Mitelings ({mitelings.Count} up, {secondsIn:0}s in, "
                + $"Banemite {banemiteHpPct:P0})");
        _meteorHoldLogged = hold;
        return hold;
    }

    /// <summary>One debug-log line each time Shield Charge starts being held for an egg.</summary>
    private void LogEggHold(bool held)
    {
        if (held == _eggHoldLogged)
            return;
        _eggHoldLogged = held;
        if (held)
            Daedalus.Rotation.Base.RotationServices.DebugLog?.Log(
                Daedalus.Services.Debug.DebugLogCategory.Action, Daedalus.Services.Debug.DebugLogSeverity.Info,
                "[Crucible] Shield Charge held: a Zu egg is in its splash");
    }

    private static Dalamud.Game.ClientState.Objects.Types.IGameObject? FindObject(IArtemisContext context, ulong id)
    {
        foreach (var obj in context.ObjectTable)
        {
            if (obj.GameObjectId == id)
                return obj;
        }

        return null;
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

    /// <summary>
    /// Parting Blow (8 yalms round its target) on <paramref name="adds"/> once they have bunched: aimed at an add or
    /// <paramref name="boss"/> that has every add within <see cref="CrucibleAutoPilotRules.PartingBlowReachYalms"/>, and
    /// only with two or more up. Nothing until then — they bunch on the familiar and the beastmaster as they close in.
    /// </summary>
    private void TryPartingBlowOnBunched(
        IArtemisContext context, RotationScheduler scheduler, List<(ulong Id, System.Numerics.Vector2 At)> adds,
        Dalamud.Game.ClientState.Objects.Types.IBattleNpc boss, string why)
    {
        var svc = context.ActionService;
        var level = context.Player.Level;
        var candidates = new List<(ulong Id, System.Numerics.Vector2 At)>(adds);
        if (boss.IsTargetable)
            candidates.Add((boss.GameObjectId, new System.Numerics.Vector2(boss.Position.X, boss.Position.Z)));
        var blowAim = CrucibleAutoPilotRules.FangAim(
            adds, candidates, context.TargetingService.GetGameHardTargetId(), CrucibleAutoPilotRules.PartingBlowReachYalms);
        if (blowAim == 0 || !CanFire(svc, level, BSTActions.PartingBlow, blowAim) || !TryGate(BSTActions.PartingBlow.ActionId))
            return;

        var count = adds.Count;
        var who = why.Split(':')[0];
        scheduler.PushOgcd(ArtemisAbilities.CruciblePartingBlow, blowAim, priority: 2, onDispatched: _ =>
        {
            context.Debug.PlannedAction = $"Parting Blow ({why})";
            Daedalus.Rotation.Base.RotationServices.DebugLog?.Log(
                Daedalus.Services.Debug.DebugLogCategory.Action, Daedalus.Services.Debug.DebugLogSeverity.Info,
                $"[Crucible] {who}: Parting Blow on {count} bunched adds");
        });
    }

    /// <summary>
    /// Parting Blow at the Great Wisp once it would finish it, before it reaches the Ogre
    /// (<see cref="ArtemisCooldownRules.ShouldPartingBlowGreatWisp"/>). Not on Legendary, where the familiar stays out
    /// so its Cover takes the blast. True when the Blow was queued.
    /// </summary>
    private bool TryPartingBlowGreatWisp(IArtemisContext context, RotationScheduler scheduler, bool familiarOut)
    {
        if (context.Configuration.Beastmaster.CrucibleGoal == CrucibleTeamGoal.Legendary)
            return false;

        var svc = context.ActionService;
        var wisp = _ogreWisps;
        var hasVantage = false;
        foreach (var status in context.Player.StatusList)
            hasVantage |= status.StatusId == ArtemisCooldownRules.LingeringVantageStatusId;
        if (!ArtemisCooldownRules.ShouldPartingBlowGreatWisp(
                Daedalus.Services.Beastmaster.CrucibleTracker.Current.OnBoard, familiarOut,
                CanFire(svc, context.Player.Level, BSTActions.PartingBlow, wisp.GreatWispId), wisp.GreatWispHp, hasVantage)
            || !TryGate(BSTActions.PartingBlow.ActionId))
            return false;

        scheduler.PushOgcd(ArtemisAbilities.CruciblePartingBlow, wisp.GreatWispId, priority: 1, onDispatched: _ =>
        {
            context.Debug.PlannedAction = "Parting Blow (the Great Wisp, before it reaches the Ogre)";
            Daedalus.Rotation.Base.RotationServices.DebugLog?.Log(
                Daedalus.Services.Debug.DebugLogCategory.Action, Daedalus.Services.Debug.DebugLogSeverity.Info,
                $"[Crucible] Ogre: Parting Blow on the Great Wisp ({wisp.GreatWispHp} HP)");
        });
        return true;
    }

    /// <summary>The Morbol Piece and where its Seedling Pieces stand; null when it is not in the fight.</summary>
    private static Dalamud.Game.ClientState.Objects.Types.IBattleNpc? FindMorbol(
        IArtemisContext context, out List<(ulong Id, System.Numerics.Vector2 At)> seedlings)
    {
        Dalamud.Game.ClientState.Objects.Types.IBattleNpc? morbol = null;
        seedlings = new List<(ulong Id, System.Numerics.Vector2 At)>();
        foreach (var obj in context.ObjectTable)
        {
            if (obj is not Dalamud.Game.ClientState.Objects.Types.IBattleNpc npc || npc.IsDead || npc.CurrentHp == 0)
                continue;
            if (npc.NameId == ArtemisCooldownRules.MorbolPieceNameId && npc.IsTargetable)
                morbol = npc;
            else if (npc.NameId == ArtemisCooldownRules.SeedlingPieceNameId && npc.IsTargetable)
                seedlings.Add((npc.GameObjectId, new System.Numerics.Vector2(npc.Position.X, npc.Position.Z)));
        }

        return morbol;
    }

    private static readonly uint[] WispNameIds = [ArtemisCooldownRules.WispPieceNameId, ArtemisCooldownRules.GreatWispPieceNameId];
    private static readonly uint[] SlugNameIds = [ArtemisCooldownRules.SlugPieceNameId];
    private static readonly uint[] SuccubusNameIds = [ArtemisCooldownRules.SuccubusMageNameId, ArtemisCooldownRules.SuccubusKnightNameId];
    private static readonly uint[] MitelingNameIds = [ArtemisCooldownRules.MitelingPieceNameId];

    private readonly Dictionary<ulong, DateTime> _wispSeenUtc = [];

    /// <summary>The Bone Knight, alive, or null.</summary>
    private static Dalamud.Game.ClientState.Objects.Types.IBattleNpc? FindBoneKnight(IArtemisContext context)
    {
        foreach (var obj in context.ObjectTable)
        {
            if (obj is Dalamud.Game.ClientState.Objects.Types.IBattleNpc { IsDead: false } npc
                && npc.NameId == ArtemisCooldownRules.BoneKnightNameId)
                return npc;
        }

        return null;
    }

    /// <summary>His Forward Guard is up or being cast.</summary>
    private static bool IsGuarded(Dalamud.Game.ClientState.Objects.Types.IBattleNpc knight)
    {
        if (knight.IsCasting && knight.CastActionId == ArtemisCooldownRules.ForwardGuardCastId)
            return true;
        foreach (var status in knight.StatusList)
        {
            if (status.StatusId == ArtemisCooldownRules.ForwardGuardStatusId)
                return true;
        }

        return false;
    }

    /// <summary>The Bone Knight while his Forward Guard is up or being cast, else null.</summary>
    private static Dalamud.Game.ClientState.Objects.Types.IBattleNpc? FindGuardedBoneKnight(IArtemisContext context)
        => FindBoneKnight(context) is { } knight && IsGuarded(knight) ? knight : null;

    /// <summary>His Forward Guard has come up this fight: the Wespe may come out (CyclesToWespeInKnightFight).</summary>
    private bool _knightGuardSeen;

    /// <summary>The Bone Knight is in this fight (Legendary or a degree).</summary>
    private bool _knightFight;

    /// <summary>
    /// The Bone Knight's Forward Guard blocks everything from the front and faces whoever holds him, but not the
    /// familiar's damage: Snarl at him so he turns to the familiar, and the beastmaster (walked behind him by the boss
    /// engine) hits his back (user, 2026-11-01). Legendary or a degree above Standard; goes on the cast. Runs before the
    /// melee target is looked for.
    /// </summary>
    private void PushBoneKnightGuard(IArtemisContext context, RotationScheduler scheduler)
    {
        var degree = Daedalus.Services.Beastmaster.CrucibleHints.Degree;
        var knight = Daedalus.Services.Beastmaster.CrucibleTracker.Current.OnBoard
                     && (context.Configuration.Beastmaster.CrucibleGoal == CrucibleTeamGoal.Legendary || degree > 0)
            ? FindBoneKnight(context)
            : null;
        _knightFight = knight != null;
        if (knight == null)
        {
            _knightGuardSeen = false;
            return;
        }

        var guarded = IsGuarded(knight);
        _knightGuardSeen |= guarded;
        var svc = context.ActionService;
        var cfg = context.Configuration.Beastmaster;
        var familiar = Daedalus.Services.Beastmaster.FamiliarLookup.Find(context.ObjectTable, context.Player);
        var familiarOut = familiar != null || context.HasFamiliar;
        var familiarHpPct = familiar is { MaxHp: > 0 } f ? (float)f.CurrentHp / f.MaxHp : familiarOut ? 1f : 0f;
        var onFamiliar = familiar != null && knight.TargetObjectId == familiar.GameObjectId;
        var snarlReady = DutyActionBar.Contains(BSTActions.Snarl.ActionId)
                         && svc.IsActionReady(BSTActions.Snarl.ActionId)
                         && svc.GetActionStatusCode(BSTActions.Snarl.ActionId, knight.GameObjectId) == 0;

        if (!guarded
            || !ArtemisCooldownRules.ShouldSnarlBoneKnight(cfg.EnableSnarl, snarlReady, familiarOut, familiarHpPct, onFamiliar)
            || !TryGate(BSTActions.Snarl.ActionId))
            return;

        scheduler.PushOgcd(ArtemisAbilities.Snarl, knight.GameObjectId, priority: 2, onDispatched: _ =>
        {
            context.Debug.PlannedAction = "Snarl (the Bone Knight's Forward Guard — he faces the familiar, you hit his back)";
            Daedalus.Rotation.Base.RotationServices.DebugLog?.Log(
                Daedalus.Services.Debug.DebugLogCategory.Action, Daedalus.Services.Debug.DebugLogSeverity.Info,
                "[Crucible] Bone Knight's Forward Guard: Snarl so he faces the familiar");
        });
    }

    /// <summary>
    /// Seconds until the next Ogre wisp reaches the ward and blows up (<see cref="ArtemisCooldownRules.WispWalkSeconds"/>
    /// after it appears), or <see cref="float.MaxValue"/> with none up. Its Scorched Earth gives whoever it hits a
    /// Vulnerability Up — the familiar instead of the beastmaster when its Cover is up.
    /// </summary>
    private float WispExplosionIn(IArtemisContext context)
    {
        var now = UtcNow();
        if (FindAdds(context, ArtemisCooldownRules.OgrePieceNameId, WispNameIds, out var wisps) == null || wisps.Count == 0)
        {
            _wispSeenUtc.Clear();
            return float.MaxValue;
        }

        var ages = new List<double>(wisps.Count);
        foreach (var (id, _) in wisps)
        {
            if (!_wispSeenUtc.TryGetValue(id, out var seen))
                _wispSeenUtc[id] = seen = now;
            ages.Add((now - seen).TotalSeconds);
        }

        return ArtemisCooldownRules.WispArrivalSeconds(ages);
    }

    /// <summary>
    /// The raidwide timer the Snarl rules use: the boss engine's, or on Legendary the next Ogre wisp explosion if sooner
    /// (a Vulnerability Up on the beastmaster costs Unbeastable).
    /// </summary>
    private float RaidwideIn(IArtemisContext context)
        => context.Configuration.Beastmaster.CrucibleGoal == CrucibleTeamGoal.Legendary
            ? Math.Min(Daedalus.Services.Beastmaster.CrucibleHints.SecondsUntilRaidwide, WispExplosionIn(context))
            : Daedalus.Services.Beastmaster.CrucibleHints.SecondsUntilRaidwide;

    private static bool HasStatus(Dalamud.Game.ClientState.Objects.Types.IBattleChara player, uint statusId)
    {
        foreach (var status in player.StatusList)
        {
            if (status.StatusId == statusId)
                return true;
        }

        return false;
    }

    /// <summary>Seconds of Cover left on the beastmaster (0 when not covered).</summary>
    private static float CoverLeft(Dalamud.Game.ClientState.Objects.Types.IBattleChara player)
    {
        foreach (var status in player.StatusList)
        {
            if (status.StatusId == ArtemisCooldownRules.CoveredStatusId)
                return status.RemainingTime;
        }

        return 0f;
    }

    /// <summary>The live enemy <paramref name="bossNameId"/> (targetable or not) and where its adds stand; null when it is not in the fight.</summary>
    private static Dalamud.Game.ClientState.Objects.Types.IBattleNpc? FindAdds(
        IArtemisContext context, uint bossNameId, uint[] addNameIds, out List<(ulong Id, System.Numerics.Vector2 At)> adds)
    {
        Dalamud.Game.ClientState.Objects.Types.IBattleNpc? boss = null;
        adds = new List<(ulong Id, System.Numerics.Vector2 At)>();
        foreach (var obj in context.ObjectTable)
        {
            if (obj is not Dalamud.Game.ClientState.Objects.Types.IBattleNpc npc || npc.IsDead || npc.CurrentHp == 0)
                continue;
            if (npc.NameId == bossNameId && (boss == null || npc.MaxHp > boss.MaxHp))
                boss = npc;
            else if (Array.IndexOf(addNameIds, npc.NameId) >= 0 && npc.IsTargetable)
                adds.Add((npc.GameObjectId, new System.Numerics.Vector2(npc.Position.X, npc.Position.Z)));
        }

        return boss;
    }

    /// <summary>
    /// True in the Ogre fight; fills <paramref name="smallWisps"/> with the targetable small wisps (not the Great Wisp)
    /// and their edge distance from the beastmaster.
    /// </summary>
    private static bool FindSmallWisps(IArtemisContext context, List<(ulong, float)> smallWisps)
    {
        var ogre = false;
        var player = context.Player;
        foreach (var obj in context.ObjectTable)
        {
            if (obj is not Dalamud.Game.ClientState.Objects.Types.IBattleNpc npc || npc.IsDead || npc.CurrentHp == 0)
                continue;
            if (npc.NameId == ArtemisCooldownRules.OgrePieceNameId)
                ogre = true;
            else if (npc.NameId == ArtemisCooldownRules.WispPieceNameId && npc.IsTargetable)
                smallWisps.Add((npc.GameObjectId,
                    System.Numerics.Vector3.Distance(player.Position, npc.Position) - npc.HitboxRadius - player.HitboxRadius));
        }

        if (!ogre)
            smallWisps.Clear();
        return ogre;
    }

    /// <summary>
    /// The enemies a Kinship has a job for: casting something to interrupt (flagged by the boss engine, or on a board
    /// marked interruptible by the game), and — on a board — carrying a buff a dispel can strip.
    /// </summary>
    private static void ReadKinshipJobs(
        IArtemisContext context, bool onBoard, List<(ulong, float, bool)> interrupts, List<(ulong, float)> buffed)
    {
        var flagged = Daedalus.Services.Beastmaster.CrucibleHints.TargetsToInterrupt;
        if (!onBoard && flagged.Count == 0)
            return;

        var player = context.Player;
        foreach (var obj in context.ObjectTable)
        {
            if (obj is not Dalamud.Game.ClientState.Objects.Types.IBattleNpc npc || npc.IsDead || npc.CurrentHp == 0
                || !npc.IsTargetable || npc.SubKind != Daedalus.Compat.BattleNpcKinds.Combatant)
                continue;

            var edge = System.Numerics.Vector3.Distance(player.Position, npc.Position) - npc.HitboxRadius - player.HitboxRadius;
            var isFlagged = false;
            foreach (var id in flagged)
                isFlagged |= id == npc.GameObjectId;
            if (ArtemisCooldownRules.IsInterruptCandidate(
                    isFlagged, onBoard, npc.IsCasting, npc.IsCastInterruptible, npc.TotalCastTime - npc.CurrentCastTime))
                interrupts.Add((npc.GameObjectId, edge, true));

            if (!onBoard)
                continue;
            foreach (var status in npc.StatusList)
            {
                if (status.StatusId != 0 && context.DebuffDetectionService.IsDispellableBuff(status.StatusId))
                {
                    buffed.Add((npc.GameObjectId, edge));
                    break;
                }
            }
        }
    }

    /// <summary>An enemy casting one of <paramref name="actionIds"/>, or null.</summary>
    private static Dalamud.Game.ClientState.Objects.Types.IBattleNpc? FindCasting(IArtemisContext context, IReadOnlySet<uint> actionIds)
    {
        foreach (var obj in context.ObjectTable)
        {
            if (obj is Dalamud.Game.ClientState.Objects.Types.IBattleNpc { IsDead: false, IsCasting: true } npc
                && actionIds.Contains(npc.CastActionId))
                return npc;
        }

        return null;
    }

    /// <summary>Whether a Parting Blow at this target would break a Zu egg (only while the Zu is in the fight).</summary>
    private bool PartingBlowBreaksZuEgg(IArtemisContext context, ulong targetId)
    {
        if (ReadZu(context, out var eggs) == null || eggs.Count == 0 || FindObject(context, targetId) is not { } target)
            return false;
        var held = ArtemisCooldownRules.PartingBlowBreaksEgg(eggs, new System.Numerics.Vector2(target.Position.X, target.Position.Z));
        if (held)
            LogEggHold(true);
        return held;
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
    /// <para>The Ogre's wisps (not on Legendary) and the Treant's slugs get the same (<paramref name="who"/> names the
    /// fight in the logs). An untargetable <paramref name="siren"/> (the Ogre during Burning Ward) is never the aim.</para>
    /// </summary>
    private void KillBunchedAdds(
        IArtemisContext context, RotationScheduler scheduler, Dalamud.Game.ClientState.Objects.Types.IBattleNpc siren,
        List<(ulong Id, System.Numerics.Vector2 At)> adds, bool familiarOut, string who, bool holdRelease = false)
    {
        var hard = context.TargetingService.GetGameHardTargetId();
        var candidates = new List<(ulong Id, System.Numerics.Vector2 At)>(adds);
        if (siren.IsTargetable)
            candidates.Add((siren.GameObjectId, new System.Numerics.Vector2(siren.Position.X, siren.Position.Z)));
        Daedalus.Services.Beastmaster.CrucibleHints.FangAimUtc = UtcNow();
        Daedalus.Services.Beastmaster.CrucibleHints.FangTargetId = 0;

        var level = context.Player.Level;
        var svc = context.ActionService;
        var blowReady = familiarOut && ActionAvailability.MeetsLevelAndLearned(level, svc, BSTActions.PartingBlow)
                        && svc.IsActionReady(BSTActions.PartingBlow.ActionId);
        if (blowReady)
        {
            // The release is being held for the adds to close in: no release yet, and no Blow, which would take it away.
            if (holdRelease)
                return;

            // Release first: it grants Lingering Vantage, which lifts the Blow from 1,000 to 1,500 (user, 2026-10-31).
            // Gone off the instant the adds appear, so the Vantage is up by the time they bunch. Not Wespe's Final
            // Sting, which would send it away before the Blow (ShouldTemperedRelease refuses it with no Parting Blow).
            var hasVantage = false;
            foreach (var status in context.Player.StatusList)
                hasVantage |= status.StatusId == ArtemisCooldownRules.LingeringVantageStatusId;
            var selfHpPct = context.Player.MaxHp > 0 ? (float)context.Player.CurrentHp / context.Player.MaxHp : 1f;
            if (!hasVantage && adds.Count > 0 && siren.IsTargetable
                && ShouldTemperedRelease(
                    context.Configuration.Beastmaster.EnableTemperedRelease,
                    CanFire(svc, level, BSTActions.TemperedRelease, siren.GameObjectId), context.FamiliarBeast?.BestiaryNo,
                    partingBlowEnabled: false, selfHpPct, double.MaxValue))
            {
                if (TryGate(BSTActions.TemperedRelease.ActionId))
                {
                    scheduler.PushOgcd(ArtemisAbilities.TemperedRelease, siren.GameObjectId, priority: 2, onDispatched: _ =>
                        context.Debug.PlannedAction = $"Tempered Release ({who}: Lingering Vantage for the Parting Blow)");
                }

                return;
            }

            TryPartingBlowOnBunched(context, scheduler, adds, siren, $"{who}: the adds are bunched");
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
            _axeStarvedSinceUtc = UtcNow();

        // The gauge says how much TP there is and how many diamonds; without it, fall back to "the axes
        // are refused" and our own tally.
        if (context.HasFamiliar)
            _familiarGoneSinceUtc = DateTime.MinValue;
        else if (_familiarGoneSinceUtc == DateTime.MinValue)
            _familiarGoneSinceUtc = UtcNow();

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
            _trickStarvedSinceUtc = UtcNow();

        var cheerReady = CanFire(svc, level, BSTActions.RallyingCheer, selfId);
        var cheer = context.Gauge is { } cheerGauge
            ? ArtemisCooldownRules.ShouldRallyingCheerFromGauge(
                cfg.EnableRally, cheerReady, context.HasFamiliar, cheerGauge.PetTp)
            : ArtemisCooldownRules.ShouldRallyingCheer(
                cfg.EnableRally, cheerReady, context.HasFamiliar, SecondsSince(_trickStarvedSinceUtc));
        // On the infinity line Cheer waits for its moment: after your answer, to pay for the second Trick.
        if (cheer && context.Gauge is { } lineGauge && context.FamiliarBeast is { } lineBeast
            && ArtemisCooldownRules.HoldsCheerForInfinityLine(
                ArtemisCooldownRules.UsesInfinityLine(level, lineGauge, rallyReady, cheerReady),
                ChainState(context).LitHeart, lineBeast.TrickAffinity))
        {
            cheer = false;
        }
        if (cheer && TryGate(BSTActions.RallyingCheer.ActionId))
        {
            scheduler.PushOgcd(ArtemisAbilities.RallyingCheer, selfId, priority: 14,
                onDispatched: _ => context.Debug.PlannedAction = BSTActions.RallyingCheer.Name);
        }

        // Beast Mode is whatever the Kinship has made of it; ask the game about THAT action, at the target
        // it takes.
        var onBoard = Daedalus.Services.Beastmaster.CrucibleTracker.Current.OnBoard;
        var familiarKin = context.HasFamiliar ? context.FamiliarBeast?.Classification : null;
        var modeId = svc.GetAdjustedActionId(BSTActions.BeastMode.ActionId);
        var borrowReady = CanFire(svc, level, BSTActions.Borrow, selfId);
        if (PreBorrowedCardId != 0 && modeId != PreBorrowedCardId)
            PreBorrowedCardId = 0; // used, or replaced by a Borrow for a job

        // Casts to interrupt and buffs to strip, read before anything spends the Borrow.
        var interruptCandidates = new List<(ulong, float, bool)>();
        var buffed = new List<(ulong, float)>();
        ReadKinshipJobs(context, onBoard, interruptCandidates, buffed);

        // The Ogre's small wisps: one Quelling Wave each, so the Wave is borrowed from the Megalocrab and kept for them.
        var smallWisps = new List<(ulong, float)>();
        var ogreFight = onBoard && FindSmallWisps(context, smallWisps);
        var wispWave = ArtemisCooldownRules.WantsQuellingWaveForWisps(onBoard, ogreFight);

        // The battle's card beast (the Banemite's Geshunpest): its summon goes on Borrow, not on its release.
        var cardBeastNo = onBoard && Daedalus.Services.Beastmaster.CrucibleTracker.Current.Enemy is { } cardFoe
            ? CrucibleDegreeTeams.CardBeast(Daedalus.Services.Beastmaster.CrucibleHints.Degree, cardFoe.Battle)
            : null;
        var borrowForCard = ArtemisCooldownRules.BorrowsInPlaceOfRelease(
            cfg.EnableBorrow, cardBeastNo, context.FamiliarBeast?.BestiaryNo, familiarKin, modeId);
        // The release waits only while Borrow's recast is done: a Borrow that cannot come leaves the release its turn.
        _cardBorrowPending = borrowForCard && svc.IsActionReady(BSTActions.Borrow.ActionId);

        // Ymir Plan A: Soul Crush is borrowed from the Ice Golem as it comes out and kept for the Sahagin's Dreadwash.
        var ymirSoulCrush = _ymir.Active && _ymir.SahaginId != 0;
        if (borrowForCard)
        {
            if (borrowReady && TryGate(BSTActions.Borrow.ActionId))
            {
                var cardFrom = context.FamiliarBeast?.Name ?? "the familiar";
                scheduler.PushOgcd(ArtemisAbilities.Borrow, selfId, priority: 2, onDispatched: _ =>
                {
                    context.Debug.PlannedAction = $"Borrow ({cardFrom}'s card, in place of its release)";
                    Daedalus.Rotation.Base.RotationServices.DebugLog?.Log(
                        Daedalus.Services.Debug.DebugLogCategory.Action, Daedalus.Services.Debug.DebugLogSeverity.Info,
                        $"[Crucible] Borrow from {cardFrom}: its card is kept for this battle, its release is not used");
                });
            }
        }
        else if (ArtemisCooldownRules.ShouldBorrowForJob(
                cfg.EnableBorrow, borrowReady, familiarKin, modeId, interruptCandidates.Count > 0 || ymirSoulCrush,
                buffed.Count > 0 || wispWave)
            && TryGate(BSTActions.Borrow.ActionId))
        {
            var job = interruptCandidates.Count > 0 && familiarKin == BeastClassification.Soulkin ? "Soul Crush" : "Quelling Wave";
            scheduler.PushOgcd(ArtemisAbilities.Borrow, selfId, priority: 2, onDispatched: _ =>
            {
                context.Debug.PlannedAction = $"Borrow (for {job})";
                Daedalus.Rotation.Base.RotationServices.DebugLog?.Log(
                    Daedalus.Services.Debug.DebugLogCategory.Action, Daedalus.Services.Debug.DebugLogSeverity.Info,
                    $"[BST] Borrow now: {job} is needed");
            });
        }
        else if (ArtemisCooldownRules.ShouldBorrow(cfg.EnableBorrow, borrowReady, context.HasFamiliar, _releaseOwed)
                 && !(ymirSoulCrush && modeId == BSTActions.SoulCrushId)
                 && !ArtemisCooldownRules.KeepsPreBorrowedCard(onBoard, modeId, PreBorrowedCardId, familiarKin)
                 && !ArtemisCooldownRules.KeepsKinshipCard(onBoard, modeId, familiarKin, selfHpPct,
                     hitComing: Daedalus.Services.Beastmaster.CrucibleHints.SecondsUntilTankbuster <= ArtemisCooldownRules.TankbusterLeadSeconds
                                || Daedalus.Services.Beastmaster.CrucibleHints.SecondsUntilRaidwide <= ArtemisCooldownRules.RaidwideLeadSeconds)
                 && TryGate(BSTActions.Borrow.ActionId))
        {
            scheduler.PushOgcd(ArtemisAbilities.Borrow, selfId, priority: 16,
                onDispatched: _ => context.Debug.PlannedAction = BSTActions.Borrow.Name);
        }

        // In a card beast's battle Scouring Ash is for the Poison alone: spent on anything else, its 60 s recast
        // would not be back for the hit it was borrowed for.
        var poisoned = modeId == BSTActions.ScouringAshId && HasStatus(player, CrucibleAutoPilotRules.CruciblePoisonStatusId);
        var cleansable = poisoned
            || (cardBeastNo == null
                && context.DebuffDetectionService.FindHighestPriorityDebuff(player).priority
                    != Daedalus.Services.Debuff.DebuffPriority.None);
        var use = ArtemisCooldownRules.DecideBeastMode(
            cfg.EnableBorrow, modeId, selfHpPct, edgeDistance, cleansable,
            holdSoulCrush: ArtemisCooldownRules.HoldsSoulCrush(onBoard, borrowReady, familiarKin),
            coveredOnBoard: onBoard && CoverLeft(player) > 0f);

        // Quelling Wave strips a buff (Drake's Blaze Spikes, Strix's Ultimate Focus, the toad's regen ...), at whoever
        // carries one — even an enemy the boss engine says not to hit, which is usually why.
        if (modeId == BSTActions.QuellingWaveId && cfg.EnableBorrow && buffed.Count > 0
            && ActionAvailability.MeetsLevelAndLearned(level, svc, BSTActions.BeastMode))
        {
            var dispelId = ArtemisCooldownRules.PickDispelTarget(targetId, buffed);
            if (dispelId != 0 && svc.GetActionStatusCode(modeId, dispelId) == 0 && TryGate(BSTActions.BeastMode.ActionId))
            {
                var buffedName = context.ObjectTable.SearchById(dispelId)?.Name.TextValue ?? "enemy";
                scheduler.PushOgcd(ArtemisAbilities.BeastModeRanged, dispelId, priority: 3, onDispatched: _ =>
                {
                    context.Debug.PlannedAction = $"Quelling Wave (dispel {buffedName})";
                    Daedalus.Rotation.Base.RotationServices.DebugLog?.Log(
                        Daedalus.Services.Debug.DebugLogCategory.Action, Daedalus.Services.Debug.DebugLogSeverity.Info,
                        $"[BST] Quelling Wave: dispelling {buffedName}");
                });
            }
        }

        // Quelling Wave on the Ogre's small wisps: instant, 30 y, and one kills one, so they never reach the ward.
        if (cfg.EnableBorrow && smallWisps.Count > 0
            && ActionAvailability.MeetsLevelAndLearned(level, svc, BSTActions.BeastMode))
        {
            var wispId = ArtemisCooldownRules.PickWispForQuellingWave(onBoard, modeId, targetId, smallWisps);
            if (wispId != 0 && svc.GetActionStatusCode(modeId, wispId) == 0 && TryGate(BSTActions.BeastMode.ActionId))
            {
                scheduler.PushOgcd(ArtemisAbilities.BeastModeRanged, wispId, priority: 3, onDispatched: _ =>
                {
                    context.Debug.PlannedAction = "Quelling Wave (a small wisp)";
                    Daedalus.Rotation.Base.RotationServices.DebugLog?.Log(
                        Daedalus.Services.Debug.DebugLogCategory.Action, Daedalus.Services.Debug.DebugLogSeverity.Info,
                        $"[Crucible] Quelling Wave on a small wisp ({smallWisps.Count} up)");
                });
            }
        }

        // Soul Crush interrupts: aimed at a caster to stop, ahead of its free-damage use.
        if (modeId == BSTActions.SoulCrushId && cfg.EnableBorrow
            && ActionAvailability.MeetsLevelAndLearned(level, svc, BSTActions.BeastMode)
            && interruptCandidates.Count > 0)
        {
            var interruptId = ArtemisCooldownRules.PickInterruptTarget(interruptCandidates);
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
                // A Poison ticks every 3 s: its cleanse goes ahead of everything but an interrupt.
                var ashForPoison = modeId == BSTActions.ScouringAshId && poisoned;
                scheduler.PushOgcd(
                    use == BeastModeUse.Enemy ? ArtemisAbilities.BeastModeAtEnemy : ArtemisAbilities.BeastMode,
                    modeTarget, priority: ashForPoison ? 3 : 18,
                    onDispatched: _ =>
                    {
                        context.Debug.PlannedAction = ashForPoison ? "Scouring Ash (Poison)" : "Beast Mode (Kinship action)";
                        if (ashForPoison && onBoard)
                            Daedalus.Rotation.Base.RotationServices.DebugLog?.Log(
                                Daedalus.Services.Debug.DebugLogCategory.Action, Daedalus.Services.Debug.DebugLogSeverity.Info,
                                $"[Crucible] Scouring Ash: cleansing the Poison at {selfHpPct:P0}");
                    });
            }
        }

        var target = context.TargetingService.GetUserEnemyTarget() ?? context.TargetingService.GetRawEnemyHardTarget();
        var dashSafe = target != null && target.GameObjectId == targetId
            && !context.TargetingService.GapCloserSafety.ShouldBlockGapCloser(target, player);
        var charge = ArtemisCooldownRules.DecideShieldCharge(
            cfg.EnableShieldCharge, cfg.EnableShieldChargeGapClose, CanFire(svc, level, BSTActions.ShieldCharge, targetId),
            (int)svc.GetCurrentCharges(BSTActions.ShieldCharge.ActionId), edgeDistance, BSTActions.ShieldCharge.Range, dashSafe);
        // The charge goes at targetId, which need not be the hard target read above (a null there let it through
        // and broke two eggs, Korha 2026-10-31 19:20): find that object itself.
        var chargeTarget = charge == ShieldChargeUse.None ? null : FindObject(context, targetId);
        var eggInSplash = chargeTarget != null
            && Daedalus.Services.Beastmaster.CrucibleTracker.Current.OnBoard
            && ReadZu(context, out var eggs) != null
            && ArtemisCooldownRules.ShieldChargeBreaksEgg(eggs,
                new System.Numerics.Vector2(chargeTarget.Position.X, chargeTarget.Position.Z), chargeTarget.HitboxRadius,
                new System.Numerics.Vector2(player.Position.X, player.Position.Z), player.HitboxRadius);
        LogEggHold(eggInSplash);
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

        var chain = ChainState(context);
        var selfId = context.Player.GameObjectId;

        // The infinity line: answer with the colour just before the familiar's, so its next Trick is intentional.
        if (awaiting && context.Gauge is { } lineGauge
            && ArtemisCooldownRules.UsesInfinityLine(level, lineGauge,
                CanFire(svc, level, BSTActions.Rally, selfId), CanFire(svc, level, BSTActions.RallyingCheer, selfId)))
        {
            var petLit = landed && lineGauge.HasHeart ? lineGauge.Affinity : pairing.PetAffinity;
            var setUp = BSTActions.PreviousInCycle(petLit);
            if (BSTActions.InstinctualFor(setUp) is { } setUpAxe && ActionAvailability.MeetsLevelAndLearned(level, svc, setUpAxe))
                answerAffinity = setUp;
        }

        var pendingAnswer = awaiting ? BSTActions.InstinctualFor(answerAffinity) : answer;
        var axeReady = pendingAnswer != null && CanFire(svc, level, pendingAnswer, targetId);
        var trickReady = !awaiting && CanFire(svc, level, BSTActions.Trick, targetId);

        // A Trick that completes an intentional combo is a link of its own: it goes now, without its axe.
        if (trickReady && ArtemisCooldownRules.TrickWouldBeIntentional(chain.LitHeart, beast.TrickAffinity))
        {
            pairing.ClearHold();
            PushTrick(context, scheduler, targetId, beast.TrickAffinity,
                $"Trick ({beast.TrickAffinity}) — intentional on the lit {chain.LitHeart}");
            context.Debug.InstinctChain = pairing.Describe();
            return;
        }

        var oneSideReady = !awaiting && axeReady != trickReady;
        var holdExpired = oneSideReady && pairing.HoldExpired();
        if (!oneSideReady)
            pairing.ClearHold();

        var step = DecidePairing(awaiting, landed || pairing.ReadyToAnswer, axeReady, trickReady, holdExpired);

        // Holding one half for the other must not let a live chain lapse: at the last moment, extend it.
        if (step is PairingStep.Hold or PairingStep.Nothing && ArtemisCooldownRules.SavesChain(chain.Live, chain.SecondsLeft))
        {
            var extension = ArtemisCooldownRules.ExtensionAffinity(chain.LitHeart, beast.TrickAffinity,
                a => BSTActions.InstinctualFor(a) is { } axe && CanFire(svc, level, axe, targetId));
            if (BSTActions.InstinctualFor(extension) is { } extensionAxe)
            {
                Push(context, scheduler, extensionAxe, targetId, priority: 10,
                    why: $"{extension} — keeps the chain ({chain.SecondsLeft:0.0}s left)", onDispatched: pairing.ClearHold);
                context.Debug.InstinctChain = pairing.Describe();
                return;
            }
            if (trickReady)
                step = PairingStep.TrickAlone;
        }

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

    /// <summary>
    /// The chain right now: whether it is live, the Heart lit (None when the last link was intentional — a half is lit
    /// — or unknown), and the seconds left on its window. The gauge says what is lit; the tracker times the window.
    /// </summary>
    private static (bool Live, InstinctAffinity LitHeart, float SecondsLeft) ChainState(IArtemisContext context)
    {
        var tracker = context.Instinct;
        if (context.Gauge is { } gauge)
            return (gauge.HasHeart || gauge.HasHalf, gauge.HasHeart ? gauge.Affinity : InstinctAffinity.None, tracker.SecondsLeft);
        return (tracker.IsWindowOpen, tracker.LastWasIntentional ? InstinctAffinity.None : tracker.LastAffinity, tracker.SecondsLeft);
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

    /// <summary>
    /// The familiar is falling and Parting Blow is not ready: the job swaps it out with another horn (Artemis reads this
    /// each frame). Recomputed every frame the familiar orders run; false otherwise.
    /// </summary>
    public bool SwapOutWanted { get; private set; }

    /// <summary>
    /// A tankbuster is coming that neither the kept beast nor the beastmaster should take: Artemis blows the horn of a
    /// beast that can be spent (<see cref="ArtemisCooldownRules.WantsSpareForBuster"/>). From the last frame.
    /// </summary>
    public bool SpareForBusterWanted { get; private set; }

    /// <summary>A Tempered Release is still owed this summon; Borrow and Parting Blow wait for it.</summary>
    private bool _releaseOwed;

    /// <summary>Tankbuster casts seen this fight, and whether one is being cast now (CrucibleAutoPilotRules.TankbusterCastIds).</summary>
    private int _bustersCast;
    private bool _busterCasting;

    /// <summary>The beast out is the plan's tankbuster beast and its tankbusters are not over: no retreat (ArtemisCooldownRules.HoldsBusterBeast).</summary>
    private bool _heldForBuster;

    /// <summary>Beasts the plan has already sent off this fight (ArtemisCooldownRules.LeavesNow), and the one being sent now.</summary>
    private readonly HashSet<int> _leftOnce = [];
    private int _leavingNo;

    /// <summary>The familiar out is the battle's card beast and Borrow is ready for it: its release waits (and is lost to the Borrow).</summary>
    private bool _cardBorrowPending;

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

    /// <summary>The rotation can press an off-GCD action this frame (ActionService.CanExecuteOgcd).</summary>
    private bool _weaveOpen = true;

    /// <summary>True, and the try is stamped, when this action has not been offered in the last two seconds.</summary>
    private bool TryGate(uint actionId)
    {
        // Only a frame that can press it counts as a try. One off-GCD press fits a GCD, in a slot half a second wide;
        // an offer stamped outside it was thrown away and the next came two seconds later, by which time the slot had
        // usually passed again (Rally a median 4.2 s after the axe it should follow, 189 recorded uses).
        if (!_weaveOpen)
            return false;

        var now = UtcNow();
        if (_lastTryUtc.TryGetValue(actionId, out var last) && (now - last).TotalSeconds < ReleaseRetrySeconds)
            return false;

        _lastTryUtc[actionId] = now;
        return true;
    }

    private double SecondsSince(DateTime since)
        => since == DateTime.MinValue ? 0d : (UtcNow() - since).TotalSeconds;

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
        SwapOutWanted = false;
        // A beast sent off by the plan has gone (or another is out): it is not sent off again this fight.
        if (_leavingNo != 0 && context.FamiliarBeast?.BestiaryNo != _leavingNo)
        {
            _leftOnce.Add(_leavingNo);
            _leavingNo = 0;
        }

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
            _familiarTrend.Sample(familiarObject.GameObjectId, familiarHpPct, UtcNow());
        _fallingLine = ArtemisCooldownRules.FallingLine(_familiarTrend.BiggestHitPct, _familiarTrend.LossPerSecond,
            ArtemisCooldownRules.FallingFloor(Daedalus.Services.Beastmaster.CrucibleHints.Degree));
        var falling = ArtemisCooldownRules.IsFamiliarFalling(
            cfg.CruciblePartingBlowWhenFalling, Daedalus.Services.Beastmaster.CrucibleTracker.Current.OnBoard, familiarHpPct,
            player.MaxHp > 0 ? (float)player.CurrentHp / player.MaxHp : 1f,
            Daedalus.Services.Beastmaster.CrucibleHints.SecondsUntilTankbuster, _fallingLine,
            RaidwideIn(context), replacementReady: !context.NoReplacementHorn);
        LogRetreatHeld(context.NoReplacementHorn
                       && ArtemisCooldownRules.IsFamiliarFalling(
                           cfg.CruciblePartingBlowWhenFalling, Daedalus.Services.Beastmaster.CrucibleTracker.Current.OnBoard,
                           familiarHpPct, player.MaxHp > 0 ? (float)player.CurrentHp / player.MaxHp : 1f,
                           Daedalus.Services.Beastmaster.CrucibleHints.SecondsUntilTankbuster, _fallingLine,
                           RaidwideIn(context)),
            familiarHpPct);
        var crucible = Daedalus.Services.Beastmaster.CrucibleTracker.Current;
        var ogreFight = FindAdds(context, ArtemisCooldownRules.OgrePieceNameId, WispNameIds, out _) != null;
        var degree = Daedalus.Services.Beastmaster.CrucibleHints.Degree;
        // The plan's tankbuster beast is not retreated until its tankbusters have been cast (the video's Ziz).
        var planBattle = crucible.OnBoard ? crucible.Enemy?.Battle ?? 0 : 0;
        var busterBeast = CrucibleDegreeTeams.BusterBeast(degree, planBattle);
        _heldForBuster = ArtemisCooldownRules.HoldsBusterBeast(
            busterBeast?.Beast, context.FamiliarBeast?.BestiaryNo, _bustersCast, busterBeast?.Busters ?? 0, _busterCasting);
        if (_heldForBuster && falling)
        {
            falling = false;
            context.Debug.InstinctState = $"{context.FamiliarBeast?.Name}: kept out for the next tankbuster ({_bustersCast} of {busterBeast?.Busters} cast)";
        }

        // A beast the plan keeps for a later battle and is done with here goes at once (its release first).
        var outNo = context.FamiliarBeast?.BestiaryNo;
        var leavesNow = ArtemisCooldownRules.LeavesNow(
            crucible.OnBoard, outNo is { } leaver && CrucibleDegreeTeams.LeavesAtOnce(degree, planBattle, leaver),
            outNo is { } left && _leftOnce.Contains(left), !context.NoReplacementHorn);
        var stingFight = ArtemisCooldownRules.WespeStingFight(
            crucible.Board?.Index ?? 0, ogreFight, crucible.Enemy?.IsFinalBoss == true, degree);
        // On a degree the Ogre fight keeps a familiar for the wisps: no cycling to the Wespe until they are gone.
        var cycleToWespe = stingFight && ArtemisCooldownRules.CyclesToWespeInKnightFight(_knightFight, _knightGuardSeen)
                           && !ArtemisCooldownRules.OgreKeepsFamiliarForWisps(ogreFight, degree, _ogreWisps.PhaseOver)
                           && ArtemisCooldownRules.CyclesToWespe(
            Daedalus.Services.Beastmaster.CrucibleTracker.Current.OnBoard, context.WespeWaiting,
            context.FamiliarBeast?.BestiaryNo == WespeBestiaryNo, !context.NoReplacementHorn, RaidwideIn(context),
            Daedalus.Services.Beastmaster.CrucibleHints.SecondsUntilTankbuster);
        var bestiaryNo = context.FamiliarBeast?.BestiaryNo;
        var ymirRelease = _ymir.Active
            ? ArtemisCooldownRules.YmirReleasePlan(bestiaryNo, _ymir.Shelled)
            : ArtemisCooldownRules.YmirRelease.Normal;
        var ymirHold = ymirRelease is ArtemisCooldownRules.YmirRelease.Hold or ArtemisCooldownRules.YmirRelease.Never;
        var ymirPartingBlow = _ymir.Active && ArtemisCooldownRules.YmirPartingBlowNow(bestiaryNo, _ymir.Shelled);
        var ymirHoldsPartingBlow = _ymir.Active && ArtemisCooldownRules.YmirHoldsPartingBlow(bestiaryNo, _ymir.Shelled, falling);
        var partingBlowAllowed = ArtemisCooldownRules.AllowsPartingBlow(cfg.EnablePartingBlow, falling, crucible.OnBoard)
                                 || cycleToWespe || ymirPartingBlow || leavesNow;
        SwapOutWanted = cfg.CruciblePartingBlowWhenFalling
                        && ArtemisCooldownRules.ShouldSwapOutFalling(falling, CanFire(svc, level, BSTActions.PartingBlow, targetId));
        if (SwapOutWanted)
            context.Debug.InstinctState = $"Familiar falling ({familiarHpPct:P0}) and Parting Blow not ready — swapping it out";

        var releaseReady = CanFire(svc, level, BSTActions.TemperedRelease, targetId);
        var releaseTarget = ymirRelease == ArtemisCooldownRules.YmirRelease.AtShell && _ymir.ShellId != 0 ? _ymir.ShellId
            : ymirRelease == ArtemisCooldownRules.YmirRelease.AtYmir ? _ymir.YmirId
            : targetId;
        var stingFinisher = Daedalus.Services.Beastmaster.CrucibleTracker.Current.OnBoard && stingFight
                            && !ArtemisCooldownRules.OgreHoldsStingForWisps(
                                ogreFight, degree, _ogreWisps.PhaseOver, !context.NoReplacementHorn)
                            && (ArtemisCooldownRules.StingsAtOnce(degree)
                                || (targetHpPct > 0f && targetHpPct <= FinalStingAtTargetHpPct)
                                || ArtemisCooldownRules.StingBeforeOgreWard(
                                    ogreFight,
                                    (UtcNow() - _combatSinceUtc).TotalSeconds));
        // The Mantis holds its Soundwave (+10% physical vulnerability, 30 s) until just before the swap to a waiting
        // Wespe, so Final Sting lands inside it. While it holds, its release counts as owed: no Parting Blow yet.
        var mantisHold = context.FamiliarBeast?.BestiaryNo == ArtemisCooldownRules.MantisBestiaryNo && releaseReady
                         && !ArtemisCooldownRules.MantisReleasesNow(
                             stingFight, context.WespeInRoster, falling, targetHpPct, ogreFight,
                             (UtcNow() - _combatSinceUtc).TotalSeconds);
        if (mantisHold)
            context.Debug.InstinctState = "Mantis: Soundwave held for the Wespe's Final Sting";
        var zuHold = ArtemisCooldownRules.HoldReleaseAtZu(
            Daedalus.Services.Beastmaster.CrucibleTracker.Current.OnBoard, ReadZu(context, out _), falling);
        if (zuHold && releaseReady)
            context.Debug.InstinctState = "Zu: release held (it breaks eggs) until the Zu is nearly dead";
        var meteorHold = releaseReady
                         && (HoldMeteorForMitelings(context, releaseTarget, falling) || HoldMeteorForSuccubi(context, falling));
        if (ymirHold && releaseReady)
            context.Debug.InstinctState = ymirRelease == ArtemisCooldownRules.YmirRelease.Never
                ? "Ymir: the Ice Golem's release is not used"
                : "Ymir: the Diremite's Bind waits for the shell to break";
        // The card beast's summon is for Borrow: a release first would spend the One with Nature Borrow needs.
        var cardHold = releaseReady && _cardBorrowPending;
        if (cardHold)
            context.Debug.InstinctState = "release not used: this beast's summon goes on Borrow";
        var release = _weaveOpen && !zuHold && !mantisHold && !meteorHold && !ymirHold && !cardHold && ShouldTemperedRelease(
            cfg.EnableTemperedRelease, releaseReady, context.FamiliarBeast?.BestiaryNo, partingBlowAllowed,
            player.MaxHp > 0 ? (float)player.CurrentHp / player.MaxHp : 1f,
            (UtcNow() - _lastReleaseTryUtc).TotalSeconds, stingFinisher);
        if (release)
        {
            _lastReleaseTryUtc = UtcNow();
            if (context.FamiliarBeast?.BestiaryNo == ArtemisCooldownRules.MantisBestiaryNo && crucible.OnBoard)
                Daedalus.Rotation.Base.RotationServices.DebugLog?.Log(
                    Daedalus.Services.Debug.DebugLogCategory.Action, Daedalus.Services.Debug.DebugLogSeverity.Info,
                    $"[Crucible] Mantis Soundwave: sting fight {stingFight} ({crucible.Enemy?.Name ?? "no enemy"}), "
                    + $"Wespe in horns {context.WespeInRoster}, Wespe ready {context.WespeWaiting}, target {targetHpPct:P0}, "
                    + $"falling {falling}, {(UtcNow() - _combatSinceUtc).TotalSeconds:0}s in");
            scheduler.PushOgcd(ArtemisAbilities.TemperedRelease, releaseTarget, priority: 12,
                onDispatched: _ => context.Debug.PlannedAction = BSTActions.TemperedRelease.Name);
        }

        // Parting Blow sends the familiar away and takes an unspent Tempered Release with it, so it waits
        // while a release is still owed (the release is also what grants Lingering Vantage, +500 on the Blow).
        var releaseOwed = ymirRelease != ArtemisCooldownRules.YmirRelease.Never && cfg.EnableTemperedRelease && releaseReady
            && (mantisHold || meteorHold || cardHold || ShouldTemperedRelease(true, true, context.FamiliarBeast?.BestiaryNo, partingBlowAllowed,
                    player.MaxHp > 0 ? (float)player.CurrentHp / player.MaxHp : 1f, double.MaxValue, stingFinisher)
                || (stingFight && SavesFinalSting(Daedalus.Services.Beastmaster.CrucibleTracker.Current.OnBoard, context.FamiliarBeast?.BestiaryNo)));
        _releaseOwed = releaseOwed;
        // Not straight after a Trick: the familiar is slow to act, and retreating it now loses the Trick (RSR 8f258dbc).
        // The falling retreat is exempt — it saves the familiar.
        var trickInFlight = !falling && svc.WasLastOgcd(BSTActions.Trick.ActionId);
        var partingBlowTarget = ymirPartingBlow && !falling ? _ymir.YmirId : targetId;
        if (!releaseOwed && partingBlowAllowed && !ymirHoldsPartingBlow && !trickInFlight
            && CanFire(svc, level, BSTActions.PartingBlow, partingBlowTarget)
            && !PartingBlowBreaksZuEgg(context, targetId))
        {
            var line = _fallingLine;
            var outBeast = context.FamiliarBeast?.Name ?? "familiar";
            scheduler.PushOgcd(
                falling ? ArtemisAbilities.PartingBlowRetreat
                : cycleToWespe ? ArtemisAbilities.CruciblePartingBlow
                : ArtemisAbilities.PartingBlow, partingBlowTarget,
                priority: falling ? 5 : leavesNow ? 9 : 15,
                onDispatched: _ =>
                {
                    if (leavesNow && !falling && outNo is { } going)
                    {
                        _leavingNo = going;
                        Daedalus.Rotation.Base.RotationServices.DebugLog?.Log(
                            Daedalus.Services.Debug.DebugLogCategory.Action, Daedalus.Services.Debug.DebugLogSeverity.Info,
                            $"[Crucible] Parting Blow: {outBeast} is kept for a later battle and done here — next horn");
                    }

                    context.Debug.PlannedAction = falling
                        ? "Parting Blow (familiar falling — retreat it)"
                        : leavesNow
                            ? "Parting Blow (kept for a later battle — next horn)"
                        : cycleToWespe
                            ? "Parting Blow (next horn — bringing the Wespe for Final Sting)"
                            : BSTActions.PartingBlow.Name;
                    if (falling)
                        Daedalus.Rotation.Base.RotationServices.DebugLog?.Log(
                            Daedalus.Services.Debug.DebugLogCategory.Action, Daedalus.Services.Debug.DebugLogSeverity.Info,
                            $"[Crucible] Parting Blow: familiar at {familiarHpPct:P0} (falling line {line:P0}), sent to retreat it");
                    else if (cycleToWespe)
                        Daedalus.Rotation.Base.RotationServices.DebugLog?.Log(
                            Daedalus.Services.Debug.DebugLogCategory.Action, Daedalus.Services.Debug.DebugLogSeverity.Info,
                            $"[Crucible] Parting Blow: {outBeast}'s release is spent — next horn, towards the Wespe's Final Sting");
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
