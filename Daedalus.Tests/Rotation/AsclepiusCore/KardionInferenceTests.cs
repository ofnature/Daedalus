using Daedalus.Rotation.AsclepiusCore.Helpers;
using Xunit;

namespace Daedalus.Tests.Rotation.AsclepiusCore;

/// <summary>
/// Kardion inference — "nobody else visibly bears it, so assume the tank does". A last resort
/// for a tank whose statuses can't be read, and nothing more.
/// <para>
/// Field 2026-08-01 (Occult Crescent): the rule never checked the tank itself, so a genuinely
/// unplaced Kardion was inferred onto the tank, PrimeTankKardionLatch confirmed the guess, and
/// the recast stayed suppressed for the whole zone — Debug read "Kardion on tank (pre-pull)"
/// while the tank had no buff at all.
/// </para>
/// </summary>
public sealed class KardionInferenceTests
{
    /// <summary>The regression: a readable tank without Kardion must never be inferred into one.</summary>
    [Fact]
    public void CanInfer_IsFalseWhenTheTankStatusesAreReadable()
    {
        Assert.False(AsclepiusStatusHelper.CanInferKardionOnTank(
            playerHasKardia: true,
            coSagePresent: false,
            tankStatusesReadable: true,
            anotherAllyBearsKardion: false));
    }

    [Fact]
    public void CanInfer_IsTrueOnlyForAnUnreadableTank()
    {
        Assert.True(AsclepiusStatusHelper.CanInferKardionOnTank(
            playerHasKardia: true,
            coSagePresent: false,
            tankStatusesReadable: false,
            anotherAllyBearsKardion: false));
    }

    /// <summary>Somebody else visibly carries it, so the tank plainly does not.</summary>
    [Fact]
    public void CanInfer_IsFalseWhenAnotherAllyCarriesKardion()
    {
        Assert.False(AsclepiusStatusHelper.CanInferKardionOnTank(
            playerHasKardia: true,
            coSagePresent: false,
            tankStatusesReadable: false,
            anotherAllyBearsKardion: true));
    }

    /// <summary>"Somebody's Kardion is on the tank" says nothing about whose.</summary>
    [Fact]
    public void CanInfer_IsFalseWithACoSage()
    {
        Assert.False(AsclepiusStatusHelper.CanInferKardionOnTank(
            playerHasKardia: true,
            coSagePresent: true,
            tankStatusesReadable: false,
            anotherAllyBearsKardion: false));
    }

    [Fact]
    public void CanInfer_IsFalseWithoutKardiaAtAll()
    {
        Assert.False(AsclepiusStatusHelper.CanInferKardionOnTank(
            playerHasKardia: false,
            coSagePresent: false,
            tankStatusesReadable: false,
            anotherAllyBearsKardion: false));
    }
}
