using System;
using System.Collections.Generic;
using System.Linq;
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

    /// <summary>The Second Board's final boss: Loosefrox, the worm Chewchum, and the small bombs (BNpcBase ids).</summary>
    public const uint LoosefroxBaseId = 0x4C65, ChewchumBaseId = 0x4C66, SmallBombBaseId = 0x4C69;

    /// <summary>Loosefrox's Gobbieboom Barrage that drops the small bombs (3 s cast; the bombs appear ~4 s after it starts).</summary>
    public const uint SmallBombBarrageCastId = 50446;

    /// <summary>The hold keeps going this long after the Barrage cast ends, bridging the second before the bombs appear.</summary>
    public const double BarrageBridgeSeconds = 3;

    /// <summary>
    /// The bomb push (user, 2026-11-07; TPLemontFFXIV's Second Board guide; Minerva builds the movement). A small bomb
    /// knocked into the quicksand where Chewchum surfaces stuns it and leaves it open to damage. Every damaging hit
    /// rolls a bomb 4 y straight away from whoever hit it, and the familiar follows whatever the beastmaster hits, so
    /// from the Barrage cast (before the bombs exist, so Parting Blow can't scatter them) until no small bomb is
    /// targetable or Minerva puts Chewchum first (a bomb was caught): the familiar is sent away and not called back, and
    /// only the 1-2-3 combo goes out — the axes, Trick, Tempered Release, the cooldowns and the duty actions wait, the
    /// chain saved for the worm. Which enemy the combo hits is Minerva's: a bomb first only while the beastmaster is
    /// lined up, the bombs forbidden otherwise. Only while Minerva (connected) runs this fight's module, which carries the
    /// push; under BossMod, or with no engine, the fight plays as before.
    /// </summary>
    public static bool HoldsForBombPush(
        bool onBoard, string fightModule, double secondsSinceBarrageCast, bool smallBombsUp, bool wormFirst)
        => onBoard && fightModule == MinervaLoosefroxModule && !wormFirst
           && (smallBombsUp || secondsSinceBarrageCast <= BarrageBridgeSeconds);

    /// <summary>Minerva's module for this fight, by type name (the port's own spelling).</summary>
    public const string MinervaLoosefroxModule = "LooserfroxInkyjots";

    /// <summary>
    /// A link held for its partner (the axe for the Trick, or the Trick for the axe) goes out anyway once the live
    /// chain has this little left of its 7 s, rather than let it lapse. Field guides chain two of your own skills in a
    /// row routinely (TPLemontFFXIV: "your blue → Rally → your yellow"); holding the axe up to
    /// <see cref="ArtemisTrickPairing.MaxHoldForTrickSeconds"/> dropped the chain instead. Pressing late rather than at
    /// once is the guides' "wait until the last moment": the familiar may still reach its Trick meanwhile.
    /// </summary>
    public const float ChainSaveSeconds = 2f;

    /// <summary>The live chain is about to lapse: a held link should go out now.</summary>
    public static bool SavesChain(bool chainLive, float secondsLeft) => chainLive && secondsLeft <= ChainSaveSeconds;

    /// <summary>
    /// The colour of your link that keeps a live chain going. With a Heart lit (the last link was instinctual) its
    /// clockwise neighbour completes an intentional combo. With none (the last link was intentional, or unknown) the
    /// next link is instinctual whatever its colour, so take the one just before the familiar's Trick colour: the
    /// Trick after it is then intentional. Otherwise any ready axe. None when no axe is ready.
    /// </summary>
    public static InstinctAffinity ExtensionAffinity(
        InstinctAffinity litHeart, InstinctAffinity petTrick, Func<InstinctAffinity, bool> ready)
    {
        var preferred = IsHeart(litHeart) ? BSTActions.NextInCycle(litHeart) : BSTActions.PreviousInCycle(petTrick);
        if (preferred != InstinctAffinity.None && ready(preferred))
            return preferred;

        foreach (var affinity in HeartOrder)
            if (ready(affinity))
                return affinity;
        return InstinctAffinity.None;
    }

    /// <summary>
    /// The familiar's Trick would complete an intentional combo right now: the lit Heart is the one just before its
    /// colour. Then it goes at once, without waiting for an axe to pair with — it is a link of the chain in its own
    /// right, and the familiar finishing a combo banks Natural Instinct for Rallying Cheer.
    /// </summary>
    public static bool TrickWouldBeIntentional(InstinctAffinity litHeart, InstinctAffinity petTrick)
        => IsHeart(litHeart) && petTrick != InstinctAffinity.None && BSTActions.NextInCycle(litHeart) == petTrick;

    /// <summary>
    /// The level 50 infinity line (TPLemontFFXIV, every board guide): with two of your diamonds and a pet charge,
    /// answer the familiar's Trick with the colour just BEFORE it (instinctual — your third diamond), Rallying Cheer,
    /// the same Trick again (now intentional — it lights the half), Rally, then the other half's skill. One more link
    /// than Trick → clockwise axe → Rally, so the finisher lands on a longer chain. Needs Rally and Rallying Cheer
    /// both ready, and the pet charge, which is what makes Cheer's refill reach a Trick (30 + 70).
    /// </summary>
    public static bool UsesInfinityLine(byte level, ArtemisGauge gauge, bool rallyReady, bool cheerReady)
        => level >= UpgradeLevel && gauge.MasteredInstinct == MaxMastered - 1 && gauge.NaturalInstinct >= 1
           && rallyReady && cheerReady;

    /// <summary>
    /// On the infinity line Rallying Cheer is kept for its one moment — after your answer, to pay for the second Trick
    /// — rather than spent the first time the familiar's TP dips.
    /// </summary>
    public static bool HoldsCheerForInfinityLine(bool lineActive, InstinctAffinity litHeart, InstinctAffinity petTrick)
        => lineActive && !TrickWouldBeIntentional(litHeart, petTrick);

    private static bool IsHeart(InstinctAffinity a)
        => a is InstinctAffinity.Volant or InstinctAffinity.Rampant or InstinctAffinity.Durant or InstinctAffinity.Eldritch;

    private static readonly InstinctAffinity[] HeartOrder =
        [InstinctAffinity.Volant, InstinctAffinity.Rampant, InstinctAffinity.Durant, InstinctAffinity.Eldritch];

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
    /// The falling line's floor on a degree above Standard. Korha 2026-11-05, Third Board on Third Degree, the
    /// Cavalier Piece: Chimera went from 17% to 0 in 3 s and Drake from 18% to 0 in 2 s, both after Parting Blow was
    /// sent at the 20% line — the blow had not landed and retreated them before they fell.
    /// </summary>
    public const float DegreeFallingHpPct = 0.35f;

    /// <summary>The falling line's floor for the board's degree (0 Standard, 1-3 First-Third Degree).</summary>
    public static float FallingFloor(int degree) => degree > 0 ? DegreeFallingHpPct : FamiliarFallingHpPct;

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
    public static float FallingLine(float biggestRecentHitPct, float lossPerSecond, float floor = FamiliarFallingHpPct)
        => System.Math.Min(FallingLineMax, System.Math.Max(floor,
            System.Math.Max(biggestRecentHitPct * FallingHitMargin, lossPerSecond * FallingLeadSeconds)));

    /// <summary>
    /// A tankbuster due within this many seconds is Snarled onto any standing familiar. Minerva warns 4.5-8.7 s ahead
    /// in the Crucible (Crushing Blade, the Elder's 100-tonze Slash locked on the beastmaster), and Snarl's Cover
    /// takes ALL the beastmaster's damage.
    /// </summary>
    public const float TankbusterLeadSeconds = 10f;

    /// <summary>
    /// Parting Blow (and Wespe's Final Sting, the same trade) when chosen, or when the familiar is falling. The setting
    /// counts off a board only: on one it fired the Blow on every cooldown and cycled the horns every 10 s, and let the
    /// Wespe sting in every fight (roommate, 2026-11-02). The board's own Blows (falling, the Wespe cycle, bunched adds,
    /// casts that kill the familiar) decide there.
    /// </summary>
    public static bool AllowsPartingBlow(bool enabled, bool familiarFalling, bool onBoard = false)
        => (enabled && !onBoard) || familiarFalling;

    /// <summary>
    /// Whether this fight gets the Wespe's Final Sting (and the cycle that brings the Wespe out). On the First Board only
    /// the Ogre and the final boss do; its other fights play normally (user, 2026-11-01). Other boards: every fight. On
    /// a degree above Standard every fight does: the documented clears lead every fight with the Wespe and sting.
    /// </summary>
    public static bool WespeStingFight(int boardIndex, bool ogreFight, bool finalBoss, int degree = 0)
        => degree > 0 || boardIndex != Daedalus.Data.CrucibleAutoPilotRules.FirstBoardIndex || ogreFight || finalBoss;

    /// <summary>
    /// On a degree above Standard Final Sting goes as soon as the Wespe is out, not at the 30% kill line: the documented
    /// clears sting at once and bring the Wespe back whenever its horn returns (fuurie's D3 clears, BMRV's plans).
    /// </summary>
    public static bool StingsAtOnce(int degree) => degree > 0;

    /// <summary>
    /// In the Bone Knight fight a Wespe whose horn has come back is brought out again only once his Forward Guard has
    /// been seen: its second sting is for the Knight (fuurie: "guard drops, chain, Parting Blow, Wespe sting"). Every
    /// other fight: whenever the Wespe is waiting.
    /// </summary>
    public static bool CyclesToWespeInKnightFight(bool knightFight, bool guardSeen) => !knightFight || guardSeen;

    /// <summary>
    /// A falling familiar whose retreat (Parting Blow) is on cooldown is switched out with another horn instead, before
    /// it is knocked out. <paramref name="familiarFalling"/> already requires a replacement horn to be ready.
    /// </summary>
    public static bool ShouldSwapOutFalling(bool familiarFalling, bool partingBlowReady) => familiarFalling && !partingBlowReady;

    /// <summary>
    /// On a board, Parting Blow the familiar out (its Tempered Release spent — the caller waits for that) to bring the
    /// next horn, until the Wespe is out: the guides' Release, Parting, next (Meoni, Ponzi's Worm → Mantis → Wespe).
    /// Without it a familiar that never falls stays the whole fight and the Wespe never comes out to sting (user,
    /// 2026-11-01). Never with no other horn ready, and never with a raidwide, tankbuster or wisp explosion due.
    /// </summary>
    public static bool CyclesToWespe(
        bool onBoard, bool wespeWaiting, bool wespeOut, bool replacementReady, float secondsUntilRaidwide,
        float secondsUntilTankbuster)
        => onBoard && wespeWaiting && !wespeOut && replacementReady
           && secondsUntilRaidwide > RaidwideLeadSeconds && secondsUntilTankbuster > TankbusterLeadSeconds;

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
    /// First Master's Board's Morbol Piece and its Seedling Pieces (BNpcName rows). Seedlings come in waves (4 at ~6 s,
    /// 6 at ~58 s, 6 at ~126 s; Minerva replays 2026-10-31), 1,294 HP each, and every hit of theirs stacks Poison (5141)
    /// that outlives them — it killed the familiar ~20 s after the last of the first wave died.
    /// </summary>
    public const uint MorbolPieceNameId = 14599, SeedlingPieceNameId = 14600;

    /// <summary>
    /// First Master's Board's Treant Piece and its Slug Pieces (BNpcName rows). Six slugs per wave (~18.6 s and
    /// ~140 s), spread over ~20 yalms; they hold ~5 s, then crawl in and are within a Fang's reach by ~+7 s. Left
    /// alone they lived 30+ s and threw up to 79 autos (Minerva replays 2026-10-31/11-01, 12 fights).
    /// </summary>
    public const uint TreantPieceNameId = 14618, SlugPieceNameId = 14619;

    /// <summary>
    /// A wisp reaches the Ogre and blows up (Scorched Earth, the whole arena, a Vulnerability Up on whoever it hits) this
    /// long after it appears, wherever it starts: 19.0-19.3 s (Minerva's OgrePiece notes; replays 2026-10-27/28: Great
    /// Wisps at 56.7/72.7 s gone at 76.1/92.1 s).
    /// </summary>
    public const double WispWalkSeconds = 19.0;

    /// <summary>The familiar's Cover on the beastmaster (from Snarl): it takes their damage, and their debuffs with it.</summary>
    public const uint CoveredStatusId = 2413;

    /// <summary>Seconds until the first of these wisps (seconds since each appeared) blows up; MaxValue with none.</summary>
    public static float WispArrivalSeconds(IEnumerable<double> secondsSinceAppeared)
    {
        var soonest = float.MaxValue;
        foreach (var age in secondsSinceAppeared)
            soonest = System.Math.Min(soonest, (float)System.Math.Max(0.0, WispWalkSeconds - age));
        return soonest;
    }

    /// <summary>
    /// Snarl so the familiar's Cover is up when a wisp blows up: its Vulnerability Up then lands on the familiar, not the
    /// beastmaster (17 Ogre fights: covered, 14 of 15 stacks went to the familiar; uncovered, 13 of 14 to the
    /// beastmaster — and one on the beastmaster costs Unbeastable). Only within the raidwide lead, and only when the
    /// Cover already up would not last until it lands.
    /// </summary>
    public static bool ShouldSnarlForWisp(
        bool enabled, bool ready, bool familiarOut, float familiarHpPct, float wispIn, float coverLeft)
        => enabled && ready && familiarOut && familiarHpPct > 0f
           && wispIn <= RaidwideLeadSeconds && coverLeft < wispIn + 1f;

    /// <summary>
    /// First Board's Bone Knight (BNpcName), his Forward Guard cast and the Directional Parry it puts on him: it blocks
    /// everything from the front and faces whoever holds him, but not the familiar's damage (Minerva's BoneBishop module).
    /// </summary>
    public const uint BoneKnightNameId = 14531, ForwardGuardCastId = 46864, ForwardGuardStatusId = 680;

    /// <summary>
    /// The Bone Knight's guard is up: Snarl at him unless he is already on the familiar, so he turns to it and the
    /// beastmaster can hit his back.
    /// </summary>
    public static bool ShouldSnarlBoneKnight(bool enabled, bool ready, bool familiarOut, float familiarHpPct, bool knightOnFamiliar)
        => enabled && ready && familiarOut && familiarHpPct > 0f && !knightOnFamiliar;

    /// <summary>First Board's final boss Pas de Seul and the succubus mage / knight it brings (BNpcName rows).</summary>
    public const uint PasDeSeulNameId = 14541, SuccubusMageNameId = 14542, SuccubusKnightNameId = 14543;

    /// <summary>
    /// Pas de Seul on a degree: from the pull until her succubi have come and died (they come once, about 49 s in), the
    /// beast out is kept as one a later battle needs — the beastmaster takes every hit, the tankbuster included (user,
    /// 2026-11-15, after fuurie's clear: "the player takes every hit" so the Behemoth's Meteor is there for them).
    /// Korha, that day: the Behemoth tanked, took the first Cold Caress (369 of 667), was retreated at 28% and its
    /// Meteor went on Pas de Seul alone 8 s before the succubi came; no beast was out for 29 s and then 23 s, and she
    /// died with Pas de Seul at 30%.
    /// </summary>
    public static bool KeepsBeastForSuccubi(int degree, bool pasDeSeulFight, bool succubiSeen, int succubiUp)
        => degree > 0 && pasDeSeulFight && !(succubiSeen && succubiUp == 0);

    /// <summary>
    /// A tankbuster cast at the beastmaster is hers to answer with an item (a Tome of the Impervious, a Crucible
    /// Tannin) only when nothing else takes it: no Cover on her, and no beast out that can be spent, which is Snarled
    /// into the hit. Korha, 2026-11-15 18:22: the Banemite's first Deadly Thrust was cast at her while the Ice Golem's
    /// Cover was up; the Golem took the 450 and her Tannin was spent for nothing — it had worn off 15 s before the
    /// second Thrust, which was hers (466).
    /// </summary>
    public static bool BusterIsTheBeastmasters(bool castAtBeastmaster, float coverLeft, bool beastOut, bool beastOutIsKept)
        => castAtBeastmaster && coverLeft <= 0f && (!beastOut || beastOutIsKept);

    /// <summary>
    /// Hold the Behemoth's Meteor for the succubi: until they have come, then until they have closed in
    /// (<see cref="MitelingsBunchedYalms"/>) or <see cref="MeteorWaitsForMitelingsSeconds"/> has passed. Not when the
    /// familiar is falling (its retreat comes first).
    /// </summary>
    public static bool HoldMeteorForSuccubi(
        int degree, int? bestiaryNo, bool pasDeSeulFight, bool succubiSeen, int succubiUp, bool succubiBunched,
        double secondsSinceSuccubiUp, bool familiarFalling)
    {
        if (degree <= 0 || !pasDeSeulFight || bestiaryNo != BehemothBestiaryNo || familiarFalling)
            return false;
        if (succubiUp > 0)
            return !succubiBunched && secondsSinceSuccubiUp < MeteorWaitsForMitelingsSeconds;
        return !succubiSeen;
    }

    /// <summary>First Board's Ogre Piece and its Wisp / Great Wisp Pieces (BNpcName rows).</summary>
    public const uint OgrePieceNameId = 14538, WispPieceNameId = 14539, GreatWispPieceNameId = 14748;

    /// <summary>
    /// The Ogre's Burning Ward (46918) begins 52-55 s into every recorded fight, whatever its HP (21-79%; Minerva replays
    /// 2026-10-22..28): the Ogre goes untargetable and the wisps march. Final Sting goes in the window before it (Meoni:
    /// "fire it before a dangerous phase (Ogre's ward)") rather than waiting for 30% — the ward is where runs end.
    /// </summary>
    public const double OgreWardAtSeconds = 52, StingBeforeWardSeconds = 8;

    /// <summary>Whether Final Sting should go now in the Ogre fight: in the seconds before Burning Ward.</summary>
    /// <summary>The Mantis (Bestiary 16): its Eerie Soundwave puts +10% physical vulnerability on the enemy for 30 s.</summary>
    public const int MantisBestiaryNo = 16;

    /// <summary>
    /// The Mantis lets its Soundwave go at this target HP, a little above the sting's 30%, so the swap to the Wespe (the
    /// Soundwave, Parting Blow, the horn) is done as the sting comes due.
    /// </summary>
    public const float MantisReleaseAtTargetHpPct = 0.35f;

    /// <summary>Against the Ogre the Mantis lets go this long before the ward, so the Wespe is out for the sting window.</summary>
    public const double MantisBeforeWardSeconds = 14;

    /// <summary>
    /// Whether the Mantis may use its Tempered Release now. In a sting fight with the Wespe still waiting it holds the
    /// Soundwave until just before the swap, so Final Sting lands inside the 30 s vulnerability (user, 2026-11-01): the
    /// target at <see cref="MantisReleaseAtTargetHpPct"/> or below, or the Ogre's ward
    /// <see cref="MantisBeforeWardSeconds"/> away. A falling Mantis lets go at once rather than lose it.
    /// </summary>
    public static bool MantisReleasesNow(
        bool stingFight, bool wespeWaiting, bool falling, float targetHpPct, bool ogreFight, double secondsIn)
        => !stingFight || !wespeWaiting || falling
           || (targetHpPct > 0f && targetHpPct <= MantisReleaseAtTargetHpPct)
           || (ogreFight && secondsIn >= OgreWardAtSeconds - MantisBeforeWardSeconds && secondsIn < OgreWardAtSeconds);

    public static bool StingBeforeOgreWard(bool ogreFight, double secondsIn)
        => ogreFight && secondsIn >= OgreWardAtSeconds - StingBeforeWardSeconds && secondsIn < OgreWardAtSeconds;

    /// <summary>
    /// On a degree, the Ogre fight keeps a familiar for the wisps: its Parting Blow is what kills the Great Wisp before
    /// it reaches the Ogre, and its whole-arena blast (about 330 on D3) is otherwise unavoidable. Korha 2026-11-07:
    /// two horns Parting-Blowed to bring the Wespe, the Wespe's Final Sting at 50 s, nothing out when the wisps came,
    /// both Great Wisps blew, dead. Until the wisp phase is over: no Parting Blow to cycle to the Wespe.
    /// </summary>
    public static bool OgreKeepsFamiliarForWisps(bool ogreFight, int degree, bool wispPhaseOver)
        => ogreFight && degree > 0 && !wispPhaseOver;

    /// <summary>…and the Wespe's Final Sting (it dies with it) waits while no other horn could come out after it.</summary>
    public static bool OgreHoldsStingForWisps(bool ogreFight, int degree, bool wispPhaseOver, bool replacementReady)
        => OgreKeepsFamiliarForWisps(ogreFight, degree, wispPhaseOver) && !replacementReady;

    /// <summary>Parting Blow's damage as measured on D3 (Minerva, 2026-11-07): 674 plain, 1,109 with Lingering Vantage.</summary>
    public const uint PartingBlowDamage = 650, PartingBlowVantageDamage = 1050;

    /// <summary>
    /// Parting Blow the Great Wisp (user, 2026-11-07: "kill it before it reaches the boss"; TPLemont: Parting Blow the
    /// Megalocrab just before the Great Wisp reaches the centre) once the Blow would finish it — the axes chip it down
    /// on its walk. Never a Blow that cannot kill it: that spends the familiar whose Cover could take the blast (Minerva
    /// counts the blast down as a raidwide since 2026-11-07).
    /// </summary>
    public static bool ShouldPartingBlowGreatWisp(
        bool onBoard, bool familiarOut, bool blowReady, uint greatWispHp, bool hasVantage)
        => onBoard && familiarOut && blowReady && greatWispHp > 0
           && greatWispHp <= (hasVantage ? PartingBlowVantageDamage : PartingBlowDamage);

    /// <summary>
    /// Casts the familiar must not be out for (research 2026-11-01): King Ahriman's Curtains for Rank 5 (49428/49429)
    /// kills a rank-25 familiar outright; Guttler's Molten Metal (49465-49468) and Beastly Aura (48607, 49452/49453)
    /// kill a familiar that Covers them (16 Guttler fights: 4.7 familiars lost a fight). Parting Blow sends it away
    /// (which also ends Cover), and no Snarl goes out while one is cast.
    /// </summary>
    public static readonly IReadOnlySet<uint> SendFamiliarAwayCastIds = new HashSet<uint>
    {
        49428, 49429,               // Curtains for Rank 5
        49465, 49466, 49467, 49468, // Molten Metal
        48607, 49452, 49453,        // Beastly Aura
    };

    /// <summary>Parting Blow hits 8 yalms round its target.</summary>
    public const float PartingBlowSplashYalms = 8f;

    /// <summary>Whether a Parting Blow at <paramref name="target"/> would catch a Zu egg (it breaks them like Shield Charge).</summary>
    public static bool PartingBlowBreaksEgg(IReadOnlyList<System.Numerics.Vector2> eggs, System.Numerics.Vector2 target)
        => SplashBreaksEgg(eggs, target, PartingBlowSplashYalms);

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
    /// Hold Tempered Release while the Zu lives above <see cref="ZuReleaseAtHpPct"/>: every egg it breaks makes the Zu
    /// stronger, and a release can be an AoE (Ponzi's Third Board guide: "not going to use behemoth meteor unless it's
    /// about to kill the zoo"). Parting Blow waits on an owed release, so it is held too. Whatever is targeted: with the
    /// hold keyed on the target, a release went out at a hatched Pullet beside the eggs (Korha 2026-10-31). Not when
    /// the familiar is falling — its retreat comes first. <paramref name="zuHpPct"/> is null when no Zu is in the fight.
    /// </summary>
    public static bool HoldReleaseAtZu(bool onBoard, float? zuHpPct, bool familiarFalling)
        => onBoard && zuHpPct is > ZuReleaseAtHpPct && !familiarFalling;

    /// <summary>Banemite Piece (First Board battle 4) and its Miteling Piece adds.</summary>
    public const uint BanemitePieceNameId = 14536, MitelingPieceNameId = 14537;

    /// <summary>Behemoth (bestiary 50): its Tempered Release is Meteor, a big AoE that one-shots adds.</summary>
    public const int BehemothBestiaryNo = 50;

    /// <summary>
    /// The Mitelings come on a timer, not at an HP mark: three at 56 s into the fight, three more at 113 s (17 Minerva
    /// replays of the Banemite, 2026-10-22 to 11-05; the first wave came at 8% to 62% Banemite HP, always at 55-57 s).
    /// </summary>
    public const double MitelingFirstWaveSeconds = 56, MitelingWaveEverySeconds = 57;

    /// <summary>
    /// Meteor always waits for the first wave (it came in 16 of the 17 fights); a later wave only when it is due within
    /// this long, since most fights end before it. Otherwise Meteor goes at the Banemite.
    /// </summary>
    public const double MeteorHoldsForWaveSeconds = 30;

    /// <summary>
    /// Once a wave is up, Meteor waits this long at most for the Mitelings to gather. A Miteling stands where it
    /// appeared for 4.9 s before it moves and is within 10 yalms of the Banemite 6 s after it became targetable (median;
    /// 7 s at the ninetieth percentile; 8 of 89 never come that close — they cast from range). At 6 s Meteor went out
    /// as they set off: 10 to 21 yalms away, one in three out of its 20-yalm reach (user, 2026-11-15: "need to wait for
    /// the adds to gather"; four Banemite fights that day).
    /// </summary>
    public const double MeteorWaitsForMitelingsSeconds = 9;

    /// <summary>...or until every Miteling is this close to the Meteor's target.</summary>
    public const float MitelingsBunchedYalms = 10f;

    /// <summary>At or below this the Banemite is nearly dead: Meteor finishes it instead of waiting.</summary>
    public const float MeteorAtBanemiteHpPct = 0.15f;

    /// <summary>Seconds until the next Miteling wave, <paramref name="secondsIn"/> seconds into the Banemite fight.</summary>
    public static double SecondsToNextMitelingWave(double secondsIn)
        => secondsIn < MitelingFirstWaveSeconds
            ? MitelingFirstWaveSeconds - secondsIn
            : MitelingWaveEverySeconds - ((secondsIn - MitelingFirstWaveSeconds) % MitelingWaveEverySeconds);

    /// <summary>
    /// Hold the Behemoth's Meteor for the Mitelings (user, 2026-11-06: Meteor kills them faster; fuurie's Third Degree
    /// clear puts Meteor on the Mitelings). With none up it waits for the first wave, or a later one due within
    /// <see cref="MeteorHoldsForWaveSeconds"/>; with a wave up, until they have bunched or
    /// <see cref="MeteorWaitsForMitelingsSeconds"/> has passed. Not when the Banemite is nearly dead, and not when the
    /// familiar is falling (its retreat comes first). Parting Blow waits on the owed release, so the Behemoth stays out.
    /// <paramref name="banemiteHpPct"/> is null when no Banemite is in the fight.
    /// </summary>
    public const int ZizBestiaryNo = 21;

    /// <summary>
    /// Beasts whose Tempered Release is kept for the Banemite's Mitelings: the Behemoth's Meteor (fuurie) and the Ziz's
    /// petrifying cone (TPLemont: "Ziz Tempered Release petrifies the adds").
    /// </summary>
    public static bool ReleaseIsForAdds(int? bestiaryNo) => bestiaryNo is BehemothBestiaryNo or ZizBestiaryNo;

    /// <summary>
    /// A beast the plan keeps for a later battle and has no more use for in this one (CrucibleDegreeTeams.LeavesAtOnce)
    /// is sent off with Parting Blow as soon as its release is spent — the caller waits for that — once a fight, and
    /// only with another horn ready to follow it. TPLemont: "Parting Blow the Uragnite at once, summon Flying Trap".
    /// </summary>
    public static bool LeavesNow(bool onBoard, bool planSaysLeave, bool alreadyLeftThisFight, bool replacementReady)
        => onBoard && planSaysLeave && !alreadyLeftThisFight && replacementReady;

    /// <summary>
    /// The battle's tankbuster beast (CrucibleDegreeTeams.BusterBeast) is not retreated, however low, until the
    /// tankbusters it is there for have all been cast and the last one has landed. TPLemont at the Banemite: the Ziz
    /// is kept alive for the second Deadly Thrust and Snarls into it.
    /// </summary>
    public static bool HoldsBusterBeast(int? busterBeastNo, int? familiarNo, int bustersCast, int bustersWanted, bool busterCasting)
        => busterBeastNo is { } no && familiarNo == no && (bustersCast < bustersWanted || busterCasting);

    /// <summary>
    /// While the tankbuster beast is held and low, the beastmaster takes the enemy off it (Challenge also ends its
    /// Cover): the video's "Challenge when the Ziz gets low". Not with the tankbuster on its way — then it Snarls.
    /// </summary>
    public static bool ChallengesForBusterBeast(
        bool enabled, bool ready, bool heldForBuster, bool enemyOnFamiliar, float familiarHpPct, float fallingLine,
        float selfHpPct, float secondsUntilTankbuster)
        => enabled && ready && heldForBuster && enemyOnFamiliar && familiarHpPct < fallingLine
           && selfHpPct > SelfCriticalHpPct && secondsUntilTankbuster > TankbusterLeadSeconds;

    public static bool HoldMeteorForMitelings(
        bool onBoard, int? bestiaryNo, float? banemiteHpPct, int mitelingsUp, bool mitelingsBunched,
        double secondsSinceMitelingsUp, double secondsIn, bool familiarFalling)
    {
        if (!onBoard || !ReleaseIsForAdds(bestiaryNo) || banemiteHpPct is not > MeteorAtBanemiteHpPct || familiarFalling)
            return false;
        if (mitelingsUp > 0)
            return !mitelingsBunched && secondsSinceMitelingsUp < MeteorWaitsForMitelingsSeconds;
        return secondsIn < MitelingFirstWaveSeconds || SecondsToNextMitelingWave(secondsIn) <= MeteorHoldsForWaveSeconds;
    }

    /// <summary>Ymir Piece, its shell and the Sahagin Piece (Third Board battle 15).</summary>
    public const uint YmirPieceNameId = 14569, YmirShellNameId = 14570, SahaginPieceNameId = 14571;

    /// <summary>Vulnerability Down: on the Ymir while it sits in a shell (BossMod's YmirPiece module; 23 replays).</summary>
    public const uint YmirShelledStatusId = 2198;

    /// <summary>Diremite (bestiary 8): its Tempered Release binds for 30 s. Ice Golem (47): Soul Crush to borrow.</summary>
    public const int DiremiteBestiaryNo = 8, IceGolemBestiaryNo = 47;

    /// <summary>What the familiar's Tempered Release does in the Ymir fight (<see cref="YmirReleasePlan"/>).</summary>
    public enum YmirRelease
    {
        /// <summary>As anywhere else.</summary>
        Normal,
        /// <summary>Held, and owed: Parting Blow waits for it.</summary>
        Hold,
        /// <summary>Never pressed, and not owed: Parting Blow does not wait for it.</summary>
        Never,
        /// <summary>At the Ymir's shell.</summary>
        AtShell,
        /// <summary>At the Ymir itself.</summary>
        AtYmir,
    }

    /// <summary>
    /// Ymir Plan A on a degree (user, 2026-11-06; fuurie's Third Degree clear, fuminpapa, BMRV): the Wespe's Final Sting
    /// goes at the shell; the Ice Golem's release is never pressed (fuurie: pressing it by habit loses the plan; BMRV:
    /// "without Tempered Release"); the Diremite's release, a 30 s Bind, waits for the shell to break and then goes at
    /// the Ymir, which cannot crawl back into a shell while bound.
    /// </summary>
    public static YmirRelease YmirReleasePlan(int? bestiaryNo, bool shelled) => bestiaryNo switch
    {
        Daedalus.Rotation.ArtemisCore.Modules.DamageModule.WespeBestiaryNo => shelled ? YmirRelease.AtShell : YmirRelease.Normal,
        IceGolemBestiaryNo => YmirRelease.Never,
        DiremiteBestiaryNo => shelled ? YmirRelease.Hold : YmirRelease.AtYmir,
        _ => YmirRelease.Normal,
    };

    /// <summary>
    /// The Ice Golem's Parting Blow goes at the Ymir as soon as its shell breaks (fuurie, fuminpapa), so the Diremite
    /// comes out to bind it. Until then the Ice Golem stays out holding the Sahagin, unless it is falling.
    /// </summary>
    public static bool YmirPartingBlowNow(int? bestiaryNo, bool shelled) => bestiaryNo == IceGolemBestiaryNo && !shelled;

    /// <summary>The Ice Golem's Parting Blow waits while the Ymir is shelled; a falling Ice Golem still retreats.</summary>
    public static bool YmirHoldsPartingBlow(int? bestiaryNo, bool shelled, bool falling)
        => bestiaryNo == IceGolemBestiaryNo && shelled && !falling;

    /// <summary>
    /// No instinct chain on the shell: it breaks without it, and the chain goes into the bound Ymir (fuurie, fuminpapa).
    /// </summary>
    public static bool HoldsInstinctsAtYmirShell(bool ymirFight, bool shelled) => ymirFight && shelled;

    /// <summary>
    /// In the Ymir fight the familiar holds the Sahagin, never the Ymir (its Head Snatch is a front cone, so the
    /// beastmaster keeps it facing away from the familiar): Snarl goes at the Sahagin whenever it is not on the familiar.
    /// </summary>
    public static bool SnarlsTheSahagin(bool enabled, bool familiarOut, bool sahaginOnFamiliar, bool snarlReady)
        => enabled && familiarOut && !sahaginOnFamiliar && snarlReady;

    /// <summary>Shield Charge hits everything within this of its target (Action sheet: circle, effect range 6).</summary>
    public const float ShieldChargeSplashYalms = 6f;

    /// <summary>Added to a splash radius for an egg's own size and a step of drift.</summary>
    public const float EggMarginYalms = 1f;

    /// <summary>
    /// Whether a splash of <paramref name="radius"/> at <paramref name="at"/> would catch a Zu egg. A Shield Charge at a
    /// hatched Cockerel broke the two eggs beside it (Korha 2026-10-31).
    /// </summary>
    public static bool SplashBreaksEgg(IReadOnlyList<System.Numerics.Vector2> eggs, System.Numerics.Vector2 at, float radius)
        => eggs.Any(e => System.Numerics.Vector2.Distance(e, at) <= radius + EggMarginYalms);

    /// <summary>
    /// Whether a Shield Charge at this target would catch an egg. Its 6-yalm splash is centred where you land (Minerva
    /// session, from Korha's replays): at the target's edge on your side, or where you stand when already that close.
    /// Where you stand now is always checked too: the Zu dashes, and in the 19:19 replay it crossed onto Korha in 0.3 s
    /// just as the charge went off, so the splash broke the two eggs beside her while the Zu had been read 17 yalms
    /// away. The target's own centre is checked as well.
    /// </summary>
    public static bool ShieldChargeBreaksEgg(
        IReadOnlyList<System.Numerics.Vector2> eggs, System.Numerics.Vector2 target, float targetHitbox,
        System.Numerics.Vector2 player, float playerHitbox)
    {
        if (eggs.Count == 0)
            return false;
        var reach = targetHitbox + playerHitbox;
        var away = player - target;
        var landing = away.Length() <= reach || away == System.Numerics.Vector2.Zero
            ? player
            : target + (System.Numerics.Vector2.Normalize(away) * reach);
        return SplashBreaksEgg(eggs, landing, ShieldChargeSplashYalms) || SplashBreaksEgg(eggs, player, ShieldChargeSplashYalms)
               || SplashBreaksEgg(eggs, target, ShieldChargeSplashYalms);
    }

    /// <summary>The Second Master's Board (XBMContent index).</summary>
    public const int SecondMastersBoardIndex = 5;

    /// <summary>
    /// Legendary: the beastmaster tanks on every board, as every Japanese Legendary clear does (research 2026-11-06,
    /// docs/crucible-research/jp-legendary-tanking.md) — Challenge keeps the enemy on you, Unbeastable is kept by dodging,
    /// and the familiars stay full. The one exception is the Second Master's Board, where the familiar takes the
    /// tankbusters (Lauda's Thunderbolt, Flauros's Erratic Blaster, Mindflayer's Void Paralyze III): Snarl it in as one
    /// comes, and no Challenge until it has landed.
    /// </summary>
    public static bool LegendaryFamiliarTakesBuster(int boardIndex, float secondsUntilTankbuster)
        => boardIndex == SecondMastersBoardIndex && secondsUntilTankbuster <= TankbusterLeadSeconds;

    /// <summary>Legendary: Snarl the familiar into a tankbuster it should take — out and above the falling line.</summary>
    public static bool ShouldSnarlForLegendaryBuster(bool enabled, bool ready, bool hasFamiliar, float familiarHpPct, float fallingLine)
        => enabled && ready && hasFamiliar && familiarHpPct >= fallingLine;

    /// <summary>
    /// A degree with counter armor held (Briar Armor, user 2026-11-06): the beastmaster takes most of the hits, since every
    /// physical hit taken deals damage back on top of the axe. The familiar only covers a critical beastmaster.
    /// </summary>
    public static bool BeastmasterTanksForCounterArmor(int degree, bool counterArmorHeld) => degree > 0 && counterArmorHeld;

    /// <summary>
    /// A degree (user, 2026-11-06): a tankbuster kills a familiar outright on the early boards, and the trick of recalling
    /// it just before the hit lands was fixed, so the beastmaster takes every tankbuster — no Snarl to hand one (or a
    /// raidwide) to the familiar, and Challenge takes the enemy back when it is on the familiar. Only a critical
    /// beastmaster still gets covered.
    /// </summary>
    public static bool BeastmasterTakesTheBuster(int degree, float secondsUntilTankbuster)
        => degree > 0 && secondsUntilTankbuster <= TankbusterLeadSeconds;

    /// <summary>
    /// A degree (user, 2026-11-15, after TPLemont's and fuurie's Third Degree clears): the beastmaster holds the enemy
    /// while the beast out is one a later battle of the board needs, so it stays whole — HP carries over between a
    /// board's battles. A beast no later battle needs does the tanking and takes the tankbusters, down to the falling
    /// line ("use two pets' HP to the last drop", fuurie; TPLemont sacrifices the Flying Trap and the Ziz to the
    /// Banemite's Deadly Thrusts). With the beastmaster tanking every beast (Korha's second run of 2026-11-15) she was
    /// at 28% a minute into each fight; with every beast tanking (the first run) all three ended each fight at 3-33%
    /// and the plan's beasts were worn out after one battle.
    /// </summary>
    public static bool BeastmasterTanksOnDegree(int degree, bool beastNeededLater) => degree > 0 && beastNeededLater;

    /// <summary>
    /// "We take tank busters unless the pet is not needed later" (user, 2026-11-15). On Third Degree Deadly Thrust
    /// hits a familiar for about 440, 60-81% of its HP, and the beastmaster for 31% of theirs. So the familiar takes
    /// one only when no later battle of the board plans to use it (<paramref name="neededLater"/>) and another horn is
    /// ready to follow it.
    /// </summary>
    public static bool FamiliarTakesTheBusterOnDegree(
        bool snarlEnabled, bool familiarOut, float familiarHpPct, bool neededLater, bool anotherHornReady)
        => snarlEnabled && familiarOut && familiarHpPct > 0f && !neededLater && anotherHornReady;

    /// <summary>
    /// Enemy casts that leave a debuff on whoever they hit and are not tankbusters: the familiar takes them (user,
    /// 2026-11-15: "pets take vuln stack and debuffs"). From 3,338 recordings (burn-reference/bst-sandbox/debuff-hits.txt):
    /// the Arch Demon's Dismember (46879, 4.2 s cast, Damage Down every time). The Great Wisp's Scorched Earth has no
    /// cast bar and goes by <see cref="ShouldSnarlForWisp"/>; Pas de Seul's Cold Caress and Beguiling Mist are left to
    /// the beastmaster, who carries the resist gear and serums for them.
    /// </summary>
    public static readonly IReadOnlySet<uint> FamiliarTakesCastIds = new HashSet<uint> { 46879 };

    /// <summary>At or below this HP a tankbuster is not the beastmaster's to take while a beast that can be spent can still come out.</summary>
    public const float SpareForBusterBelowHpPct = 0.75f;

    /// <summary>
    /// A tankbuster is coming, the beast out is one a later battle needs, and the beastmaster cannot afford the hit:
    /// bring out a beast that can be spent to take it, dying if it must (TPLemont's Third Degree Banemite: "keep it
    /// alive until the next tankbuster, then the Ziz Snarls into it"). Korha, 2026-11-15: at 52% she took the second
    /// Deadly Thrust herself for the Behemoth's sake (453, then Poison) and died with the Banemite at 8%, while the
    /// Ice Golem, which no later battle needs, sat on its horn.
    /// </summary>
    public static bool WantsSpareForBuster(int degree, bool busterSoon, bool beastOutIsKept, float selfHpPct)
        => degree > 0 && busterSoon && beastOutIsKept && selfHpPct <= SpareForBusterBelowHpPct;

    /// <summary>Snarl so the familiar is the one a debuff cast lands on: out, above the falling line, and not already holding the caster.</summary>
    public static bool ShouldSnarlForDebuffCast(
        bool enabled, bool ready, bool familiarOut, float familiarHpPct, float fallingLine, bool casterOnFamiliar)
        => enabled && ready && familiarOut && familiarHpPct >= fallingLine && !casterOnFamiliar;

    /// <summary>
    /// Two or more enemies: "pet takes one while you take the rest" (user, 2026-11-15). Snarl one that is on the
    /// beastmaster and is not the target, while the familiar holds none and is above the falling line.
    /// </summary>
    public static bool ShouldSnarlToHoldOne(
        bool enabled, bool ready, bool familiarOut, float familiarHpPct, float fallingLine, bool familiarHoldsOne, bool anotherOnBeastmaster)
        => enabled && ready && familiarOut && familiarHpPct >= fallingLine && !familiarHoldsOne && anotherOnBeastmaster;

    /// <summary>
    /// The beastmaster tanks, but Snarl's Cover (the familiar takes all their damage, up to 45 s) is still up with
    /// nothing left for it to take: Challenge ends it, so the familiar stops bleeding for hits it was not sent in for.
    /// </summary>
    public static bool CoverNoLongerWanted(float coverLeft, float wispIn, bool debuffCastGoing, float selfHpPct)
        => coverLeft > 0f && wispIn > WispWalkSeconds && !debuffCastGoing && selfHpPct > SelfCriticalHpPct;

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
        bool enabled, uint adjustedId, float selfHpPct, float edgeDistanceYalms, bool selfHasCleansableDebuff,
        bool holdSoulCrush = false, bool coveredOnBoard = false)
    {
        if (!enabled)
            return BeastModeUse.None;

        return adjustedId switch
        {
            // +5% critical hit rate for 30s is damage; the 25% physical mitigation comes with it. On a board it waits
            // out a Cover: while a beast takes the hits the 25% protects nobody (it went out at 3-32 s in every
            // recorded Third Degree fight, mostly under a Cover).
            BSTActions.BeastskinId => coveredOnBoard ? BeastModeUse.None : BeastModeUse.Self,

            // Pure defence: a full block for 15s, or a magic barrier. For when something is hitting us.
            BSTActions.VileskinId or BSTActions.ScaleskinId
                => selfHpPct < SkinHpPct ? BeastModeUse.Self : BeastModeUse.None,

            // -10% damage dealt and a DoT on everything within 6y.
            BSTActions.SeedsowerId
                => edgeDistanceYalms <= SeedsowerYalms ? BeastModeUse.Self : BeastModeUse.None,

            // 300 potency in melee, 1,000 and 100 TP if it interrupts. Free damage either way — unless it is being
            // kept for an interrupt (HoldsSoulCrush).
            BSTActions.SoulCrushId
                => edgeDistanceYalms <= MeleeYalms && !holdSoulCrush ? BeastModeUse.Enemy : BeastModeUse.None,

            // A cleanse that pays 10% HP and 100 TP — but only when it actually removes something.
            BSTActions.ScouringAshId
                => selfHasCleansableDebuff ? BeastModeUse.Self : BeastModeUse.None,

            // Cloud Skim is a dash in a chosen direction, and Quelling Wave is a 350-potency spell on the
            // GCD that loses to the axe combo in melee. Neither is pressed here: Quelling Wave goes only at a
            // buff to strip (PickDispelTarget).
            _ => BeastModeUse.None,
        };
    }
    /// <summary>An interruptible cast with less than this left is not worth a Borrow (BMRV uses the same).</summary>
    public const float InterruptMinCastLeftSeconds = 0.5f;

    /// <summary>Below this, the beastmaster's own HP comes first: a held Soul Crush / Quelling Wave may be borrowed over.</summary>
    public const float KeepKinshipMinHpPct = 0.5f;

    /// <summary>
    /// An enemy cast Soul Crush should stop: one the boss engine flagged, or — on a board — any cast the game marks
    /// interruptible with time left. Minerva doesn't flag Medusa's Lamias' Raise (49287), which Game8 and the
    /// Second Master's Board replay both say is the fight's problem; the game's own flag catches it.
    /// </summary>
    public static bool IsInterruptCandidate(bool flaggedByEngine, bool onBoard, bool casting, bool interruptible, float castLeftSeconds)
        => casting && (flaggedByEngine || (onBoard && interruptible && castLeftSeconds > InterruptMinCastLeftSeconds));

    /// <summary>
    /// Borrow right now, ahead of everything (a release still owed included), to get the one Kinship a pending job
    /// needs: Soul Crush for a cast to interrupt with a Soulkin out, Quelling Wave for a buff to strip with a Wavekin out.
    /// Nothing when the button already holds it. (BMRV does the same; fuurie's Drake clear borrows Quelling Wave for
    /// the Blaze Spikes reflect.)
    /// </summary>
    public static bool ShouldBorrowForJob(
        bool enabled, bool ready, BeastClassification? familiarKin, uint beastModeId, bool interruptPending, bool dispelPending)
        => enabled && ready
           && ((interruptPending && familiarKin == BeastClassification.Soulkin && beastModeId != BSTActions.SoulCrushId)
               || (dispelPending && familiarKin == BeastClassification.Wavekin && beastModeId != BSTActions.QuellingWaveId));

    /// <summary>
    /// On a board, an unused Soul Crush or Quelling Wave stays on the button: an everyday Borrow from a familiar of
    /// another kin would replace it, and the interrupt or dispel it was for would find nothing. Let go when the
    /// beastmaster drops under half HP, or when the familiar out gives the same card back. A defensive skin (Beastskin,
    /// Vileskin, Scaleskin) also wins as soon as the beastmaster needs one: under the skin line, or a tankbuster or
    /// raidwide inside the lead time — the guides time Beastskin to the heavy hits.
    /// </summary>
    public static bool KeepsKinshipCard(
        bool onBoard, uint beastModeId, BeastClassification? familiarKin, float selfHpPct, bool hitComing = false)
    {
        // Scouring Ash is borrowed for a debuff still to come (the Banemite's second Deadly Thrust): no skin replaces
        // it, whatever is about to hit. Only an Ashkin, which gives the same card back, may be borrowed from.
        if (onBoard && beastModeId == BSTActions.ScouringAshId && familiarKin != BeastClassification.Ashkin)
            return true;

        var defensiveKin = familiarKin is BeastClassification.Beastkin or BeastClassification.Vilekin
            or BeastClassification.Scalekin;
        if (defensiveKin && (hitComing || selfHpPct < SkinHpPct))
            return false;

        return onBoard && selfHpPct >= KeepKinshipMinHpPct
               && ((beastModeId == BSTActions.SoulCrushId && familiarKin != BeastClassification.Soulkin)
                   || (beastModeId == BSTActions.QuellingWaveId && familiarKin != BeastClassification.Wavekin));
    }

    /// <summary>The Beast Mode action a familiar's Kinship gives; 0 for an unknown kin.</summary>
    public static uint KinshipCard(BeastClassification? kin) => kin switch
    {
        BeastClassification.Beastkin => BSTActions.BeastskinId,
        BeastClassification.Vilekin => BSTActions.VileskinId,
        BeastClassification.Seedkin => BSTActions.SeedsowerId,
        BeastClassification.Wavekin => BSTActions.QuellingWaveId,
        BeastClassification.Scalekin => BSTActions.ScaleskinId,
        BeastClassification.Soulkin => BSTActions.SoulCrushId,
        BeastClassification.Ashkin => BSTActions.ScouringAshId,
        _ => 0,
    };

    /// <summary>
    /// The familiar out is the battle's card beast (CrucibleDegreeTeams.CardBeast) and its card is not on the button
    /// yet: its summon goes on Borrow, and its Tempered Release is not pressed — the game allows one of the two a
    /// summon. Korha's 28 recorded fights of 2026-11-15: every Kinship sat in the slot One with Nature had held, and
    /// the Megalocrab, borrowed from at the Ogre, never released in the 57 s it stayed out.
    /// </summary>
    public static bool BorrowsInPlaceOfRelease(
        bool enabled, int? cardBeastNo, int? familiarNo, BeastClassification? familiarKin, uint beastModeId)
        => enabled && cardBeastNo is { } no && familiarNo == no
           && KinshipCard(familiarKin) is var card && card != 0 && beastModeId != card;

    /// <summary>
    /// The card borrowed before the fight stays on Beast Mode until it is used: an everyday Borrow from the familiar out
    /// would replace a Vileskin before the hit it is for (Beastskin goes out at the pull anyway). A Beastkin out may
    /// replace it: its Beastskin is the better card. Borrows for an interrupt or a dispel still go.
    /// </summary>
    public static bool KeepsPreBorrowedCard(bool onBoard, uint beastModeId, uint preBorrowedCardId, BeastClassification? familiarKin)
        => onBoard && preBorrowedCardId != 0 && beastModeId == preBorrowedCardId
           && familiarKin != BeastClassification.Beastkin;

    /// <summary>
    /// On a board, Soul Crush is not spent as plain damage (300) while it would leave nothing for the next interrupt
    /// (1,000 and the interrupt itself): only when the familiar out is a Soulkin and Borrow is ready to give it back.
    /// </summary>
    public static bool HoldsSoulCrush(bool onBoard, bool borrowReady, BeastClassification? familiarKin)
        => onBoard && !(borrowReady && familiarKin == BeastClassification.Soulkin);

    /// <summary>
    /// The enemy for Quelling Wave: the hard target if it carries a buff to strip, else the nearest that does. 0 when
    /// none does. Reach is left to the game's action status.
    /// </summary>
    public static ulong PickDispelTarget(
        ulong hardTargetId, System.Collections.Generic.IEnumerable<(ulong Id, float EdgeDistance)> buffed)
    {
        ulong best = 0;
        var bestDistance = float.MaxValue;
        foreach (var (id, distance) in buffed)
        {
            if (id == hardTargetId)
                return id;
            if (distance < bestDistance)
            {
                best = id;
                bestDistance = distance;
            }
        }

        return best;
    }

    /// <summary>
    /// The Ogre fight on a board wants Quelling Wave on the button for its small wisps: one Wave kills one, on Third
    /// Degree too (TPLemont's D3 guide, user 2026-11-07). Borrowed whenever a Wavekin (the Megalocrab) is out, and
    /// then kept by <see cref="KeepsKinshipCard"/> after it leaves.
    /// </summary>
    public static bool WantsQuellingWaveForWisps(bool onBoard, bool ogreFight) => onBoard && ogreFight;

    /// <summary>
    /// The small wisp to Quelling Wave: the hard target if it is one, else the nearest. 0 off a board, without the
    /// Wave on the button, or with none up. The Great Wisp is not here: it takes several, and the axes win in melee.
    /// </summary>
    public static ulong PickWispForQuellingWave(
        bool onBoard, uint beastModeId, ulong hardTargetId,
        System.Collections.Generic.IEnumerable<(ulong Id, float EdgeDistance)> smallWisps)
        => onBoard && beastModeId == BSTActions.QuellingWaveId ? PickDispelTarget(hardTargetId, smallWisps) : 0;
}
