using Daedalus.Data;
using Daedalus.Models.Action;
using Daedalus.Services.Action;

namespace Daedalus.Rotation.IrisCore.Helpers;

/// <summary>
/// Button-replacement probes for Pictomancer (RSR GetAdjustedActionId parity).
/// </summary>
public static class IrisActionProbes
{
    public static bool IsAdjustedTo(IActionService actionService, uint baseActionId, uint expectedActionId)
        => actionService.GetAdjustedActionId(baseActionId) == expectedActionId;

    public static bool IsHammerBrushReady(IActionService actionService)
        => IsAdjustedTo(actionService, PCTActions.HammerStamp.ActionId, PCTActions.HammerBrush.ActionId);

    public static bool IsPolishingHammerReady(IActionService actionService)
        => IsAdjustedTo(actionService, PCTActions.HammerStamp.ActionId, PCTActions.PolishingHammer.ActionId);

    public static bool IsHammerMotifReady(IActionService actionService)
        => IsAdjustedTo(actionService, PCTActions.WeaponMotif.ActionId, PCTActions.HammerMotif.ActionId);

    public static bool IsStarrySkyMotifReady(IActionService actionService)
        => IsAdjustedTo(actionService, PCTActions.LandscapeMotif.ActionId, PCTActions.StarrySkyMotif.ActionId);

    public static bool IsStarryMuseReady(IActionService actionService)
        => IsAdjustedTo(actionService, PCTActions.ScenicMuse.ActionId, PCTActions.StarryMuse.ActionId);

    public static bool IsStrikingMuseReady(IActionService actionService)
        => IsAdjustedTo(actionService, PCTActions.SteelMuse.ActionId, PCTActions.StrikingMuse.ActionId);

    public static bool IsPomMotifReady(IActionService actionService)
        => IsAdjustedTo(actionService, PCTActions.CreatureMotif.ActionId, PCTActions.PomMotif.ActionId);

    public static bool IsWingMotifReady(IActionService actionService)
        => IsAdjustedTo(actionService, PCTActions.CreatureMotif.ActionId, PCTActions.WingMotif.ActionId);

    public static bool IsClawMotifReady(IActionService actionService)
        => IsAdjustedTo(actionService, PCTActions.CreatureMotif.ActionId, PCTActions.ClawMotif.ActionId);

    public static bool IsMawMotifReady(IActionService actionService)
        => IsAdjustedTo(actionService, PCTActions.CreatureMotif.ActionId, PCTActions.MawMotif.ActionId);

    public static bool IsPomMuseReady(IActionService actionService)
        => IsAdjustedTo(actionService, PCTActions.LivingMuse.ActionId, PCTActions.PomMuse.ActionId);

    public static bool IsWingedMuseReady(IActionService actionService)
        => IsAdjustedTo(actionService, PCTActions.LivingMuse.ActionId, PCTActions.WingedMuse.ActionId);

    public static bool IsClawedMuseReady(IActionService actionService)
        => IsAdjustedTo(actionService, PCTActions.LivingMuse.ActionId, PCTActions.ClawedMuse.ActionId);

    public static bool IsFangedMuseReady(IActionService actionService)
        => IsAdjustedTo(actionService, PCTActions.LivingMuse.ActionId, PCTActions.FangedMuse.ActionId);

    /// <summary>
    /// Resolves the creature motif the game slot currently offers, with charge-order fallback.
    /// </summary>
    public static ActionDefinition GetNextCreatureMotif(IActionService actionService, byte level, int livingMuseCharges)
    {
        if (level >= PCTActions.ClawMotif.MinLevel)
        {
            if (IsClawMotifReady(actionService)) return PCTActions.ClawMotif;
            if (IsMawMotifReady(actionService)) return PCTActions.MawMotif;
        }

        if (IsPomMotifReady(actionService)) return PCTActions.PomMotif;
        if (IsWingMotifReady(actionService)) return PCTActions.WingMotif;

        return PCTActions.GetCreatureMotif(level, livingMuseCharges, actionService);
    }

    public static bool IsLivingMuseReadyForCreature(IActionService actionService, PCTActions.CreatureMotifType creatureType)
    {
        return creatureType switch
        {
            PCTActions.CreatureMotifType.Pom => IsPomMuseReady(actionService),
            PCTActions.CreatureMotifType.Wing => IsWingedMuseReady(actionService),
            PCTActions.CreatureMotifType.Claw => IsClawedMuseReady(actionService),
            PCTActions.CreatureMotifType.Maw => IsFangedMuseReady(actionService),
            _ => IsAdjustedTo(actionService, PCTActions.LivingMuse.ActionId, PCTActions.LivingMuse.ActionId),
        };
    }

    /// <summary>
    /// Resolves base/subtractive combo step from action-manager slot replacement (RSR parity).
    /// </summary>
    public static void ResolveBaseComboStep(
        IActionService actionService,
        bool shouldUseAoe,
        byte level,
        bool useSubtractiveRoute,
        out int baseStep,
        out bool isSubtractive)
    {
        baseStep = 0;
        isSubtractive = false;

        if (useSubtractiveRoute && level >= PCTActions.BlizzardInCyan.MinLevel)
        {
            isSubtractive = true;

            if (shouldUseAoe && level >= PCTActions.Blizzard2InCyan.MinLevel)
            {
                // Only the combo STARTER (Blizzard II in Cyan) morphs via GetAdjustedActionId — the mid/end
                // steps report themselves. Probe the starter for every step, matching the additive resolver
                // above and the AdjustedActionProbe on these behaviors. Probing Stone II for step 2 never
                // matched, so the Thunder II step fell back to the starter and stalled on its probe.
                if (IsAdjustedTo(actionService, PCTActions.Blizzard2InCyan.ActionId, PCTActions.Thunder2InMagenta.ActionId))
                {
                    baseStep = 2;
                    return;
                }

                if (IsAdjustedTo(actionService, PCTActions.Blizzard2InCyan.ActionId, PCTActions.Stone2InYellow.ActionId))
                {
                    baseStep = 1;
                    return;
                }

                return;
            }

            if (IsAdjustedTo(actionService, PCTActions.BlizzardInCyan.ActionId, PCTActions.ThunderInMagenta.ActionId))
            {
                baseStep = 2;
                return;
            }

            if (IsAdjustedTo(actionService, PCTActions.BlizzardInCyan.ActionId, PCTActions.StoneInYellow.ActionId))
            {
                baseStep = 1;
                return;
            }

            return;
        }

        if (shouldUseAoe && level >= PCTActions.Fire2InRed.MinLevel)
        {
            if (IsAdjustedTo(actionService, PCTActions.Fire2InRed.ActionId, PCTActions.Water2InBlue.ActionId))
            {
                baseStep = 2;
                return;
            }

            if (IsAdjustedTo(actionService, PCTActions.Fire2InRed.ActionId, PCTActions.Aero2InGreen.ActionId))
            {
                baseStep = 1;
                return;
            }

            return;
        }

        if (IsAdjustedTo(actionService, PCTActions.FireInRed.ActionId, PCTActions.WaterInBlue.ActionId))
        {
            baseStep = 2;
            return;
        }

        if (IsAdjustedTo(actionService, PCTActions.FireInRed.ActionId, PCTActions.AeroInGreen.ActionId))
        {
            baseStep = 1;
        }
    }

    public static int ResolveHammerComboStep(IActionService actionService, int comboHammerStep, bool hasHammerTime, int hammerTimeStacks)
    {
        if (IsPolishingHammerReady(actionService))
            return 2;

        if (IsHammerBrushReady(actionService))
            return 1;

        if (comboHammerStep > 0)
            return comboHammerStep;

        if (hasHammerTime && hammerTimeStacks >= 3)
            return 0;

        return comboHammerStep;
    }

    public static bool CanStartHammerStamp(IActionService actionService, bool hasHammerTime, int hammerTimeStacks, int hammerComboStep)
    {
        if (hammerComboStep > 0)
            return true;

        return hasHammerTime && hammerTimeStacks >= 3
               && IsAdjustedTo(actionService, PCTActions.HammerStamp.ActionId, PCTActions.HammerStamp.ActionId);
    }
}
