using System;
using Daedalus.Data;
using Daedalus.Models.Action;

namespace Daedalus.Rotation.HermesCore.Helpers;

internal readonly record struct TenChiJinStep(
    uint MudraButtonActionId,
    uint AdjustedNinjutsuId,
    ActionDefinition DisplayAction,
    string DebugName);

/// <summary>
/// RSR DoTenChiJin — AdjustId(Ten/Chi/Jin) slot probes + IsLastAction de-dupe.
/// </summary>
internal static class HermesTenChiJinHelper
{
    public const float BuffDurationSeconds = 6f;

    public static bool TryGetNextStep(
        uint tenAdjusted,
        uint chiAdjusted,
        uint jinAdjusted,
        int enemyCount,
        int aoeMinTargets,
        bool hasDotonActive,
        Func<uint, bool> wasLastAction,
        out TenChiJinStep step)
    {
        step = default;

        if (tenAdjusted == NINActions.TenChiJinAdjusted.FumaShurikenSt
            && !WasLastAny(wasLastAction, NINActions.TenChiJinAdjusted.FumaShurikenSt,
                NINActions.TenChiJinAdjusted.FumaShurikenAoE))
        {
            // The AoE sequence's third press can ONLY be Doton (Jin→Ten→Chi — no other button left).
            // If Doton is already ticking, entering the AoE sequence guarantees either a wasted
            // refresh or a stall at step 3 — take the ST sequence (Fuma→Raiton→Suiton) instead.
            // (Validated log 2026-07-01: TCJ stalled 4.7s at step 3 behind an active Doton.)
            if (enemyCount >= aoeMinTargets && !hasDotonActive)
            {
                step = new TenChiJinStep(
                    NINActions.Ten.ActionId,
                    NINActions.TenChiJinAdjusted.FumaShurikenAoE,
                    NINActions.FumaShuriken,
                    "TCJ: Fuma Shuriken (AoE)");
                return true;
            }

            step = new TenChiJinStep(
                NINActions.Ten.ActionId,
                NINActions.TenChiJinAdjusted.FumaShurikenSt,
                NINActions.FumaShuriken,
                "TCJ: Fuma Shuriken");
            return true;
        }

        if (tenAdjusted == NINActions.TenChiJinAdjusted.Katon
            && !wasLastAction(NINActions.TenChiJinAdjusted.Katon))
        {
            step = new TenChiJinStep(
                NINActions.Ten.ActionId,
                NINActions.TenChiJinAdjusted.Katon,
                NINActions.Katon,
                "TCJ: Katon");
            return true;
        }

        if (chiAdjusted == NINActions.TenChiJinAdjusted.Raiton
            && !wasLastAction(NINActions.TenChiJinAdjusted.Raiton))
        {
            step = new TenChiJinStep(
                NINActions.Chi.ActionId,
                NINActions.TenChiJinAdjusted.Raiton,
                NINActions.Raiton,
                "TCJ: Raiton");
            return true;
        }

        if (jinAdjusted == NINActions.TenChiJinAdjusted.Suiton
            && !wasLastAction(NINActions.TenChiJinAdjusted.Suiton))
        {
            step = new TenChiJinStep(
                NINActions.Jin.ActionId,
                NINActions.TenChiJinAdjusted.Suiton,
                NINActions.Suiton,
                "TCJ: Suiton");
            return true;
        }

        // NO hasDotonActive gate here: mid-sequence, Doton is the ONLY remaining press — refusing it
        // stalls until the 6s TCJ buff expires (the 4.7s hole in the validation log). A refresh of an
        // active Doton is a minor snapshot waste; a stall is a lost cast AND lost uptime. Doton-active
        // avoidance belongs at sequence ENTRY (see the Fuma step above).
        if (chiAdjusted == NINActions.TenChiJinAdjusted.Doton
            && !wasLastAction(NINActions.TenChiJinAdjusted.Doton))
        {
            step = new TenChiJinStep(
                NINActions.Chi.ActionId,
                NINActions.TenChiJinAdjusted.Doton,
                NINActions.Doton,
                "TCJ: Doton");
            return true;
        }

        return false;
    }

    private static bool WasLastAny(Func<uint, bool> wasLastAction, params uint[] ids)
    {
        foreach (var id in ids)
        {
            if (wasLastAction(id))
                return true;
        }

        return false;
    }
}
