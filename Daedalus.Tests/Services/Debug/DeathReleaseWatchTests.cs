using Daedalus.Services.Debug;
using Xunit;

namespace Daedalus.Tests.Services.Debug;

/// <summary>
/// "The toons release while waiting on a rez" has two causes that look identical in the moment:
/// a plugin clicking the return prompt, or the game's own death timer expiring. The marker
/// separates them by distance, then reports the elapsed time so the cause is obvious.
/// </summary>
public sealed class DeathReleaseWatchTests
{
    /// <summary>A raise leaves you exactly where you fell.</summary>
    [Theory]
    [InlineData(0f)]
    [InlineData(2f)]
    [InlineData(29.9f)]
    public void Classify_TreatsStayingPutAsARaise(float moved)
    {
        Assert.Equal(RevivalKind.Raised, DeathReleaseWatch.Classify(moved));
    }

    /// <summary>A release relocates you to a spawn point.</summary>
    [Theory]
    [InlineData(30.1f)]
    [InlineData(250f)]
    [InlineData(4000f)]
    public void Classify_TreatsRelocationAsARelease(float moved)
    {
        Assert.Equal(RevivalKind.Released, DeathReleaseWatch.Classify(moved));
    }

    /// <summary>
    /// Distance is the discriminator rather than time, because time varies with who is raising
    /// and how far away they are — a slow raise and a fast release would otherwise look alike.
    /// </summary>
    [Fact]
    public void Classify_UsesTheDocumentedBoundary()
    {
        Assert.Equal(RevivalKind.Raised, DeathReleaseWatch.Classify(DeathReleaseWatch.ReleaseDistanceYalms));
        Assert.Equal(RevivalKind.Released, DeathReleaseWatch.Classify(DeathReleaseWatch.ReleaseDistanceYalms + 0.1f));
    }
}
