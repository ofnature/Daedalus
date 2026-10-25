using System.Collections.Generic;

namespace Daedalus.Config.DPS;

/// <summary>
/// Beastmaster (Artemis) configuration.
/// <para>
/// Deliberately small. Beastmaster is a limited job with a Lv50 cap and no role actions, so there
/// is no burst window, no party coordination and no mitigation to configure — see
/// <see cref="Daedalus.Rotation.Common.Helpers.LimitedJobContentPolicy"/>.
/// </para>
/// </summary>
public sealed class BeastmasterConfig
{
    /// <summary>
    /// Fire the four instinctual skills (Avalanche / Mistral / Spinning / Gale Axe).
    /// They spend the whole TP gauge and run on their own 5s recast, independent of the GCD.
    /// </summary>
    public bool EnableInstinctualSkills { get; set; } = true;

    /// <summary>
    /// Prefer the affinity that completes an intentional combo (Sunstrider / Moonstalker) over
    /// simply firing whichever instinctual skill is off cooldown.
    /// </summary>
    public bool PreferIntentionalCombos { get; set; } = true;

    /// <summary>Order the familiar's instinctual skill with Trick when it is ready.</summary>
    public bool EnableTrick { get; set; } = true;

    /// <summary>
    /// Fire Parting Blow. Off by default: it deals 1,000 potency in 8y but the familiar RETREATS
    /// afterwards, which ends Trick until the player summons again. That is a trade the player
    /// should opt into, not one the rotation should make unprompted.
    /// </summary>
    public bool EnablePartingBlow { get; set; }

    /// <summary>
    /// Order the familiar's signature move with Tempered Release, once per summon. On by default: unlike
    /// Parting Blow the familiar stays out afterwards (Wespe's Final Sting is the exception, and follows
    /// <see cref="EnablePartingBlow"/>).
    /// </summary>
    public bool EnableTemperedRelease { get; set; } = true;

    /// <summary>
    /// Rally when the axes are out of TP, and Rallying Cheer when Trick is out of Familiar TP. Both only
    /// refill a gauge, so there is nothing to lose by pressing them.
    /// </summary>
    public bool EnableRally { get; set; } = true;

    /// <summary>
    /// Shield Charge as damage, from inside melee range only — it is never used to cross distance.
    /// </summary>
    public bool EnableShieldCharge { get; set; } = true;

    /// <summary>
    /// Borrow the familiar's Kinship once Tempered Release is spent, and press the action Beast Mode becomes
    /// (see <c>ArtemisCooldownRules.DecideBeastMode</c> for which kin's action is pressed when).
    /// </summary>
    public bool EnableBorrow { get; set; } = true;

    /// <summary>
    /// Blow a Battlehorn whenever no familiar is out — after a death, a zone change, a Parting Blow. The
    /// beast that was out comes back when its horn is ready, otherwise the next horn in line. On by default:
    /// without a familiar the job has no Trick, no combos and no Tempered Release.
    /// </summary>
    public bool KeepFamiliarOut { get; set; } = true;

    #region Crucible

    /// <summary>
    /// Crucible: whenever ready, while your familiar has 25% HP or more, Snarl hands it the enemy (it Threatens and
    /// covers you — takes all your damage for 45s).
    /// </summary>
    public bool EnableSnarl { get; set; } = true;

    /// <summary>
    /// Crucible: when the enemy is on a familiar under 25% HP, take it back with Challenge — only while you are above
    /// 70%. Otherwise the familiar tanks: your death ends the run.
    /// </summary>
    public bool EnableChallenge { get; set; } = true;

    /// <summary>
    /// On the Crucible's horn screen before each battle, put the three team members that hit the battle's weakness on
    /// the Battlehorns (the game does not keep them between battles). Never starts the battle.
    /// </summary>
    public bool AutoCrucibleHorns { get; set; } = true;

    /// <summary>Open the Crucible window on entering a board. It closes again on leaving.</summary>
    public bool CrucibleWindowAutoOpen { get; set; } = true;

    /// <summary>Open the Crucible team picker while the board layout or horn screen is up.</summary>
    public bool CrucibleTeamPickerAutoOpen { get; set; } = true;

    /// <summary>
    /// The player's horn picks per battle, keyed <c>"board:battle"</c> (<c>CrucibleHornPlan.PickKey</c>): Bestiary
    /// numbers in horn order, 0 = Auto. A battle with no entry is all Auto.
    /// </summary>
    public Dictionary<string, int[]> CrucibleHornPicks { get; set; } = [];

    /// <summary>What runs are for: clearing (weakness roster and horns) or levelling (core carries, lowest ranks ride).</summary>
    public Daedalus.Data.CrucibleTeamGoal CrucibleGoal { get; set; } = Daedalus.Data.CrucibleTeamGoal.Clear;

    /// <summary>The three beasts that carry (Bestiary numbers, 0 = the highest rank not already chosen).</summary>
    public int[] CrucibleCoreTeam { get; set; } = [0, 0, 0];

    /// <summary>Beast ranks as last seen on Team Composition or the horn screen (Bestiary number → rank).</summary>
    public Dictionary<int, int> CrucibleRanks { get; set; } = [];

    /// <summary>
    /// Step through the board's screens: start each battle once the horns are set, take the spoils' tokens, the first
    /// treasure, leave shops, rest the most hurt beast at campsites, close the result. Off by default.
    /// </summary>
    public bool CrucibleAutoPilot { get; set; }

    /// <summary>Autopilot: also take the first item from the spoils (beast gear helps survive; costs Nameday Attire).</summary>
    public bool CrucibleLootTakeItems { get; set; }

    /// <summary>
    /// Loop: runs still to start. With the autopilot on, after a run Daedalus talks to Lauda (you must be next to
    /// her), opens the same board, sets the goal's team and goes in again; one is taken off per board entered.
    /// </summary>
    public int CrucibleLoopRuns { get; set; }

    /// <summary>
    /// With the autopilot on, walk the board from space to space (vnavmesh), taking the suggested side at each fork.
    /// </summary>
    public bool CrucibleWalkBoard { get; set; } = true;

    #endregion

    #region Battlehorns

    /// <summary>
    /// Keep <see cref="BattlehornTeam"/>'s beasts on the three Battlehorns: out of combat, when they
    /// don't match, open the Master's Bestiary, assign them and close it. Off by default: it opens a
    /// window and clicks through it for you.
    /// </summary>
    public bool AutoSetBattlehorns { get; set; }

    /// <summary>Id of the chosen team in <see cref="Daedalus.Data.BattlehornTeams"/>.</summary>
    public string BattlehornTeam { get; set; } = "single-target";

    /// <summary>The player's own team (Bestiary numbers per horn, 0 = leave alone).</summary>
    public int[] SavedBattlehorns { get; set; } = [0, 0, 0];

    #endregion

    #region Capture

    /// <summary>
    /// <b>Part 1 — scan logging (debug builds only).</b> Records the Gauge action's scan text into
    /// the beast-capture ledger. The tooling behind this is compiled out of Release entirely, so
    /// the toggle does nothing there; it exists so data collection can run with auto-capture OFF
    /// while samples are still being gathered.
    /// </summary>
    public bool EnableScanLogging { get; set; } = true;

    /// <summary>
    /// <b>Part 2 — auto-capture (all builds).</b> Fires Capture against beasts the ledger already
    /// knows are capturable. Independent of <see cref="EnableScanLogging"/> by design: collection
    /// and acting-on-collected-data are separate decisions, and running both at once while the
    /// scan wording is still unconfirmed is the case worth being able to avoid.
    /// </summary>
    public bool EnableAutoCapture { get; set; }

    /// <summary>
    /// How close to the beast's estimated death to apply Capture, in seconds. Lower wastes less of
    /// the 120s window; higher is safer against a time-to-kill estimate that runs long. Hard-clamped
    /// at runtime to the debuff window minus <see cref="CaptureSafetyMarginSeconds"/>, so no value
    /// here can cause the debuff to expire before the kill.
    /// </summary>
    public float CaptureLeadSeconds { get; set; } = 20f;

    /// <summary>
    /// Slack kept between the end of the Capture debuff and the expected kill, in seconds. Guards
    /// against a TTK estimate that runs low. Better slightly early than a missed window.
    /// </summary>
    public float CaptureSafetyMarginSeconds { get; set; } = 10f;

    #endregion
}
