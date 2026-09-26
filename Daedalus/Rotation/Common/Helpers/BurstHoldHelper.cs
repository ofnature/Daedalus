using Daedalus.Services;
using Daedalus.Services.Input;
using Daedalus.Timeline;
using Daedalus.Timeline.Models;

namespace Daedalus.Rotation.Common.Helpers;

/// <summary>
/// Shared burst window helpers for BuffModules.
/// Centralizes the ShouldHoldForBurst / IsInBurst logic so that all 21 rotation
/// BuffModules use an identical implementation instead of private copy-pasted methods.
/// </summary>
public static class BurstHoldHelper
{
    /// <summary>
    /// Player-intent override for all burst-window decisions. Set once at plugin
    /// startup. Static accessor used here because the override is a global player
    /// signal; threading <see cref="IModifierKeyService"/> through ~30 call sites
    /// would be churn for no architectural gain.
    /// </summary>
    public static IModifierKeyService? ModifierKeys { get; set; }

    /// <summary>
    /// True when raid buff burst window is currently active.
    /// When the burst-override key is held, returns true regardless of real state
    /// (rotation should act as if in burst). When the conservative key is held,
    /// returns false (rotation should act as if not in burst).
    /// </summary>
    public static bool IsInBurst(IBurstWindowService? burstWindowService)
        => IsInBurstWith(ModifierKeys, burstWindowService);

    /// <summary>
    /// <see cref="IsInBurst"/> with the modifier service passed in. Tests use this instead of setting
    /// <see cref="ModifierKeys"/>: that static is process-wide, and xUnit runs test classes in parallel,
    /// so a test that set it to "conservative" made unrelated rotation tests hold their burst at random
    /// (the Red Mage Embolden test failed about one full run in six, 2026-09-25).
    /// </summary>
    internal static bool IsInBurstWith(IModifierKeyService? modifierKeys, IBurstWindowService? burstWindowService)
    {
        if (modifierKeys?.IsBurstOverride == true) return true;
        if (modifierKeys?.IsConservativeOverride == true) return false;
        return burstWindowService?.IsInBurstWindow == true;
    }

    /// <summary>
    /// True when burst is imminent within <paramref name="thresholdSeconds"/> and not yet active.
    /// Use to hold cooldowns/gauge spenders until the burst window opens.
    /// When the burst-override key is held, returns false (don't hold, fire now).
    /// When the conservative key is held, returns true (hold regardless of detected burst).
    /// </summary>
    public static bool ShouldHoldForBurst(IBurstWindowService? burstWindowService, float thresholdSeconds = 8f)
        => ShouldHoldForBurstWith(ModifierKeys, burstWindowService, thresholdSeconds);

    /// <summary><see cref="ShouldHoldForBurst"/> with the modifier service passed in (see <see cref="IsInBurstWith"/>).</summary>
    internal static bool ShouldHoldForBurstWith(
        IModifierKeyService? modifierKeys, IBurstWindowService? burstWindowService, float thresholdSeconds = 8f)
    {
        if (modifierKeys?.IsBurstOverride == true) return false;
        if (modifierKeys?.IsConservativeOverride == true) return true;
        return burstWindowService?.IsBurstImminent(thresholdSeconds) == true &&
               burstWindowService?.IsInBurstWindow != true;
    }

    /// <summary>
    /// Checks if burst abilities should be held for an imminent phase transition.
    /// Returns true if a high-confidence phase transition is expected within the window.
    /// Modifier overrides apply: burst-override forces false, conservative forces true.
    /// </summary>
    public static bool ShouldHoldForPhaseTransition(ITimelineService? timelineService, float windowSeconds = 8f)
        => ShouldHoldForPhaseTransitionWith(ModifierKeys, timelineService, windowSeconds);

    /// <summary><see cref="ShouldHoldForPhaseTransition"/> with the modifier service passed in (see <see cref="IsInBurstWith"/>).</summary>
    internal static bool ShouldHoldForPhaseTransitionWith(
        IModifierKeyService? modifierKeys, ITimelineService? timelineService, float windowSeconds = 8f)
    {
        if (modifierKeys?.IsBurstOverride == true) return false;
        if (modifierKeys?.IsConservativeOverride == true) return true;
        var nextPhase = timelineService?.GetNextMechanic(TimelineEntryType.Phase);
        if (nextPhase?.IsSoon != true || !nextPhase.Value.IsHighConfidence)
            return false;
        return nextPhase.Value.SecondsUntil <= windowSeconds;
    }
}
