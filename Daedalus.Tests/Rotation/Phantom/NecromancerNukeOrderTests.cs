using Daedalus.Rotation.Phantom;
using Daedalus.Services.Occult;
using Xunit;

namespace Daedalus.Tests.Rotation.Phantom;

/// <summary>
/// The three Necromancer nukes share ONE 40s recast, so the order is not a preference — whichever
/// dispatches first spends the recast and the other two are refused on cooldown. Getting the pick
/// wrong therefore means the wrong element fires, not merely a suboptimal one.
/// </summary>
public sealed class NecromancerNukeOrderTests
{
    private const uint DeepFreeze = PhantomBandRules.DeepFreezeId;
    private const uint HellWind = PhantomBandRules.HellWindId;
    private const uint ChaosDrive = PhantomBandRules.ChaosDriveId;

    [Fact]
    public void WindWeak_LeadsWithHellWind()
        => Assert.Equal(HellWind, PhantomBandRules.NecromancerNukeOrder(OccultElement.Wind)[0]);

    [Fact]
    public void IceWeak_LeadsWithDeepFreeze()
        => Assert.Equal(DeepFreeze, PhantomBandRules.NecromancerNukeOrder(OccultElement.Ice)[0]);

    [Fact]
    public void LightningWeak_LeadsWithChaosDrive()
        => Assert.Equal(ChaosDrive, PhantomBandRules.NecromancerNukeOrder(OccultElement.Lightning)[0]);

    /// <summary>
    /// Fire has no nuke in this kit, so it is not a match and must not be treated as one.
    /// </summary>
    [Fact]
    public void FireWeak_FallsBackToTheEarliestUnlock()
        => Assert.Equal(DeepFreeze, PhantomBandRules.NecromancerNukeOrder(OccultElement.Fire)[0]);

    /// <summary>
    /// An unrecorded weakness is not evidence of absence — the table only knows what something
    /// revealed. Deep Freeze leads because it unlocks first (Lv.2 against Hell Wind's Lv.3).
    /// </summary>
    [Fact]
    public void UnknownWeakness_LeadsWithTheEarliestUnlock()
        => Assert.Equal(DeepFreeze, PhantomBandRules.NecromancerNukeOrder(null)[0]);

    /// <summary>
    /// Elements are a bitmask and the shipped table genuinely carries combined values. All
    /// weaknesses give the same bonus, so among matching elements the earliest unlock wins —
    /// which is what the ordered tests below encode rather than leave to chance.
    /// </summary>
    [Fact]
    public void WeakToIceAndWind_LeadsWithTheOneUnlockedFirst()
        => Assert.Equal(DeepFreeze, PhantomBandRules.NecromancerNukeOrder(OccultElement.Ice | OccultElement.Wind)[0]);

    [Fact]
    public void WeakToWindAndLightning_LeadsWithHellWind()
        => Assert.Equal(HellWind, PhantomBandRules.NecromancerNukeOrder(OccultElement.Wind | OccultElement.Lightning)[0]);

    /// <summary>
    /// A non-matching element must not shuffle the rest away: all three are always pushed, so a
    /// refusal on the leader can never mean zero damage.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData(OccultElement.Wind)]
    [InlineData(OccultElement.Ice)]
    [InlineData(OccultElement.Lightning)]
    public void AllThreeAreAlwaysOffered(OccultElement? weakness)
    {
        var order = PhantomBandRules.NecromancerNukeOrder(weakness);
        Assert.Equal(3, order.Length);
        Assert.Contains(DeepFreeze, order);
        Assert.Contains(HellWind, order);
        Assert.Contains(ChaosDrive, order);
    }
}
