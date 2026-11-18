using Daedalus.Data;
using Daedalus.Rotation.ArtemisCore.Helpers;
using Xunit;
using Step = Daedalus.Rotation.ArtemisCore.Helpers.ArtemisFamiliarKeeper.PreBorrowStep;

namespace Daedalus.Tests.Rotation.ArtemisCore;

/// <summary>
/// The pre-fight Borrow on a Crucible board (user, 2026-11-08): a skin borrowed from a beast on the horns before the
/// pull, then the fight opens on the first horn. The pick must match BMRV's / fuurie's documented D3 pre-pull Borrows.
/// </summary>
public sealed class ArtemisPreBorrowTests
{
    private static BeastClassification[] Kins(params int[] bestiaryNos)
    {
        var kins = new BeastClassification[bestiaryNos.Length];
        for (var i = 0; i < kins.Length; i++)
            kins[i] = BstFamiliars.ByBestiaryNo((uint)bestiaryNos[i])!.Classification;
        return kins;
    }

    // Bestiary numbers.
    private const int OpoOpo = 5, Diremite = 8, Megalocrab = 9, Wespe = 10, Mandragora = 12, Geshunpest = 13, Crab = 15,
        Mantis = 16, Colibri = 32, Coeurl = 33, Damselfly = 44, IceGolem = 47, Behemoth = 50;

    [Theory]
    [InlineData(new[] { Mandragora, Wespe, OpoOpo }, 2)]     // Bone pair: Opo-opo Beastskin
    [InlineData(new[] { Wespe, IceGolem, Coeurl }, 2)]       // Arch Demon: Coeurl Beastskin
    [InlineData(new[] { Wespe, Geshunpest, Behemoth }, 2)]   // Banemite: the Behemoth by kin — but nothing is borrowed before that fight (CrucibleDegreeTeams.CardBeast)
    [InlineData(new[] { Wespe, Damselfly, Megalocrab }, 1)]  // Ogre: Damselfly Vileskin
    [InlineData(new[] { Wespe, Behemoth, Colibri }, 1)]      // Pas de Seul: Behemoth Beastskin
    [InlineData(new[] { Wespe, IceGolem, Diremite }, 2)]     // Ymir: Diremite Vileskin
    [InlineData(new[] { Crab, Damselfly, Mantis }, 1)]       // Piscodemon (undocumented): the first Vilekin
    public void TheDocumentedTeamsPreBorrowFromTheDocumentedBeast(int[] team, int expectedSlot)
        => Assert.Equal(expectedSlot, ArtemisFamiliarKeeper.PickPreBorrowSlot(Kins(team)));

    [Fact]
    public void TheFirstHornIsNeverPreBorrowedFrom()
        => Assert.Equal(ArtemisFamiliarKeeper.None, ArtemisFamiliarKeeper.PickPreBorrowSlot(Kins(Behemoth, Mandragora, Colibri)));

    [Fact]
    public void BeastkinBeatsAnEarlierVilekin()
        => Assert.Equal(2, ArtemisFamiliarKeeper.PickPreBorrowSlot(Kins(Mandragora, Damselfly, Behemoth)));

    [Fact]
    public void EmptyHornsGiveNothing()
        => Assert.Equal(ArtemisFamiliarKeeper.None, ArtemisFamiliarKeeper.PickPreBorrowSlot(
            [BeastClassification.Unknown, BeastClassification.Unknown, BeastClassification.Unknown]));

    [Fact]
    public void TheCardFollowsTheKin()
    {
        Assert.Equal(BSTActions.BeastskinId, ArtemisFamiliarKeeper.PreBorrowCard(BeastClassification.Beastkin));
        Assert.Equal(BSTActions.VileskinId, ArtemisFamiliarKeeper.PreBorrowCard(BeastClassification.Vilekin));
        Assert.Equal(0u, ArtemisFamiliarKeeper.PreBorrowCard(BeastClassification.Seedkin));
    }

    [Fact]
    public void SkinsAndKeptJobCardsAreNotReplacedButLeftoversAre()
    {
        Assert.True(ArtemisFamiliarKeeper.HoldsCardWorthKeeping(BSTActions.BeastskinId));
        Assert.True(ArtemisFamiliarKeeper.HoldsCardWorthKeeping(BSTActions.VileskinId));
        Assert.True(ArtemisFamiliarKeeper.HoldsCardWorthKeeping(BSTActions.ScaleskinId));
        Assert.True(ArtemisFamiliarKeeper.HoldsCardWorthKeeping(BSTActions.SoulCrushId));
        Assert.True(ArtemisFamiliarKeeper.HoldsCardWorthKeeping(BSTActions.QuellingWaveId));
        Assert.False(ArtemisFamiliarKeeper.HoldsCardWorthKeeping(BSTActions.CloudSkimId));
        Assert.False(ArtemisFamiliarKeeper.HoldsCardWorthKeeping(BSTActions.SeedsowerId));
        Assert.False(ArtemisFamiliarKeeper.HoldsCardWorthKeeping(BSTActions.BeastMode.ActionId));
    }

    // The sequence: no familiar → call the beast → Borrow → call the first horn back → done.
    [Fact]
    public void TheStepsRunInOrder()
    {
        const int pre = 2, none = ArtemisFamiliarKeeper.None;
        Assert.Equal(Step.SummonBeast, ArtemisFamiliarKeeper.NextPreBorrowStep(true, true, pre, none, cardHeld: false, borrowReady: true));
        Assert.Equal(Step.SummonBeast, ArtemisFamiliarKeeper.NextPreBorrowStep(true, true, pre, outSlot: 0, false, true));
        Assert.Equal(Step.Borrow, ArtemisFamiliarKeeper.NextPreBorrowStep(true, true, pre, outSlot: pre, false, true));
        Assert.Equal(Step.SummonOpener, ArtemisFamiliarKeeper.NextPreBorrowStep(true, true, pre, outSlot: pre, cardHeld: true, borrowReady: false));
        Assert.Equal(Step.None, ArtemisFamiliarKeeper.NextPreBorrowStep(true, true, pre, outSlot: 0, cardHeld: true, borrowReady: false));
    }

    [Fact]
    public void WithBorrowCoolingTheBeastOutGoesBackToTheFirstHorn()
    {
        Assert.Equal(Step.SummonOpener, ArtemisFamiliarKeeper.NextPreBorrowStep(true, true, 2, outSlot: 2, cardHeld: false, borrowReady: false));
        Assert.Equal(Step.None, ArtemisFamiliarKeeper.NextPreBorrowStep(true, true, 2, ArtemisFamiliarKeeper.None, false, borrowReady: false));
    }

    [Fact]
    public void NothingWhenNotWantedOrUnableOrNoBeast()
    {
        Assert.Equal(Step.None, ArtemisFamiliarKeeper.NextPreBorrowStep(wanted: false, true, 2, ArtemisFamiliarKeeper.None, false, true));
        Assert.Equal(Step.None, ArtemisFamiliarKeeper.NextPreBorrowStep(true, canAct: false, 2, ArtemisFamiliarKeeper.None, false, true));
        Assert.Equal(Step.None, ArtemisFamiliarKeeper.NextPreBorrowStep(true, true, ArtemisFamiliarKeeper.None, ArtemisFamiliarKeeper.None, false, true));
    }

    [Fact]
    public void ThePreBorrowedVileskinIsKeptAgainstAnEverydayBorrow()
    {
        Assert.True(ArtemisCooldownRules.KeepsPreBorrowedCard(true, BSTActions.VileskinId, BSTActions.VileskinId, BeastClassification.Seedkin));
        Assert.True(ArtemisCooldownRules.KeepsPreBorrowedCard(true, BSTActions.VileskinId, BSTActions.VileskinId, BeastClassification.Vilekin));
    }

    [Fact]
    public void ABeastkinOutMayReplaceItWithBeastskin()
        => Assert.False(ArtemisCooldownRules.KeepsPreBorrowedCard(true, BSTActions.VileskinId, BSTActions.VileskinId, BeastClassification.Beastkin));

    [Fact]
    public void OnceUsedOrOffABoardNothingIsKept()
    {
        Assert.False(ArtemisCooldownRules.KeepsPreBorrowedCard(true, BSTActions.BeastMode.ActionId, BSTActions.VileskinId, BeastClassification.Seedkin));
        Assert.False(ArtemisCooldownRules.KeepsPreBorrowedCard(true, BSTActions.VileskinId, preBorrowedCardId: 0, BeastClassification.Seedkin));
        Assert.False(ArtemisCooldownRules.KeepsPreBorrowedCard(onBoard: false, BSTActions.VileskinId, BSTActions.VileskinId, BeastClassification.Seedkin));
    }
}
