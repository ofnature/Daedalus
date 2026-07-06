using Daedalus.Data;
using Daedalus.Rotation.HermesCore.Context;

namespace Daedalus.Rotation.HermesCore.Helpers;

/// <summary>
/// RSR CustomRotation_GCD NIN mudra gate: status 496 + GcdRemaining >= 0.625s,
/// plus reserve open GCDs for mudra/ninjutsu steps (NinjutsuModule runs before DamageModule).
/// </summary>
internal static class HermesMudraGate
{
    public const float RsrGcdRemainingThresholdSeconds = 0.625f;

    public static bool ShouldBlockComboGcds(IHermesContext context)
    {
        // Rabbit must be cleared before combo — Death Blossom between Chi and Doton causes failed sequences.
        if (HermesNinjutsuMudraExecutor.IsRabbitFailureSlot(context))
            return true;

        if (!context.MudraHelper.IsSequenceActive)
        {
            // Orphaned live signs: the module-side sequence aborted (stuck counter / slot desync),
            // but the GAME still holds registered signs — mudra status up and the ninjutsu slot
            // showing a pending chain. Any weaponskill now invalidates that chain into Rabbit
            // Medium (Lv60 field logs: Death Blossom 1.0s after an orphaned Ten; Throwing Dagger
            // over a completed Ten-Chi-Jin pending Suiton). Hold combo GCDs until
            // TryResumeOrphanedMudraSlot re-adopts the chain or the signs expire.
            if (context.HasGameMudraStatus
                && !HermesNinjutsuSlotProbe.IsNoActiveNinjutsu(
                    HermesNinjutsuSlotProbe.GetEffectiveSlotId(context.ActionService)))
                return true;

            return false;
        }

        var aim = context.MudraHelper.TargetNinjutsu;
        if (aim == NINActions.NinjutsuType.None)
            return false;

        if (ShouldBlockComboGcds(context.HasGameMudraStatus, context.ActionService.GcdRemaining))
        {
            // Status 496 alone must not idle the rotation during Ten recast — only block when a step is imminent.
            if (HermesNinjutsuMudraExecutor.IsWaitingForSlotAcknowledge(context, aim)
                || HermesNinjutsuMudraExecutor.IsPendingNinjutsuFinishStep(context, aim)
                || HermesNinjutsuMudraExecutor.WillConsumeOpenGcdForMudraStep(context, aim))
                return true;
        }

        // Mid-sequence: combo GCDs between mudra presses desync into Rabbit Medium.
        if (context.MudraHelper.MudraCount > 0)
            return true;

        if (HermesNinjutsuMudraExecutor.IsWaitingForSlotAcknowledge(context, aim))
            return true;

        // Slot shows Doton/Katon/etc. but finish cast not ready yet — reserve GCD until it fires.
        if (HermesNinjutsuMudraExecutor.IsPendingNinjutsuFinishStep(context, aim))
            return true;

        // Sequence queued but first mudra on charge CD — NinjutsuModule aborts; allow combo filler.
        if (HermesNinjutsuMudraExecutor.IsFirstMudraBlockedOnCharge(context, aim))
            return false;

        // Ten/mudra ready on this GCD — combo must not steal the window (phantom Ten spam in action feed).
        return HermesNinjutsuMudraExecutor.WillConsumeOpenGcdForMudraStep(context, aim);
    }

    public static bool ShouldBlockComboGcds(bool hasGameMudraStatus, float gcdRemainingSeconds)
        => hasGameMudraStatus && gcdRemainingSeconds >= RsrGcdRemainingThresholdSeconds;
}
