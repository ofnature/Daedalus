using System;
using System.Collections.Generic;
using Daedalus.Data;
using Daedalus.Rotation.ArtemisCore.Abilities;
using Daedalus.Rotation.ArtemisCore.Context;
using Daedalus.Rotation.Common.Helpers;
using Daedalus.Rotation.Common.Scheduling;
using Daedalus.Services.Action;
using Daedalus.Services.Beastmaster;

namespace Daedalus.Rotation.ArtemisCore.Modules;

/// <summary>
/// Fires Capture at the right moment against a beast that is capturable and not yet in the Bestiary.
///
/// <para>
/// <b>Which enemies.</b> A Gauge scan of that enemy (the ledger) is direct evidence and wins.
/// Without one, <see cref="BstCaptureMobs"/> — the community's enemy-name → beast list — names the
/// beast, and the game's own Bestiary says whether you have it; Capture needs you at or above the
/// enemy's level. See <see cref="CheckTarget"/>.
/// </para>
///
/// <para>
/// <b>Ships in every build.</b> Only the ledger's <i>collection</i> tooling is debug-gated; this
/// reads the persisted table and nothing else, so it works in Release off whatever a debug build
/// recorded. If the table is empty it simply never fires.
/// </para>
///
/// <para>
/// <b>The timing problem.</b> Interest Captured lasts 120s and the kill must land while it is up.
/// That makes two opposite mistakes possible: apply too early in a long fight and the debuff
/// expires before the beast dies; wait too long and the beast dies before Capture ever lands. So
/// the rule is a window, not a threshold — fire once the estimated time-to-kill has dropped inside
/// the debuff's reach, biased late to waste as little of the window as possible.
/// </para>
///
/// <para>
/// <b>It refuses to guess.</b> <c>ITimeToKillService</c> returns <c>float.MaxValue</c> when it has
/// no confident estimate — early in a pull, or when damage is erratic. That is treated as "do not
/// act", not as "infinite time": the module no-ops and the player captures manually, which is
/// strictly better than burning the one attempt on a bad estimate.
/// </para>
/// </summary>
public sealed class CaptureModule : IArtemisModule
{
    /// <summary>Ahead of the damage rotation: a missed capture window cannot be retried this pull.</summary>
    public int Priority => 20;

    public string Name => "Capture";

    /// <summary>
    /// TTK above this is treated as no estimate at all. The service reports <c>float.MaxValue</c>
    /// for "unknown", but a merely absurd number (a target barely losing HP) is just as useless,
    /// and both must fail the same way.
    /// </summary>
    internal const float NoConfidentEstimateSeconds = 3600f;

    private readonly BeastCaptureLedger? _ledger;
    private readonly Func<uint, bool?> _isPetUnlocked;

    /// <summary>Targets we have already fired Capture at, and when. Keyed by game object id.</summary>
    private readonly Dictionary<ulong, DateTime> _appliedUtc = new();

    internal Func<DateTime> UtcNow = () => DateTime.UtcNow;

    /// <param name="ledger">Gauge scans; may be null.</param>
    /// <param name="isPetUnlocked">Bestiary number → owned? Null while the Bestiary has not loaded.</param>
    public CaptureModule(BeastCaptureLedger? ledger, Func<uint, bool?>? isPetUnlocked = null)
    {
        _ledger = ledger;
        _isPetUnlocked = isPetUnlocked ?? (_ => null);
    }

    public bool TryExecute(IArtemisContext context, bool isMoving) => false;

    public void UpdateDebugState(IArtemisContext context) { }

    public void CollectCandidates(IArtemisContext context, RotationScheduler scheduler, bool isMoving)
    {
        var cfg = context.Configuration.Beastmaster;
        if (!cfg.EnableAutoCapture || !context.InCombat)
            return;

        var player = context.Player;
        var target = context.TargetingService.FindEnemyForAction(
            context.Configuration.Targeting.EnemyStrategy,
            BSTActions.Capture.ActionId,
            player);

        if (target is null)
            return;

        var name = target.Name.TextValue;
        var listed = BstCaptureMobs.BeastFor(name);
        var check = CheckTarget(
            name,
            _ledger?.Find(name),
            _ledger?.IsNoPact(name) == true,
            listed,
            listed is null ? null : _isPetUnlocked((uint)listed.BestiaryNo),
            player.Level,
            (target as Dalamud.Game.ClientState.Objects.Types.ICharacter)?.Level ?? 0);
        if (!check.Go)
        {
            context.Debug.CaptureState = check.Why;
            return;
        }

        // One attempt per target per window — Capture re-applied on a beast that already has the
        // debuff spends a GCD to refresh something that was not going to expire anyway.
        if (_appliedUtc.TryGetValue(target.GameObjectId, out var appliedAt)
            && (UtcNow() - appliedAt).TotalSeconds < BSTActions.CaptureWindowSeconds)
        {
            context.Debug.CaptureState = $"{name}: already captured this pull";
            return;
        }

        if (!ActionAvailability.MeetsLevelAndLearned(player.Level, context.ActionService, BSTActions.Capture)
            || context.ActionService.GetActionStatusCode(BSTActions.Capture.ActionId, target.GameObjectId) != 0)
        {
            context.Debug.CaptureState = $"{name}: Capture not usable";
            return;
        }

        var ttk = context.TimeToKillService?.GetTtkSeconds(target.GameObjectId) ?? float.MaxValue;
        var decision = Decide(ttk, cfg.CaptureLeadSeconds, cfg.CaptureSafetyMarginSeconds);

        context.Debug.CaptureState = decision.Reason(name, ttk);
        if (!decision.Fire)
            return;

        var targetId = target.GameObjectId;
        scheduler.PushGcd(ArtemisAbilities.Capture, targetId, priority: 5, onDispatched: _ =>
        {
            _appliedUtc[targetId] = UtcNow();
            context.Debug.PlannedAction = BSTActions.Capture.Name;
        });
    }

    /// <summary>Whether to go after this enemy at all, and why not.</summary>
    internal readonly record struct TargetCheck(bool Go, string Why);

    /// <summary>
    /// Is this enemy worth a Capture? Pure, so the rules are testable.
    /// <list type="number">
    /// <item>"No pact" this session → no.</item>
    /// <item>The list names a beast the Bestiary already holds → no (the game's own record beats an
    /// old scan).</item>
    /// <item>A Gauge scan exists → it decides, except a "not yet strong enough" scan of a listed
    /// enemy, which the level check below re-decides (you may have levelled since).</item>
    /// <item>Not in the list and never scanned → no: nothing says it is a beast.</item>
    /// <item>Bestiary not loaded → no: can't tell whether you own it.</item>
    /// <item>Enemy above your level → no: Capture needs you at or above it.</item>
    /// </list>
    /// </summary>
    internal static TargetCheck CheckTarget(
        string name, BeastCaptureEntry? scan, bool noPact, BstFamiliar? listed, bool? listedOwned,
        int playerLevel, int targetLevel)
    {
        if (string.IsNullOrWhiteSpace(name))
            return new(false, "no target");
        if (noPact)
            return new(false, DescribeSkip(name, scan, noPact: true));
        if (listed != null && listedOwned == true)
            return new(false, $"{name}: {listed.Name} is already in your Bestiary");

        if (scan != null && !(listed != null && scan.Difficulty == BeastCaptureDifficulty.LevelGated))
        {
            var capturable = scan.Capturable == true && scan.AlreadyCaptured != true
                && scan.Difficulty is not (BeastCaptureDifficulty.Impossible or BeastCaptureDifficulty.LevelGated);
            return capturable
                ? new(true, $"{name}: scanned capturable")
                : new(false, DescribeSkip(name, scan));
        }

        if (listed == null)
            return new(false, $"{name}: not in the capture list and not scanned — use Gauge on it");
        if (listedOwned == null)
            return new(false, $"{name} ({listed.Name}): Bestiary not loaded yet");
        if (targetLevel > playerLevel)
            return new(false, $"{name} ({listed.Name}): level {targetLevel} is above you");
        return new(true, $"{name}: a {listed.Name} you haven't caught");
    }

    /// <summary>The timing decision, pure so it can be tested without a game attached.</summary>
    internal readonly record struct CaptureDecision(bool Fire, string Why)
    {
        public string Reason(string name, float ttk) =>
            ttk >= NoConfidentEstimateSeconds
                ? $"{name}: {Why}"
                : $"{name}: {Why} (ttk {ttk:0.0}s)";
    }

    /// <summary>
    /// Should Capture fire now?
    ///
    /// <para>
    /// <paramref name="leadSeconds"/> is how close to death we want to be — the "as late as
    /// possible" knob. It is clamped to the debuff window minus <paramref name="safetyMargin"/>,
    /// so a lead longer than the debuff can survive is impossible to configure: setting the lead
    /// to 300s cannot make the module apply a 120s debuff 5 minutes before the kill.
    /// </para>
    /// </summary>
    internal static CaptureDecision Decide(float ttkSeconds, float leadSeconds, float safetyMargin)
    {
        if (float.IsNaN(ttkSeconds) || ttkSeconds >= NoConfidentEstimateSeconds)
            return new CaptureDecision(false, "no confident TTK — leaving it to you");

        var margin = Math.Max(0f, safetyMargin);

        // The latest moment that still guarantees coverage: the whole remaining fight has to fit
        // inside the debuff with the margin to spare.
        var latestSafe = BSTActions.CaptureWindowSeconds - margin;
        var lead = Math.Min(Math.Max(0f, leadSeconds), latestSafe);

        if (ttkSeconds > latestSafe)
            return new CaptureDecision(false, "too long to live — debuff would expire first");

        if (ttkSeconds > lead)
            return new CaptureDecision(false, "holding for a later application");

        return new CaptureDecision(true, "firing Capture");
    }

    /// <summary>
    /// Why auto-capture is leaving this target alone, in terms the player can act on. "Not known
    /// capturable" alone would hide the difference between "scan it first" and "you already own it".
    /// </summary>
    internal static string DescribeSkip(string name, BeastCaptureEntry? entry, bool noPact = false)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "no target";
        if (noPact)
            return $"{name}: no pact possible";
        if (entry is null)
            return $"{name}: not scanned yet — use Gauge on it";
        if (entry.AlreadyCaptured == true)
            return $"{name}: already in your Bestiary";
        return entry.Difficulty switch
        {
            BeastCaptureDifficulty.LevelGated => $"{name}: too strong for you yet",
            BeastCaptureDifficulty.Impossible => $"{name}: no pact possible",
            _ when entry.Capturable is null => $"{name}: scan reply not understood yet",
            _ => $"{name}: not capturable",
        };
    }

    /// <summary>Combat end / zone change.</summary>
    public void Reset() => _appliedUtc.Clear();
}
