using Daedalus.Rotation.ArtemisCore.Helpers;
using Daedalus.Services.Targeting;
using Moq;
using Xunit;

namespace Daedalus.Tests.Rotation.ArtemisCore;

/// <summary>
/// The Second Board bomb push (user, 2026-11-07; trigger agreed with Minerva): from Loosefrox's small-bomb Barrage until
/// the bombs are gone or Chewchum heads the engine's priority list, the familiar stays away and only the 1-2-3 combo
/// goes out — and only while Minerva runs this fight's module.
/// </summary>
public sealed class ArtemisBombPushTests
{
    private const double NoBarrage = double.MaxValue;
    private const string Minerva = ArtemisCooldownRules.MinervaLoosefroxModule;

    [Fact]
    public void TheHoldStartsAtTheBarrageCastBeforeAnyBombExists()
        => Assert.True(ArtemisCooldownRules.HoldsForBombPush(true, Minerva, secondsSinceBarrageCast: 0, smallBombsUp: false, wormFirst: false));

    [Fact]
    public void TheHoldBridgesTheGapBetweenTheCastAndTheBombs()
    {
        var bridge = ArtemisCooldownRules.BarrageBridgeSeconds;
        Assert.True(ArtemisCooldownRules.HoldsForBombPush(true, Minerva, bridge, false, false));
        Assert.False(ArtemisCooldownRules.HoldsForBombPush(true, Minerva, bridge + 0.1, false, false));
    }

    [Fact]
    public void TheHoldLastsWhileSmallBombsAreTargetable()
        => Assert.True(ArtemisCooldownRules.HoldsForBombPush(true, Minerva, NoBarrage, smallBombsUp: true, wormFirst: false));

    [Fact]
    public void TheWormFirstEndsTheHold()
        => Assert.False(ArtemisCooldownRules.HoldsForBombPush(true, Minerva, NoBarrage, smallBombsUp: true, wormFirst: true));

    [Fact]
    public void NoBombsAndNoBarrageIsNoHold()
        => Assert.False(ArtemisCooldownRules.HoldsForBombPush(true, Minerva, NoBarrage, false, false));

    [Theory]
    [InlineData("")]                 // no engine, BossMod, or Minerva not connected
    [InlineData("PasDeSeul")]        // another fight's module
    public void WithoutMinervasModuleForTheFightItPlaysAsBefore(string module)
        => Assert.False(ArtemisCooldownRules.HoldsForBombPush(true, module, 0, true, false));

    [Fact]
    public void TheModuleNameIsThePortsSpelling()
        => Assert.Equal("LooserfroxInkyjots", Minerva);

    [Fact]
    public void OffTheBoardIsNoHold()
        => Assert.False(ArtemisCooldownRules.HoldsForBombPush(onBoard: false, Minerva, 0, true, false));

    [Fact]
    public void TheIdsAreMinervasRecordedOnes()
    {
        Assert.Equal(0x4C69u, ArtemisCooldownRules.SmallBombBaseId);
        Assert.Equal(0x4C66u, ArtemisCooldownRules.ChewchumBaseId);
        Assert.Equal(0x4C65u, ArtemisCooldownRules.LoosefroxBaseId);
        Assert.Equal(50446u, ArtemisCooldownRules.SmallBombBarrageCastId);
    }

    [Fact]
    public void ATargetingServiceWithoutAnEngineNamesNothing()
    {
        var targeting = new Mock<ITargetingService>().Object;
        Assert.Equal(0UL, targeting.FirstFightPriority);
        Assert.True(string.IsNullOrEmpty(targeting.ActiveFightModule));
    }
}
