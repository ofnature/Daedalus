namespace Daedalus.Services.Prediction;

/// <summary>
/// BossModReborn timeline IPC reader for the overlay mechanic forecast.
/// Fallback source for content without an embedded Cactbot timeline — covers any duty
/// BMR has a module for. Fail-open: everything reads as "nothing upcoming" when BMR
/// is not installed or has no active module.
/// </summary>
public interface IBossModForecastService
{
    /// <summary>BossModReborn plugin is installed and loaded.</summary>
    bool IsAvailable { get; }

    /// <summary><c>BossMod.HasActiveModule</c> — a boss module is running with an active state.</summary>
    bool HasActiveModule { get; }

    /// <summary><c>BossMod.ActiveModuleName</c> — primary actor name of the active module, or null.</summary>
    string? ActiveModuleName { get; }

    /// <summary><c>BossMod.Timeline.NextRaidwideIn</c> in seconds, or <see cref="float.MaxValue"/> when none/unavailable.</summary>
    float NextRaidwideInSeconds { get; }

    /// <summary><c>BossMod.Timeline.NextTankbusterIn</c> in seconds, or <see cref="float.MaxValue"/> when none/unavailable.</summary>
    float NextTankbusterInSeconds { get; }

    /// <summary>
    /// <c>BossMod.Hints.NextTankbusterDamageIn</c> in seconds, or <see cref="float.MaxValue"/> when
    /// none/unavailable. Live AIHints prediction (BMR scans PredictedDamage for a Tankbuster-typed
    /// entry) — unlike <see cref="NextTankbusterInSeconds"/> this covers any active module, not just
    /// ones with an embedded timeline. Drives the coordinated tank-swap auto trigger.
    /// </summary>
    float NextTankbusterDamageInSeconds { get; }
}
