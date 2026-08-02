using System.Numerics;
using Daedalus.Config;
using Daedalus.Services.Occult;
using Xunit;

namespace Daedalus.Tests.Services.Occult;

/// <summary>
/// The hunt tracker turns elixir chat lines into bearings. Exercised through HandleMessage so
/// the logic is testable without a game session — the live path only supplies the player's
/// position and the message text.
/// </summary>
public sealed class PotTreasureHuntTests
{
    private static PotTreasureHunt Hunt(PhantomConfig? config = null)
        => new(null, null, null, config ?? new PhantomConfig());

    [Fact]
    public void HandleMessage_TurnsAReadingIntoABearing()
    {
        var hunt = Hunt();

        hunt.HandleMessage("You sense something far to the northeast.", new Vector3(10, 0, 20));

        var bearing = Assert.Single(hunt.Bearings);
        Assert.Equal(ElixirProximity.Far, bearing.Proximity);
        Assert.Equal(new Vector3(10, 0, 20), bearing.Origin);
    }

    [Fact]
    public void HandleMessage_AccumulatesReadingsInOrder()
    {
        var hunt = Hunt();

        hunt.HandleMessage("You sense something far, far to the north.", Vector3.Zero);
        hunt.HandleMessage("You sense something to the east.", new Vector3(0, 0, 50));

        Assert.Equal(2, hunt.Bearings.Count);
        Assert.Equal(ElixirProximity.VeryFar, hunt.Bearings[0].Proximity);
        Assert.Equal(ElixirProximity.Within, hunt.Bearings[1].Proximity);
    }

    /// <summary>The tracker is fed the whole chat stream, so unrelated lines must not accumulate.</summary>
    [Theory]
    [InlineData("You obtain 10 Enlightenment silver obols.")]
    [InlineData("Seraph: where?")]
    [InlineData("")]
    [InlineData(null)]
    public void HandleMessage_IgnoresChatThatIsNotAReading(string? text)
    {
        var hunt = Hunt();

        hunt.HandleMessage(text, Vector3.Zero);

        Assert.Empty(hunt.Bearings);
    }

    /// <summary>
    /// Back-to-back hunts must not share bearings — a stale reading from the previous coffer
    /// would drag the overlap somewhere the new one cannot be.
    /// </summary>
    [Fact]
    public void HandleMessage_DiscoveryClearsTheReadings()
    {
        var hunt = Hunt();
        hunt.HandleMessage("You sense something to the south.", Vector3.Zero);

        hunt.HandleMessage("You discover a treasure coffer!", Vector3.Zero);

        Assert.Empty(hunt.Bearings);
    }

    [Fact]
    public void Reset_DropsEverything()
    {
        var hunt = Hunt();
        hunt.HandleMessage("You sense something to the south.", Vector3.Zero);

        hunt.Reset();

        Assert.Empty(hunt.Bearings);
        Assert.False(hunt.IsHunting);
    }

    // ── Activation radius ──

    [Fact]
    public void MaxObservedActivationRadius_IsNullBeforeAnyHuntCompletes()
    {
        Assert.Null(Hunt().MaxObservedActivationRadius);
    }

    /// <summary>
    /// Max, not average: each sample is taken on the tick we first notice the coffer, by which
    /// point the player may already be inside the true trigger boundary, so every reading
    /// understates the radius. The largest is nearest the truth.
    /// </summary>
    [Fact]
    public void MaxObservedActivationRadius_TakesTheLargestSample()
    {
        var config = new PhantomConfig();
        config.ActivationRadiusSamples.AddRange([3.1f, 5.4f, 2.2f]);

        Assert.Equal(5.4f, Hunt(config).MaxObservedActivationRadius!.Value, precision: 3);
    }

    // ── Solving the arc from data ──

    /// <summary>
    /// The arc stops being a guess: each reading measured against the find gives the angle it was
    /// off by, and the arc must be at least as wide as the worst one.
    /// </summary>
    [Fact]
    public void MaxObservedAngularError_TakesTheWorstReading()
    {
        var config = new PhantomConfig();
        config.PotHuntCalibration.AddRange(
        [
            new PotHuntCalibrationSample { Band = "Far", AngularErrorRadians = 0.10f },
            new PotHuntCalibrationSample { Band = "Within", AngularErrorRadians = 0.38f },
            new PotHuntCalibrationSample { Band = "VeryFar", AngularErrorRadians = 0.21f },
        ]);

        var hunt = Hunt(config);

        Assert.Equal(0.38f, hunt.MaxObservedAngularErrorRadians!.Value, precision: 3);
        Assert.Equal(21.77f, hunt.MaxObservedAngularErrorDegrees!.Value, precision: 1);
    }

    [Fact]
    public void MaxObservedAngularError_IsNullUntilAHuntCompletes()
    {
        Assert.Null(Hunt().MaxObservedAngularErrorRadians);
        Assert.Null(Hunt().MaxObservedAngularErrorDegrees);
    }

    // ── Suggested arc ──

    private static PhantomConfig ConfigWithSamples(int count, float worstErrorRadians)
    {
        var config = new PhantomConfig();
        for (var i = 0; i < count; i++)
        {
            config.PotHuntCalibration.Add(new PotHuntCalibrationSample
            {
                Band = "Within",
                AngularErrorRadians = i == 0 ? worstErrorRadians : worstErrorRadians * 0.5f,
            });
        }

        return config;
    }

    /// <summary>
    /// The observed maximum is a lower bound that only grows. Tightening to it after a handful
    /// of hunts means the next genuinely wider reading is silently excluded — so hold at the
    /// default until there is real evidence.
    /// </summary>
    [Fact]
    public void SuggestedHalfAngle_HoldsAtTheDefaultUntilEnoughSamples()
    {
        var hunt = Hunt(ConfigWithSamples(PotTreasureHunt.MinCalibrationSamples - 1, 0.20f));

        Assert.Equal(PotTreasureTriangulation.DefaultHalfAngleRadians, hunt.SuggestedHalfAngleRadians, precision: 5);
        Assert.False(hunt.IsArcCalibrated);
    }

    [Fact]
    public void SuggestedHalfAngle_TightensToTheWorstReadingPlusAMargin()
    {
        var hunt = Hunt(ConfigWithSamples(PotTreasureHunt.MinCalibrationSamples, 0.20f));

        Assert.True(hunt.IsArcCalibrated);
        Assert.Equal(0.20f + PotTreasureHunt.CalibrationMarginRadians, hunt.SuggestedHalfAngleRadians, precision: 5);
        Assert.True(hunt.SuggestedHalfAngleDegrees < 22.5f, "a 0.2 rad worst case should tighten the arc");
    }

    /// <summary>
    /// Eight compass words cannot mean more than ±22.5°, so a measurement above the ceiling is a
    /// bug to investigate — never an arc to widen past it.
    /// </summary>
    [Fact]
    public void SuggestedHalfAngle_NeverExceedsTheCompassCeiling()
    {
        var hunt = Hunt(ConfigWithSamples(PotTreasureHunt.MinCalibrationSamples, 1.2f));

        Assert.Equal(PotTreasureTriangulation.DefaultHalfAngleRadians, hunt.SuggestedHalfAngleRadians, precision: 5);
    }

    [Fact]
    public void SuggestedHalfAngle_FallsBackWhenSamplesCarryNoError()
    {
        var hunt = Hunt(ConfigWithSamples(PotTreasureHunt.MinCalibrationSamples, 0f));

        Assert.Equal(PotTreasureTriangulation.DefaultHalfAngleRadians, hunt.SuggestedHalfAngleRadians, precision: 5);
        Assert.False(hunt.IsArcCalibrated);
    }

    [Fact]
    public void MaxObservedActivationRadius_IgnoresAnEmptySampleSet()
    {
        var config = new PhantomConfig();
        config.ActivationRadiusSamples.Add(0f);

        Assert.Null(Hunt(config).MaxObservedActivationRadius);
    }

    /// <summary>
    /// The find must be recorded ONCE. It previously fired every tick the coffer stayed visible,
    /// producing 500 activation samples from a handful of hunts — none of them the trigger
    /// distance, since the player kept walking while it logged.
    /// </summary>
    [Fact]
    public void FindProximity_RejectsACofferTooFarToBeThisHuntsOne()
    {
        Assert.True(PotTreasureHunt.FindProximityYalms > PotTreasureTriangulation.ImmediateRangeYalms,
            "must at least cover the 'immediately' band");
        Assert.True(PotTreasureHunt.FindProximityYalms < 50f,
            "a coffer across the field is an ordinary world chest, not this hunt's");
    }
}
