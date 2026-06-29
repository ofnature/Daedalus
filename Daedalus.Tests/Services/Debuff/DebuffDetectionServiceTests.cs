using Dalamud.Plugin.Services;
using Daedalus.Services.Debuff;
using Moq;
using Xunit;

namespace Daedalus.Tests.Services.Debuff;

/// <summary>
/// Tests for DebuffDetectionService priority logic. Exercises the real service — GetDebuffPriority does
/// not touch the Lumina Status sheet, so a bare IDataManager mock (null sheet) is enough. IsDispellable /
/// FindHighestPriorityDebuff need real game data and are in-game-validation-only.
/// </summary>
public class DebuffDetectionServiceTests
{
    private static DebuffPriority GetPriority(uint statusId) =>
        new DebuffDetectionService(new Mock<IDataManager>().Object).GetDebuffPriority(statusId);

    [Theory]
    [InlineData(910u)]   // Doom (common)
    [InlineData(1769u)]  // Throttle
    [InlineData(2519u)]  // Doom (Bozjan)
    [InlineData(3364u)]  // Doom (variant dungeon)
    public void GetDebuffPriority_LethalDebuffs_ReturnsLethal(uint statusId) =>
        Assert.Equal(DebuffPriority.Lethal, GetPriority(statusId));

    [Theory]
    [InlineData(714u)]   // Vulnerability Up
    [InlineData(638u)]   // Damage Down
    [InlineData(1195u)]  // Vulnerability Up (alternate)
    public void GetDebuffPriority_HighPriorityDebuffs_ReturnsHigh(uint statusId) =>
        Assert.Equal(DebuffPriority.High, GetPriority(statusId));

    [Theory]
    [InlineData(17u)]   // Paralysis
    [InlineData(7u)]    // Silence
    [InlineData(6u)]    // Pacification
    [InlineData(3u)]    // Sleep
    [InlineData(18u)]   // Stun
    public void GetDebuffPriority_MediumPriorityDebuffs_ReturnsMedium(uint statusId) =>
        Assert.Equal(DebuffPriority.Medium, GetPriority(statusId));

    [Theory]
    [InlineData(13u)]   // Bind
    [InlineData(14u)]   // Heavy
    [InlineData(15u)]   // Blind
    [InlineData(564u)]  // Leaden
    public void GetDebuffPriority_KnownMovementDebuffs_ReturnLow(uint statusId) =>
        Assert.Equal(DebuffPriority.Low, GetPriority(statusId));

    [Theory]
    [InlineData(99999u)]  // Unknown status
    [InlineData(12345u)]  // Random ID
    [InlineData(1u)]      // Not in any priority list
    public void GetDebuffPriority_UnknownDebuffs_ReturnMedium(uint statusId)
    {
        // Unknown dispellable debuffs default to Medium so they cleanse at the default Esuna threshold —
        // most "esuna check" mechanics use a unique status id and must be cleansed or the party wipes.
        Assert.Equal(DebuffPriority.Medium, GetPriority(statusId));
    }

    [Fact]
    public void GetDebuffPriority_PriorityOrdering_LethalIsHighest()
    {
        Assert.True(DebuffPriority.Lethal < DebuffPriority.High);
        Assert.True(DebuffPriority.High < DebuffPriority.Medium);
        Assert.True(DebuffPriority.Medium < DebuffPriority.Low);
        Assert.True(DebuffPriority.Low < DebuffPriority.None);
    }
}
