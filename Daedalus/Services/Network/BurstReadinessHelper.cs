using System;
using System.Collections.Generic;
using Daedalus.Data;

namespace Daedalus.Services.Network;

/// <summary>
/// Decides when this toon announces LAN burst readiness (the readiness pips + the all-ready
/// auto-fire). Wire contract: jobs with a party raid buff are "ready" when that buff is off
/// cooldown in combat; jobs with no raid buff (tanks, WHM/SGE, BLM...) have nothing to align
/// and are ready whenever they are in combat. BLU never reports here — its readiness is the
/// Moon Flute path (<see cref="Daedalus.Services.Blu.BluCoordinationState.SignalBurstReady"/>),
/// which was previously the ONLY caller of BroadcastBurstReady (why every pip sat red on
/// non-BLU comps and the auto-fire could never trigger).
/// </summary>
public static class BurstReadinessHelper
{
    /// <summary>Each job's party raid buff — the 2-minute cooldown worth aligning on.</summary>
    private static readonly Dictionary<uint, uint> RaidBuffByJob = new()
    {
        { JobRegistry.Dragoon, DRGActions.BattleLitany.ActionId },
        { JobRegistry.Bard, BRDActions.BattleVoice.ActionId },
        { JobRegistry.Summoner, SMNActions.SearingLight.ActionId },
        { JobRegistry.RedMage, RDMActions.Embolden.ActionId },
        { JobRegistry.Dancer, DNCActions.TechnicalFinish.ActionId },
        { JobRegistry.Reaper, RPRActions.ArcaneCircle.ActionId },
        { JobRegistry.Monk, MNKActions.Brotherhood.ActionId },
        { JobRegistry.Pictomancer, PCTActions.StarryMuse.ActionId },
        { JobRegistry.Samurai, SAMActions.Ikishoten.ActionId },
        { JobRegistry.Ninja, NINActions.KunaisBane.ActionId },
        { JobRegistry.Viper, VPRActions.SerpentsIre.ActionId },
        { JobRegistry.Machinist, MCHActions.Wildfire.ActionId },
        { JobRegistry.Astrologian, ASTActions.Divination.ActionId },
        { JobRegistry.Scholar, SCHActions.ChainStratagem.ActionId },
    };

    /// <summary>
    /// True when the job brings something the auto-fire should ALIGN: a party raid buff, or
    /// BLU's synced Moon Flute. Groups with none of these (e.g. WAR + WHM only) never auto-fire —
    /// their members are permanently "ready", which would reopen the window on a timer for no
    /// gain; the Force button still works there.
    /// </summary>
    public static bool HasAlignableRaidBuff(uint jobId) =>
        jobId == JobRegistry.BlueMage || RaidBuffByJob.ContainsKey(jobId);

    /// <summary>
    /// Should this toon report burst-ready right now? Called on the ~2s heartbeat cadence.
    /// </summary>
    public static bool IsBurstReady(
        uint jobId,
        bool inCombat,
        Func<uint, bool> isActionReady,
        Func<uint, bool> isActionLearned)
    {
        if (!inCombat)
            return false;

        // BLU readiness is owned by the Moon Flute coordination path — never double-report.
        if (jobId == JobRegistry.BlueMage)
            return false;

        if (!RaidBuffByJob.TryGetValue(jobId, out var raidBuffId))
            return true; // no raid buff — nothing to wait on

        if (!isActionLearned(raidBuffId))
            return true; // below the unlock level — nothing to align yet

        return isActionReady(raidBuffId);
    }
}
