using Daedalus.Data;
using Daedalus.Rotation;
using Daedalus.Services.Positional;
using Xunit;

namespace Daedalus.Tests.Rotation.EchidnaCore;

/// <summary>
/// VPR next-positional map — LIVE-SHEET facts (2026-07-20 correction: the finisher family HAS
/// positionals on live — Flanksting/Flanksbane FLANK, Hindsting/Hindsbane REAR 340→400 — the
/// "lost in 7.05" note came from the stale RSR snapshot; coils were always Hunter's FLANK /
/// Swiftskin's REAR). The twinblade (dread) chain outranks the dual-wield finisher step,
/// mirroring TryPushTwinbladeCombo's push order.
/// </summary>
public class EchidnaNextPositionalTests
{
    [Theory]
    [InlineData(VPRActions.DreadCombo.DreadwindyReady)]
    [InlineData(VPRActions.DreadCombo.HunterCoilReady)]
    public void CoilChain_AnticipatesFlank_ForHuntersCoil(VPRActions.DreadCombo state)
    {
        Assert.Equal(PositionalType.Flank,
            Echidna.ComputeNextPositional(state, lastComboAction: 0, hasRearVenom: false, hasFlankVenom: false));
    }

    [Fact]
    public void CoilChain_AnticipatesRear_ForSwiftskinsCoil()
    {
        Assert.Equal(PositionalType.Rear,
            Echidna.ComputeNextPositional(VPRActions.DreadCombo.SwiftskinCoilReady, 0, false, false));
    }

    [Fact]
    public void CoilChain_OutranksVenomStep()
    {
        // Mid-coil with a rear venom banked: the coil arc (flank) wins — the module pushes the
        // coil GCD before the dual-wield finisher.
        Assert.Equal(PositionalType.Flank,
            Echidna.ComputeNextPositional(VPRActions.DreadCombo.HunterCoilReady, 34608, hasRearVenom: true, hasFlankVenom: false));
    }

    [Fact]
    public void FinisherStep_VenomDecidesArc()
    {
        // After Hunter's/Swiftskin's Sting (34608/34609) the banked venom picks the finisher arc.
        Assert.Equal(PositionalType.Rear,
            Echidna.ComputeNextPositional(VPRActions.DreadCombo.None, 34608, hasRearVenom: true, hasFlankVenom: false));
        Assert.Equal(PositionalType.Flank,
            Echidna.ComputeNextPositional(VPRActions.DreadCombo.None, 34609, hasRearVenom: false, hasFlankVenom: true));
    }

    [Fact]
    public void FinisherStep_NoVenom_DefaultsFlank()
    {
        Assert.Equal(PositionalType.Flank,
            Echidna.ComputeNextPositional(VPRActions.DreadCombo.None, 34608, false, false));
    }

    [Fact]
    public void OtherComboSteps_NoAnticipation()
    {
        Assert.Null(Echidna.ComputeNextPositional(VPRActions.DreadCombo.None, 0, false, false));
        Assert.Null(Echidna.ComputeNextPositional(VPRActions.DreadCombo.None, 34606, true, true)); // Steel Fangs
    }
}
