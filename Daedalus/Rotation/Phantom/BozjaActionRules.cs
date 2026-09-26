using System;
using Daedalus.Config;
using Daedalus.Data;

namespace Daedalus.Rotation.Phantom;

/// <summary>Pure decision predicates for the Bozja Lost Action layer. Fed live values by BozjaActionLayer.</summary>
public static class BozjaActionRules
{
    /// <summary>
    /// After we send a cast at someone, leave that target alone this long for the same action. A 2.5s cast
    /// plus the moment the status takes to land — without this the next GCD sees nothing and casts again.
    /// </summary>
    public const double RecentCastGuardSeconds = 4.0;

    /// <summary>An area heal needs at least this many people under the threshold (4-man parties: not 3).</summary>
    public const int AreaHealMinTargets = 2;

    /// <summary>An area barrier needs at least this many people near us without one.</summary>
    public const int AreaBarrierMinTargets = 2;

    /// <summary>Lost Burst / Rampage spam mode: enemies within 10y needed (RSR's AoeCount).</summary>
    public const int AversionAoeMinEnemies = 2;

    /// <summary>
    /// Whether a target still needs this buff. <paramref name="remainingOf"/> gives the seconds left on a
    /// status, or null when absent. In combat only a missing buff is cast: every one of these is a GCD.
    /// Out of combat it is renewed once under the action's refresh window.
    /// </summary>
    public static bool NeedsBuff(LostActionDef def, Func<uint, float?> remainingOf, bool inCombat)
    {
        var threshold = inCombat ? 0f : def.RefreshOutOfCombatSeconds;
        foreach (var statusId in def.Provides)
        {
            if (remainingOf(statusId) is { } remaining && remaining > threshold)
                return false;
        }

        return true;
    }

    public static bool TargetAllowed(BozjaConfig cfg, bool isSelf)
        => isSelf || cfg.BuffParty;

    public static bool NeedsHeal(BozjaConfig cfg, float hpPct)
        => hpPct < cfg.HealHpPct;

    /// <summary>
    /// Who a forge is for. The aversions sit on the ENEMY ("vulnerable to physical/magic attacks"):
    /// against a magic-averse enemy, physical attackers get Spellforge (their hits become magic); against a
    /// physical-averse one, magic users get Steelsting. RSR checks the aversion on the friendly target it
    /// is buffing — which never carries it — so RSR's version cannot fire.
    /// </summary>
    public static bool WantsForge(bool isSpellforge, bool memberDealsMagicDamage,
        bool enemyMagicallyAverse, bool enemyPhysicallyAverse)
        => isSpellforge
            ? enemyMagicallyAverse && !memberDealsMagicDamage
            : enemyPhysicallyAverse && memberDealsMagicDamage;

    /// <summary>
    /// Lost Burst / Rampage. Spam mode (RSR's default): AoE damage whenever enough enemies are close,
    /// aversion ignored. Otherwise only against an averse target that lacks the debuff.
    /// </summary>
    public static bool ShouldAversionAoe(bool spam, int enemiesInRange, bool targetAverse, bool targetDebuffed)
        => spam
            ? enemiesInRange >= AversionAoeMinEnemies
            : targetAverse && !targetDebuffed;

    /// <summary>
    /// Lost Seraph Strike. Cleric Stance cuts healing by 60% for 15s, so a healer never takes it; and it is
    /// a leap, so it waits for a landing the boss engine calls safe.
    /// </summary>
    public static bool SeraphAllowed(bool isHealer, bool hasClericStance, bool dashSafe)
        => !isHealer && !hasClericStance && dashSafe;

    /// <summary>
    /// Whether the layer may take the GCD ahead of the job (RSR runs duty GCDs before the job's own).
    /// A raise we queued always may. Otherwise never with a body the job itself can raise, and never ahead
    /// of a healer's GCDs in combat — a healer's rotation decides its GCDs, the layer takes free ones.
    /// </summary>
    public static bool MayPreemptGcd(bool inCombat, bool isHealer, bool raisePending, bool raiseQueued)
        => raiseQueued || (!raisePending && (!inCombat || !isHealer));
}
