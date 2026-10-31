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
/// <summary>What Shield Charge is for this frame.</summary>
public enum ShieldChargeUse
{
    None,

    /// <summary>Rush to a target that is a run away.</summary>
    GapClose,

    /// <summary>In melee, with a charge to spare.</summary>
    Damage,
}

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
    /// <para>
    /// At level 50 with a familiar, Rally is the middle step of the infinitive combo, so it also waits for a lit
    /// half (the intentional combo that opens the 7 s window). In-game tutorial "Perform an Infinitive Combo"
    /// (2026-10-25): three diamonds → an intentional combo → Rally → the other affinity's skill within 7 s. Rallied
    /// before the half is lit, the 250-TP axe is spent LIGHTING the half and nothing is left to finish it.
    /// </para>
    /// <para>
    /// With no familiar but diamonds banked, the beast is usually only between swaps (a retreat, then the next one
    /// summoned), so Rally waits up to <see cref="RallyHoldForFamiliarSeconds"/> for it. Korha 2026-10-31: a beast left
    /// on two diamonds, Rally went straight out for 208 TP, and its two-minute recast cost the next finisher.
    /// </para>
    public static bool ShouldRallyFromGauge(
        bool enabled, bool ready, int tp, int masteredInstinct, byte level, bool hasFamiliar, bool halfLit = false,
        double secondsWithoutFamiliar = double.MaxValue)
        => enabled && ready && tp < ArtemisGauge.MinSpendTp
           && (hasFamiliar
               ? masteredInstinct >= RallyMinMasteredFor(level) && (level < UpgradeLevel || halfLit)
               : masteredInstinct == 0 || secondsWithoutFamiliar >= RallyHoldForFamiliarSeconds);

    /// <summary>How long banked diamonds keep Rally waiting for a familiar to come back before it takes the TP anyway.</summary>
    public const double RallyHoldForFamiliarSeconds = 20;

    /// <summary>
    /// The infinitive combo's window is open: level 50, half the Compass lit, and either the 250 TP already in hand or
    /// three diamonds and Rally ready to make it. From the intentional combo the finisher has 7 s, and the axes share
    /// a 5 s recast — so for those seconds nothing but Rally and the finisher goes out (no Trick, no other axe to
    /// disturb the Compass).
    /// </summary>
    public static bool InfinitiveWindowOpen(byte level, ArtemisGauge gauge, bool rallyReady)
        => level >= UpgradeLevel
           && gauge.HasHalf
           && (gauge.Tp >= ArtemisGauge.MaxTp || (gauge.MasteredInstinct >= MaxMastered && rallyReady));

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

    /// <summary>Beyond this edge distance the target is a run away, not a step: Shield Charge closes it.</summary>
    public const float ShieldChargeGapYalms = 5f;

    /// <summary>
    /// Shield Charge (Lv24): a rush that hits the target and everything near it (300 at Lv38+), 3 charges from Lv36.
    /// <list type="bullet">
    /// <item><b>Gap close</b> — target 5-20y off and the dash is safe (the shared gap-closer guard: no dodge in
    /// progress, ground and landing safe, your own target, not moving away). Field 2026-10-25: every multi-second GCD
    /// gap in a Board 1 clear was the beastmaster running across the arena after spread adds.</item>
    /// <item><b>Damage</b> — in melee, only while it leaves a charge banked for the next gap (so never below Lv36,
    /// where it has one charge).</item>
    /// </list>
    /// </summary>
    public static ShieldChargeUse DecideShieldCharge(
        bool enabled, bool gapCloseEnabled, bool ready, int charges, float edgeDistanceYalms, float rangeYalms, bool dashSafe)
    {
        if (!enabled || !ready || charges <= 0)
            return ShieldChargeUse.None;

        if (edgeDistanceYalms >= ShieldChargeGapYalms)
            return gapCloseEnabled && dashSafe && edgeDistanceYalms <= rangeYalms ? ShieldChargeUse.GapClose : ShieldChargeUse.None;

        return edgeDistanceYalms <= MeleeYalms && charges >= 2 ? ShieldChargeUse.Damage : ShieldChargeUse.None;
    }

    /// <summary>
    /// Borrow (Lv22): takes the familiar's Kinship, which turns Beast Mode into that kin's action. It needs
    /// One with Nature, the same as Tempered Release, so it never goes ahead of a release that is still owed.
    /// </summary>
    public static bool ShouldBorrow(bool enabled, bool ready, bool hasFamiliar, bool releaseOwed)
        => enabled && ready && hasFamiliar && !releaseOwed;

    // Who tanks in the Crucible. HP does NOT refill between a board's battles — for the beastmaster or the familiars.
    // The beastmaster's death ends the run; a familiar's costs one beast of a 10-15 roster, and only the first horn
    // fights until it falls. So the familiar tanks and the beastmaster stays healthy:
    //  - 2026-10-22 (3-beast team): Snarl on cooldown wore Damselfly down to 112/535 by the second battle. With three
    //    beasts each one mattered, so the rule became "the beastmaster tanks, Snarl only in an emergency".
    //  - 2026-10-24 (12-beast team): that rule killed the run twice at the Voidmancer — Challenge kept pulling the
    //    Voidmancer, four Zombie Pieces and the Maladies onto the beastmaster, who died with a full-HP Rotting Goobbue
    //    still out. So Challenge now only rescues a familiar about to fall, and only while the beastmaster is healthy.
    //  - 2026-10-25: with Snarl held for "beastmaster under 50%", enemies still went for the beastmaster by default:
    //    1,733 max HP on board 2, 100 → 55% in the first fight with every familiar untouched, dead in the fourth. With
    //    Snarl up the Voidmancer fight cost the beastmaster 7%. Snarl's recast (15s) matches its Threaten, and its
    //    Cover takes ALL damage meant for the beastmaster for 45s — so it goes on cooldown while the familiar can soak.
    //    Twelve familiars hold ~9,600 HP; the beastmaster's 1,733 must last the whole board.

    /// <summary>A familiar under this much HP on a board is about to fall: spend it before it does.</summary>
    public const float FamiliarFallingHpPct = 0.20f;

    /// <summary>
    /// Crucible: the familiar is about to fall — its Tempered Release, then Parting Blow, before it does. A fallen
    /// familiar takes its unspent release with it, and Parting Blow (1,000 potency) RETREATS the familiar rather
    /// than letting it be knocked out, so it keeps its last HP for later. Field 2026-10-25: familiars tanked until
    /// they died with nothing spent. 0 HP is already fallen.
    /// </summary>
    /// <para>
    /// Not while the beastmaster is critical: then the familiar's Cover is all that stands between them and the
    /// hits, and a retreat would leave them uncovered until the next horn is out and Snarled — so it tanks to the end.
    /// </para>
    /// <para>Not with a tankbuster on its way either: the familiar is kept to take it.</para>
    /// <para><paramref name="fallingLine"/> is <see cref="FallingLine"/>: the flat line, raised for a familiar being hit hard.</para>
    /// <para>A raidwide Cover can take (<paramref name="secondsUntilRaidwide"/>) keeps it too, like a tankbuster.</para>
    /// <para>Not when no other horn is ready (<paramref name="replacementReady"/>): the retreat would leave the
    /// beastmaster alone until a horn comes back, so the familiar stays and soaks.</para>
    public static bool IsFamiliarFalling(
        bool enabled, bool onBoard, float familiarHpPct, float selfHpPct = 1f, float secondsUntilTankbuster = float.MaxValue,
        float fallingLine = FamiliarFallingHpPct, float secondsUntilRaidwide = float.MaxValue, bool replacementReady = true)
        => enabled && onBoard && familiarHpPct > 0f && familiarHpPct < fallingLine
           && replacementReady
           && selfHpPct > SelfCriticalHpPct
           && secondsUntilTankbuster > TankbusterLeadSeconds
           && secondsUntilRaidwide > RaidwideLeadSeconds;

    /// <summary>
    /// A raidwide due within this many seconds is Snarled onto any standing familiar: Cover takes the beastmaster's
    /// share (the familiar still takes its own), and the beastmaster's HP is the run. Same lead as a tankbuster.
    /// </summary>
    public const float RaidwideLeadSeconds = 10f;

    /// <summary>
    /// Raidwides Cover does not take, by action id: Snarling for them only spends the familiar. Icy Veins (2026-09):
    /// Durga's Atomic Ray on the Second Master's Board, "pet cover will NOT take it". Knockback raidwides are not here:
    /// their damage is covered, only the push is not.
    /// </summary>
    public static readonly System.Collections.Generic.IReadOnlySet<uint> UncoverableRaidwides =
        new System.Collections.Generic.HashSet<uint> { 49272 };

    /// <summary>
    /// Seconds until the next raidwide that Cover can take: <paramref name="seconds"/>, or <see cref="float.MaxValue"/>
    /// when the boss engine names an uncoverable one (<paramref name="actionId"/> 0 = not named, read as coverable).
    /// </summary>
    public static float CoverableRaidwideSeconds(float seconds, uint actionId)
        => UncoverableRaidwides.Contains(actionId) ? float.MaxValue : seconds;

    /// <summary>Retreat with room for this many of the biggest recent hits still to land.</summary>
    public const float FallingHitMargin = 1.5f;

    /// <summary>Retreat with this many seconds of HP left at the current loss rate (a weave slot plus the order).</summary>
    public const float FallingLeadSeconds = 4f;

    /// <summary>The falling line is never raised past this; above it the familiar tanks on regardless.</summary>
    public const float FallingLineMax = 0.5f;

    /// <summary>
    /// Where "falling" starts for the familiar out now: <see cref="FamiliarFallingHpPct"/>, raised to
    /// <see cref="FallingHitMargin"/> of its biggest recent hit and to <see cref="FallingLeadSeconds"/> of HP at the
    /// rate it is losing it, capped at <see cref="FallingLineMax"/>. Field 2026-10-25 (Minerva session, Saar vs
    /// Lakhamu Piece and its Golem Pieces): two of three familiars went from over 20% to 0 in one hit, the third
    /// from 16% to 0 in 3 s.
    /// </summary>
    public static float FallingLine(float biggestRecentHitPct, float lossPerSecond)
        => System.Math.Min(FallingLineMax, System.Math.Max(FamiliarFallingHpPct,
            System.Math.Max(biggestRecentHitPct * FallingHitMargin, lossPerSecond * FallingLeadSeconds)));

    /// <summary>
    /// A tankbuster due within this many seconds is Snarled onto any standing familiar. Minerva warns 4.5-8.7 s ahead
    /// in the Crucible (Crushing Blade, the Elder's 100-tonze Slash locked on the beastmaster), and Snarl's Cover
    /// takes ALL the beastmaster's damage.
    /// </summary>
    public const float TankbusterLeadSeconds = 10f;

    /// <summary>Parting Blow (and Wespe's Final Sting, the same trade) when chosen, or when the familiar is falling.</summary>
    public static bool AllowsPartingBlow(bool enabled, bool familiarFalling) => enabled || familiarFalling;

    /// <summary>Challenge only to save a familiar this low…</summary>
    public const float ChallengeFamiliarBelowHpPct = 0.25f;

    /// <summary>…and only while the beastmaster is this healthy.</summary>
    public const float ChallengeMinSelfHpPct = 0.70f;

    /// <summary>
    /// Snarl onto a familiar with at least this much left — the same line as "falling", so a familiar is always
    /// either Snarled or spent. Field 2026-10-25 (Minerva session, Saar board 2): at 0.25 a familiar on 20-25% was
    /// neither Snarled nor retreated, nothing covered the beastmaster, and a tankbuster (the Elder's 100-tonze Slash,
    /// locked on the beastmaster) killed her.
    /// </summary>
    public const float SnarlMinFamiliarHpPct = FamiliarFallingHpPct;

    /// <summary>
    /// At or below this the beastmaster is critical: Snarl onto any familiar still standing — if one of you has to
    /// fall it is the familiar (RotationSolver's BST_Reborn: "If one of you has to die, it will be the familiar").
    /// </summary>
    public const float SelfCriticalHpPct = 0.30f;

    /// <summary>
    /// Crucible: the enemy has turned onto a familiar that is about to fall — take it back with Challenge (top of its
    /// enmity for 30s; also ends the familiar's Cover). Not otherwise: the familiar is the tank, and the beastmaster's
    /// HP is the run.
    /// </summary>
    public static bool ShouldChallenge(
        bool enabled, bool onBoardInBattle, bool ready, bool hasFamiliar, bool enemyOnFamiliar, float familiarHpPct, float selfHpPct)
        => enabled && onBoardInBattle && ready && hasFamiliar
           && enemyOnFamiliar
           && familiarHpPct < ChallengeFamiliarBelowHpPct
           && selfHpPct >= ChallengeMinSelfHpPct;

    /// <summary>
    /// Crucible: whenever it is ready and the familiar can still soak, Snarl — the familiar Threatens (15s) and covers
    /// the beastmaster (takes all their damage, 45s). The beastmaster's HP does not refill between battles.
    /// </summary>
    public static bool ShouldSnarl(
        bool enabled, bool onBoardInBattle, bool ready, bool hasFamiliar, float familiarHpPct, float selfHpPct = 1f,
        float secondsUntilTankbuster = float.MaxValue, float fallingLine = SnarlMinFamiliarHpPct,
        float secondsUntilRaidwide = float.MaxValue)
        => enabled && onBoardInBattle && ready && hasFamiliar
           && (familiarHpPct >= fallingLine
               || (familiarHpPct > 0f && (selfHpPct <= SelfCriticalHpPct
                                          || secondsUntilTankbuster <= TankbusterLeadSeconds
                                          || secondsUntilRaidwide <= RaidwideLeadSeconds)));

    /// <summary>
    /// Legendary with the beastmaster tanking: Challenge whenever the enemy is not on the beastmaster (the pull included),
    /// so no familiar takes a hit and Selfless holds. Not while the beastmaster is critical: then Snarl's Cover saves the
    /// run, and Challenge would end it.
    /// </summary>
    public static bool ShouldChallengeToTank(bool enabled, bool onBoardInBattle, bool ready, bool enemyOnBeastmaster, float selfHpPct)
        => enabled && onBoardInBattle && ready && !enemyOnBeastmaster && selfHpPct > SelfCriticalHpPct;

    /// <summary>Third Board's Siren Piece, her Crawling Piece add (BNpcName rows) and her Summon cast (2.7s).</summary>
    public const uint SirenNameId = 14583, CrawlingPieceNameId = 14586, SirenSummonActionId = 48567;

    /// <summary>
    /// The Siren on Legendary (user 2026-10-30): the familiar holds her from the moment she casts Summon until every
    /// Crawling Piece is dead; the beastmaster holds her the rest of the time. A crawler goes for whoever holds the Siren
    /// and binds on contact — Bind and Damage Down, which cost Unbeastable on the beastmaster (Minerva replay, Korha
    /// 2026-10-30: with Diremite holding her the first crawler went for Diremite; with no familiar out the second went
    /// for Korha).
    /// </summary>
    public static bool SirenFamiliarHolds(bool summonCasting, bool crawlerAlive) => summonCasting || crawlerAlive;

    /// <summary>
    /// Lingering Vantage, from Tempered Release: "Next Parting Blow will deal increased damage" (1,000 → 1,500). On the
    /// beastmaster (Minerva replay 2026-10-31, STA+ 4614 on Korha after the release).
    /// </summary>
    public const uint LingeringVantageStatusId = 4614;

    /// <summary>The Siren's Shambling Piece adds (Summon brings three, with one Crawling Piece).</summary>
    public const uint ShamblingPieceNameId = 14585;

    /// <summary>Zu piece (Third Board elite).</summary>
    public const uint ZuNameId = 14572;

    /// <summary>At or below this the Zu is close enough to dead for a release to finish it.</summary>
    public const float ZuReleaseAtHpPct = 0.15f;

    /// <summary>
    /// Hold Tempered Release at the Zu: every egg it breaks makes the Zu stronger, and a release can be an AoE (Ponzi's
    /// Third Board guide: "not going to use behemoth meteor unless it's about to kill the zoo"). Parting Blow waits on an
    /// owed release, so it is held too. Not when the familiar is falling — its retreat comes first.
    /// </summary>
    public static bool HoldReleaseAtZu(bool onBoard, uint targetNameId, float targetHpPct, bool familiarFalling)
        => onBoard && targetNameId == ZuNameId && targetHpPct > ZuReleaseAtHpPct && !familiarFalling;

    /// <summary>Shared tanking: the beastmaster takes the enemy only while at or above this much HP…</summary>
    public const float SharedTankSelfFloorPct = 0.50f;

    /// <summary>…and only off a familiar this low: one that is about to fall.</summary>
    public const float SharedTakeOverFamiliarBelowPct = 0.30f;

    /// <summary>
    /// Shared tanking (Legendary, user 2026-10-27): the FAMILIAR holds the enemy by default — its Cover takes what lands
    /// on the beastmaster, Damage Down / Vulnerability Up included, and those cost Unbeastable (1,250) the moment the
    /// beastmaster wears one (user: "the vuln stacks need a pet to tank them"; field 2026-10-27: Korha's debuffs came
    /// from Death Throes, Burst, Dark Orb, Arm of Purgatory, Buffet…). The beastmaster takes over only to save a familiar
    /// about to fall (below <see cref="SharedTakeOverFamiliarBelowPct"/>, so No Beast Left Behind holds) while at or above
    /// <see cref="SharedTankSelfFloorPct"/>, or when no familiar is standing (the only choice). Never critical.
    /// </summary>
    public static bool BeastmasterHoldsShared(float selfHpPct, bool familiarOut, float familiarHpPct)
        => selfHpPct > SelfCriticalHpPct
           && (!familiarOut || familiarHpPct <= 0f
               || (selfHpPct >= SharedTankSelfFloorPct && familiarHpPct < SharedTakeOverFamiliarBelowPct));

    /// <summary>
    /// Shared tanking while the beastmaster holds: Snarl (Cover moves the beastmaster's damage onto the familiar) only
    /// for a tankbuster or raidwide on its way, onto a familiar above the falling line. Critical never applies here:
    /// <see cref="BeastmasterHoldsShared"/> hands the enemy back first.
    /// </summary>
    public static bool ShouldSnarlWhileHoldingShared(
        bool enabled, bool ready, bool hasFamiliar, float familiarHpPct, float fallingLine,
        float secondsUntilTankbuster, float secondsUntilRaidwide)
        => enabled && ready && hasFamiliar && familiarHpPct >= fallingLine
           && (secondsUntilTankbuster <= TankbusterLeadSeconds || secondsUntilRaidwide <= RaidwideLeadSeconds);

    /// <summary>Legendary with the beastmaster tanking: Snarl only to save a critical beastmaster (Selfless is spent then).</summary>
    public static bool ShouldSnarlWhileTanking(
        bool enabled, bool onBoardInBattle, bool ready, bool hasFamiliar, float familiarHpPct, float selfHpPct)
        => enabled && onBoardInBattle && ready && hasFamiliar && familiarHpPct > 0f && selfHpPct <= SelfCriticalHpPct;

    /// <summary>Why Snarl is not going out this frame, for the debug log; null when it is (or would be, once ready).</summary>
    /// <para>Takes the same tankbuster / raidwide warnings as <see cref="ShouldSnarl"/>, so the log never says "held" while
    /// Snarl goes out for one (found by the cloud cleanup pass, 2026-10-27).</para>
    public static string? SnarlHeldReason(
        bool enabled, bool hasFamiliar, float familiarHpPct, float selfHpPct, float fallingLine = SnarlMinFamiliarHpPct,
        float secondsUntilTankbuster = float.MaxValue, float secondsUntilRaidwide = float.MaxValue)
    {
        if (!enabled)
            return "Snarl is turned off";
        if (!hasFamiliar)
            return "no familiar out";
        if (familiarHpPct >= fallingLine
            || (familiarHpPct > 0f && (selfHpPct <= SelfCriticalHpPct
                                       || secondsUntilTankbuster <= TankbusterLeadSeconds
                                       || secondsUntilRaidwide <= RaidwideLeadSeconds)))
            return null;
        return $"familiar at {familiarHpPct:P0} (under {fallingLine:P0}), you at {selfHpPct:P0}";
    }

    /// <summary>
    /// The enemy for Soul Crush (the Soulkin Kinship action, which interrupts) to stop: the nearest of the boss
    /// engine's interrupt targets that is still casting and within melee. 0 when none is.
    /// </summary>
    public static ulong PickInterruptTarget(
        System.Collections.Generic.IEnumerable<(ulong Id, float EdgeDistance, bool Casting)> candidates)
    {
        ulong best = 0;
        var bestDistance = float.MaxValue;
        foreach (var (id, distance, casting) in candidates)
        {
            if (casting && distance <= MeleeYalms && distance < bestDistance)
            {
                best = id;
                bestDistance = distance;
            }
        }

        return best;
    }

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
