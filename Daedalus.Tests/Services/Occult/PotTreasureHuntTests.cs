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

    [Fact]
    public void MaxObservedActivationRadius_IgnoresAnEmptySampleSet()
    {
        var config = new PhantomConfig();
        config.ActivationRadiusSamples.Add(0f);

        Assert.Null(Hunt(config).MaxObservedActivationRadius);
    }
}
