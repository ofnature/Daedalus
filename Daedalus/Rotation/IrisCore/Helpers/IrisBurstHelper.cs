using Daedalus.Config.DPS;
using Daedalus.Data;
using Daedalus.Rotation.Common.Helpers;
using Daedalus.Rotation.IrisCore.Context;
using Daedalus.Services;
using Daedalus.Services.Action;

namespace Daedalus.Rotation.IrisCore.Helpers;

/// <summary>
/// Burst pooling and portrait prep rules (RSR / pct-rotation.md parity).
/// </summary>
public static class IrisBurstHelper
{
    public static bool IsPoolingEnabled(PictomancerConfig cfg) => cfg.EnableBurstPooling;

    public static bool IsInBurst(IIrisContext context, IBurstWindowService? burstWindowService) =>
        context.IsInBurstWindow || context.HasStarryMuse || BurstHoldHelper.IsInBurst(burstWindowService);

    /// <summary>
    /// Hold Mog of the Ages when Starry Muse burst is imminent but not yet active.
    /// </summary>
    public static bool ShouldHoldMogPortrait(IIrisContext context, IBurstWindowService? burstWindowService)
    {
        if (!IsPoolingEnabled(context.Configuration.Pictomancer)) return false;
        if (IsInBurst(context, burstWindowService)) return false;
        if (context.MadeenReady) return false;
        return BurstHoldHelper.ShouldHoldForBurst(burstWindowService, 8f);
    }

    /// <summary>
    /// Hold Striking Muse when Starry Muse will be available within 60s (RSR burstTimingCheckerStriking).
    /// </summary>
    public static bool ShouldHoldStrikingMuse(
        IIrisContext context,
        IActionService actionService,
        IBurstWindowService? burstWindowService)
    {
        if (!IsPoolingEnabled(context.Configuration.Pictomancer)) return false;
        if (context.HasStarryMuse) return false;
        if (context.Player.Level < PCTActions.StarryMuse.MinLevel) return false;
        if (context.StarryMuseReady) return false;

        if (BurstHoldHelper.IsInBurst(burstWindowService)) return false;

        // Read the cooldown on the base gauge action (Scenic Muse), not the morphed Starry Muse id.
        var starryCd = actionService.GetCooldownRemaining(PCTActions.ScenicMuse.ActionId);
        return starryCd > 0f && starryCd <= 60f;
    }

    /// <summary>
    /// Hold motif repaints when a burst window is expected within 10s (IPC / burn prep).
    /// Inspiration is part of the burst window — do not block those paints.
    /// </summary>
    public static bool ShouldHoldRepaint(IIrisContext context, IBurstWindowService? burstWindowService)
    {
        if (!IsPoolingEnabled(context.Configuration.Pictomancer)) return false;
        if (context.HasInspiration || IsInBurst(context, burstWindowService)) return false;
        return BurstHoldHelper.ShouldHoldForBurst(burstWindowService, 10f);
    }

    /// <summary>
    /// Hold starting Hammer Stamp when burst is imminent, or when Starry Muse is within 30s outside burst.
    /// </summary>
    public static bool ShouldHoldHammerStart(
        IIrisContext context,
        IActionService actionService,
        IBurstWindowService? burstWindowService)
    {
        if (!IsPoolingEnabled(context.Configuration.Pictomancer)) return false;
        if (context.HammerComboStep > 0) return false;
        if (IsInBurst(context, burstWindowService)) return false;

        if (BurstHoldHelper.ShouldHoldForBurst(burstWindowService, 8f))
            return true;

        if (context.Player.Level < PCTActions.StarryMuse.MinLevel)
            return false;

        if (context.HasStarryMuse)
            return false;

        // Read the cooldown on the base gauge action (Scenic Muse), not the morphed Starry Muse id.
        var starryCd = actionService.GetCooldownRemaining(PCTActions.ScenicMuse.ActionId);
        return starryCd > 0f && starryCd <= 30f && !context.StarryMuseReady;
    }

    public static bool IsLivingMuseInBurst(IIrisContext context) =>
        context.HasStarryMuse || context.HasSubtractiveSpectrum || context.IsInBurstWindow;
}
