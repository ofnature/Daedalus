using System.Collections.Generic;

namespace Daedalus.Services.Analytics;

/// <summary>
/// Full-party DPS parser fed by the ActionEffect hook. Tracks every friendly combatant —
/// the local player, other players, Trust/support NPCs, with pet damage merged into owners.
/// </summary>
public interface IDpsMeterService
{
    /// <summary>The live encounter, or null when out of combat.</summary>
    DpsEncounter? Current { get; }

    /// <summary>Ended encounters, newest first. Capped at <see cref="Config.ParserConfig.FightHistoryCount"/>.</summary>
    IReadOnlyList<DpsEncounter> History { get; }

    /// <summary>
    /// One-line DoT-tick pipeline status for the parser tooltip: whether the ActorControl hook is
    /// live and how many packets (total / HoT-DoT) it has seen this session. Distinguishes
    /// "hook never installed" from "hook alive but no tick category arriving".
    /// </summary>
    string DescribeTickPipeline();

    /// <summary>Per-frame update: encounter segmentation + draining queued damage events.</summary>
    void Update();

    /// <summary>Clears the live encounter and all history.</summary>
    void Reset();
}
