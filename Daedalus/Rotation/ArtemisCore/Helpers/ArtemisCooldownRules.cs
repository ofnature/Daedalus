using Daedalus.Data;

namespace Daedalus.Rotation.ArtemisCore.Helpers;

/// <summary>What to do with the Beast Mode button this frame.</summary>
public enum BeastModeUse
{
    None,

    /// <summary>Press it on ourselves (the skins, Seedsower, Scouring Ash).</summary>
    Self,

    /// <summary>Press it at the enemy (Soul Crush).</summary>
    Enemy,
}

/// <summary>
/// When the Beastmaster's remaining buttons go out: Rally, Rallying Cheer, Shield Charge, Borrow and the
/// Beast Mode action Borrow unlocks. Pure, so every rule is testable without a game.
/// <para>
/// None of this reads a gauge, because there is none to read — TP, Familiar TP and the instinct diamonds
/// live only in the job HUD. What the game does expose is whether an action is usable, so "the axes are off
/// cooldown and still refused" stands in for "out of TP".
/// </para>
/// </summary>
public static class ArtemisCooldownRules
{
    /// <summary>
    /// How long an axe (or Trick) must sit refused-but-off-cooldown before it counts as out of TP. An action
    /// is briefly unusable during any animation lock, and that is not starvation.
    /// </summary>
    public const double StarvedSeconds = 1.0;

    /// <summary>
    /// Mastered Instinct to bank before Rally while combos are being paired with Trick. Rally is 40 TP plus
    /// 70 per diamond: with none it is barely worth a 90-120s recast, with two it is a full axe.
    /// </summary>
    public const int RallyMinMastered = 2;

    /// <summary>The game holds at most three diamonds.</summary>
    public const int MaxMastered = 3;

    /// <summary>Reach of the axes; inside it Shield Charge has nowhere to carry the beastmaster.</summary>
    public const float MeleeYalms = 3f;

    /// <summary>Seedsower's radius.</summary>
    public const float SeedsowerYalms = 6f;

    /// <summary>Below this the defensive skins are worth pressing.</summary>
    public const float SkinHpPct = 0.80f;

    /// <summary>
    /// Rally (Lv28): spends Mastered Instinct for TP. The axes spend the whole bar and hit harder the fuller
    /// it is, so the moment to refill is right after one has emptied it. While Trick pairing is banking
    /// diamonds it waits for <see cref="RallyMinMastered"/> (our own count — the real one is not readable);
    /// with no pairing going on there are no diamonds coming, and 40 TP beats an idle button.
    /// </summary>
    public static bool ShouldRally(
        bool enabled, bool ready, double axeStarvedSeconds, bool pairing, int masteredEstimate)
        => enabled && ready && axeStarvedSeconds >= StarvedSeconds
           && (!pairing || masteredEstimate >= RallyMinMastered);

    /// <summary>Level at which 250 TP turns the axes into their upgraded forms (trait Instinctual Mastery).</summary>
    public const byte UpgradeLevel = 50;

    /// <summary>
    /// Diamonds worth waiting for before Rally. At 50 it is all three: 40 + 3 x 70 is exactly the 250 that
    /// upgrades the axes, and two would leave the bar at 180 with nothing upgraded. Below 50 there is no
    /// upgrade to reach, so two (a full-strength axe) is enough.
    /// </summary>
    public static int RallyMinMasteredFor(byte level) => level >= UpgradeLevel ? MaxMastered : RallyMinMastered;

    /// <summary>
    /// Rally, read off the gauge instead of inferred. Below 100 TP no axe can be pressed, so the refill
    /// cannot overwrite anything usable; with a familiar out it waits for the diamonds, without one there
    /// are none coming.
    /// </summary>
    public static bool ShouldRallyFromGauge(
        bool enabled, bool ready, int tp, int masteredInstinct, byte level, bool hasFamiliar)
        => enabled && ready && tp < ArtemisGauge.MinSpendTp
           && (!hasFamiliar || masteredInstinct >= RallyMinMasteredFor(level));

    /// <summary>Rallying Cheer, read off the gauge: the familiar is out and cannot afford a Trick.</summary>
    public static bool ShouldRallyingCheerFromGauge(bool enabled, bool ready, bool hasFamiliar, int petTp)
        => enabled && ready && hasFamiliar && petTp < ArtemisGauge.MinSpendTp;

    /// <summary>
    /// The level 50 finisher. With half of the centre lit and a full 250 TP, the upgraded skill of the
    /// OTHER half completes the infinitive combo Universality — most of the job's damage. Returns the base
    /// axe to press (the game substitutes the upgrade), or None when there is no finisher to take.
    /// <para>
    /// Each half has two upgraded skills and one of each pair rushes 25y at the target (Brutal Rage,
    /// Hawkish Talons). The other of each pair does the same 1,200 without moving the beastmaster, so
    /// those are the ones chosen: Calamity through Gale Axe, Risen Fall through Spinning Axe.
    /// </para>
    /// </summary>
    public static InstinctAffinity FinisherAxe(byte level, ArtemisGauge gauge)
    {
        if (level < UpgradeLevel || gauge.Tp < ArtemisGauge.MaxTp)
            return InstinctAffinity.None;

        return gauge.Affinity switch
        {
            InstinctAffinity.Sunstrider => InstinctAffinity.Volant,    // Gale Axe -> Calamity (Moonstalker)
            InstinctAffinity.Moonstalker => InstinctAffinity.Eldritch, // Spinning Axe -> Risen Fall (Sunstrider)
            _ => InstinctAffinity.None,
        };
    }

    /// <summary>
    /// The axe that answers what is lit on the Compass right now — the clockwise neighbour of the lit
    /// Heart, which is the game's own definition of an intentional combo. None when no Heart is lit.
    /// </summary>
    public static InstinctAffinity AnswerFromGauge(ArtemisGauge gauge)
        => gauge.HasHeart ? BSTActions.NextInCycle(gauge.Affinity) : InstinctAffinity.None;

    /// <summary>
    /// Has the Compass moved since Trick was sent? That is the familiar's skill landing, seen directly —
    /// what the action-effect hook and the 1.5s fallback were both standing in for.
    /// </summary>
    public static bool GaugeMovedSinceTrick(ArtemisGauge atTrick, ArtemisGauge now)
        => now.ChainCount != atTrick.ChainCount || now.Affinity != atTrick.Affinity;

    /// <summary>
    /// Rallying Cheer (Lv40): the same for the familiar's TP, which is what Trick spends. Trick-first pairing
    /// banks no Natural Instinct, so this is usually its flat 30 — still the difference between a Trick now
    /// and a Trick after several more of the beast's auto-attacks.
    /// </summary>
    public static bool ShouldRallyingCheer(bool enabled, bool ready, bool hasFamiliar, double trickStarvedSeconds)
        => enabled && ready && hasFamiliar && trickStarvedSeconds >= StarvedSeconds;

    /// <summary>
    /// Shield Charge (Lv24): a rush that hits the target and everything near it — real damage on a 60s charge.
    /// Only from inside melee range, where the rush goes nowhere: a dash across the room is how a character
    /// lands in an AoE, and closing distance is the movement code's job.
    /// </summary>
    public static bool ShouldShieldCharge(bool enabled, bool ready, float edgeDistanceYalms)
        => enabled && ready && edgeDistanceYalms <= MeleeYalms;

    /// <summary>
    /// Borrow (Lv22): takes the familiar's Kinship, which turns Beast Mode into that kin's action. It needs
    /// One with Nature, the same as Tempered Release, so it never goes ahead of a release that is still owed.
    /// </summary>
    public static bool ShouldBorrow(bool enabled, bool ready, bool hasFamiliar, bool releaseOwed)
        => enabled && ready && hasFamiliar && !releaseOwed;

    /// <summary>Snarl only while the familiar is healthy enough to take the hits.</summary>
    public const float SnarlMinFamiliarHpPct = 0.50f;

    /// <summary>Below this the familiar is about to go down: take the enemy back with Challenge.</summary>
    public const float ChallengeFamiliarHpPct = 0.30f;

    /// <summary>Challenge holds the enemy on the beastmaster for 30s; Snarl would undo it, so wait that long.</summary>
    public const float SnarlAfterChallengeSeconds = 30f;

    /// <summary>Challenge only while the beastmaster can stand being hit.</summary>
    public const float ChallengeMinSelfHpPct = 0.40f;

    /// <summary>
    /// Crucible: keep the familiar tanking. Snarl has the familiar Threaten (15s, the same as the recast) and cover
    /// the beastmaster (45s), so it goes out whenever it is ready — but not onto a familiar that is low, and not
    /// straight after a Challenge, which would hand back an enemy that was just pulled off it on purpose.
    /// </summary>
    public static bool ShouldSnarl(
        bool enabled, bool onBoardInBattle, bool ready, bool hasFamiliar, float familiarHpPct, float secondsSinceChallenge)
        => enabled && onBoardInBattle && ready && hasFamiliar
           && familiarHpPct >= SnarlMinFamiliarHpPct
           && secondsSinceChallenge >= SnarlAfterChallengeSeconds;

    /// <summary>
    /// Crucible: the familiar is about to go down while the enemy is on it — take the enemy with Challenge (which also
    /// ends the familiar's Cover). Not when the beastmaster is too low to take the hits instead.
    /// </summary>
    public static bool ShouldChallenge(
        bool enabled, bool onBoardInBattle, bool ready, bool hasFamiliar, float familiarHpPct,
        bool enemyOnFamiliar, float selfHpPct)
        => enabled && onBoardInBattle && ready && hasFamiliar
           && familiarHpPct < ChallengeFamiliarHpPct
           && enemyOnFamiliar
           && selfHpPct >= ChallengeMinSelfHpPct;

    /// <summary>
    /// The Beast Mode button, by what it has turned into. <paramref name="adjustedId"/> is what the game
    /// says the button currently is; while that is still Beast Mode itself there is no Kinship and nothing
    /// to press.
    /// </summary>
    public static BeastModeUse DecideBeastMode(
        bool enabled, uint adjustedId, float selfHpPct, float edgeDistanceYalms, bool selfHasCleansableDebuff)
    {
        if (!enabled)
            return BeastModeUse.None;

        return adjustedId switch
        {
            // +5% critical hit rate for 30s is damage; the 25% physical mitigation comes with it.
            BSTActions.BeastskinId => BeastModeUse.Self,

            // Pure defence: a full block for 15s, or a magic barrier. For when something is hitting us.
            BSTActions.VileskinId or BSTActions.ScaleskinId
                => selfHpPct < SkinHpPct ? BeastModeUse.Self : BeastModeUse.None,

            // -10% damage dealt and a DoT on everything within 6y.
            BSTActions.SeedsowerId
                => edgeDistanceYalms <= SeedsowerYalms ? BeastModeUse.Self : BeastModeUse.None,

            // 300 potency in melee, 1,000 and 100 TP if it interrupts. Free damage either way.
            BSTActions.SoulCrushId
                => edgeDistanceYalms <= MeleeYalms ? BeastModeUse.Enemy : BeastModeUse.None,

            // A cleanse that pays 10% HP and 100 TP — but only when it actually removes something.
            BSTActions.ScouringAshId
                => selfHasCleansableDebuff ? BeastModeUse.Self : BeastModeUse.None,

            // Cloud Skim is a dash in a chosen direction, and Quelling Wave is a 350-potency spell on the
            // GCD that loses to the axe combo in melee. Neither is pressed.
            _ => BeastModeUse.None,
        };
    }
}
