using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Daedalus.Data;
using Daedalus.Rotation.Phantom;
using Xunit;

namespace Daedalus.Tests.Rotation.Phantom;

/// <summary>
/// Phantom Berserker's damage band.
/// <para>
/// Deadly Blow gains up to 2,000 potency (on a 200 base) from damage taken while Pent-up Rage is
/// active, and Rage is what grants Pent-up Rage. Both used to be pushed in the same tick, so Deadly
/// Blow fired straight after Rage and cashed in a window that had soaked almost nothing.
/// <c>PentupRage = 4236</c> sat in the status catalog the whole time, referenced by nothing.
/// </para>
/// </summary>
public sealed class PhantomBerserkerBandTests
{
    private const uint Rage = 41592;
    private const uint DeadlyBlow = 41594;

    // ── the timing rule ────────────────────────────────────────────────────────────────

    /// <summary>The whole point: with the window still open, keep soaking.</summary>
    [Theory]
    [InlineData(10f)]
    [InlineData(6f)]
    [InlineData(PhantomBandRules.PentUpRageSpendWindowSeconds + 0.1f)]
    public void HoldsWhilePentUpRageIsStillSoaking(float remaining)
    {
        var hold = PhantomBandRules.DeadlyBlowHoldReason(rageSlotted: true, rageCooldownRemaining: 58f, pentUpRageRemaining: remaining);

        Assert.NotNull(hold);
        Assert.Contains("soaking", hold);
    }

    /// <summary>As Pent-up Rage is about to drop, spend it — waiting any longer loses the lot.</summary>
    [Theory]
    [InlineData(PhantomBandRules.PentUpRageSpendWindowSeconds)]
    [InlineData(1f)]
    [InlineData(0.1f)]
    public void FiresAsPentUpRageIsAboutToExpire(float remaining)
        => Assert.Null(PhantomBandRules.DeadlyBlowHoldReason(rageSlotted: true, rageCooldownRemaining: 52f, pentUpRageRemaining: remaining));

    /// <summary>
    /// Rage ready and no window yet: hold, because Rage is about to open one and a Deadly Blow spent
    /// now would be on its 30s cooldown for the entire 10s window.
    /// </summary>
    [Fact]
    public void HoldsForRageWhenRageIsReady()
    {
        var hold = PhantomBandRules.DeadlyBlowHoldReason(rageSlotted: true, rageCooldownRemaining: 0f, pentUpRageRemaining: 0f);

        Assert.NotNull(hold);
        Assert.Contains("Rage is ready", hold);
    }

    /// <summary>
    /// Off-cycle: Deadly Blow's recast is half of Rage's, so every other one has no window to wait
    /// for. Holding it would simply lose the use.
    /// </summary>
    [Fact]
    public void FiresFreelyOffCycleWhileRageIsOnCooldown()
        => Assert.Null(PhantomBandRules.DeadlyBlowHoldReason(rageSlotted: true, rageCooldownRemaining: 35f, pentUpRageRemaining: 0f));

    /// <summary>
    /// The starvation guard. An unslotted Rage never goes on cooldown, so "wait for Rage" would
    /// hold Deadly Blow for the rest of the fight. RSR guards this with RagePvE.IsEnabled; the
    /// phantom layer's equivalent is the duty bar.
    /// </summary>
    [Fact]
    public void NeverWaitsForARageThatIsNotSlotted()
        => Assert.Null(PhantomBandRules.DeadlyBlowHoldReason(rageSlotted: false, rageCooldownRemaining: 0f, pentUpRageRemaining: 0f));

    /// <summary>
    /// A Pent-up Rage that is already up is spent properly even when Rage has since been unslotted
    /// — the live status decides, not the bar.
    /// </summary>
    [Fact]
    public void AnActiveWindowIsSoakedEvenIfRageIsNoLongerSlotted()
        => Assert.NotNull(PhantomBandRules.DeadlyBlowHoldReason(rageSlotted: false, rageCooldownRemaining: 0f, pentUpRageRemaining: 8f));

    // ── Rage roots you ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Rage's status has LockControl in the game data: ten seconds of the game driving the character.
    /// Registering it as a 10s root is what puts it behind the stand-still gate — never mid-dodge, never
    /// on ground that won't stay safe for the whole lock.
    /// </summary>
    [Fact]
    public void RageRootsForItsFullTenSeconds()
        => Assert.Equal(10f, PhantomBandRules.RootSeconds(Rage));

    /// <summary>Deadly Blow is an instant weaponskill with no lock; gating it would only cost damage.</summary>
    [Fact]
    public void DeadlyBlowDoesNotRoot()
        => Assert.Equal(0f, PhantomBandRules.RootSeconds(DeadlyBlow));

    /// <summary>
    /// Rage is an ability, so no GCD wait is added: the stand covers exactly the ten-second lock.
    /// Occult Jump's two seconds must be unaffected by adding Rage beside it.
    /// </summary>
    [Fact]
    public void TheStandForRageIsTheLockAloneAndOccultJumpIsUnchanged()
    {
        Assert.Equal(10f, PhantomBandRules.StillSecondsForCast(gcdRemaining: 1.4f, isGcd: false, castSeconds: PhantomBandRules.RootSeconds(Rage)), 3);
        Assert.Equal(2f, PhantomBandRules.RootSeconds(49077));
    }

    // ── Rage waits for calm (field 2026-09-14: still locked into mechanics) ─────────────

    /// <summary>
    /// An enemy mid-cast is the lead-in to most mechanics, usually visible before its telegraph is drawn —
    /// which is exactly what the root gate's ground check cannot see.
    /// </summary>
    [Fact]
    public void RageHoldsWhileAnEnemyIsCasting()
    {
        var hold = PhantomBandRules.RageHoldReason(enemyCasting: true, secondsSinceEngineSteered: double.MaxValue);

        Assert.NotNull(hold);
        Assert.Contains("casting", hold);
    }

    /// <summary>A dodge a few seconds ago means a mechanic phase; a ten-second lock is the worst thing to start in one.</summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(3.0)]
    [InlineData(PhantomBandRules.RageCalmSeconds - 0.1)]
    public void RageHoldsUntilTheFightHasBeenCalm(double secondsSinceDodge)
    {
        var hold = PhantomBandRules.RageHoldReason(enemyCasting: false, secondsSinceDodge);

        Assert.NotNull(hold);
        Assert.Contains("calm", hold);
    }

    [Theory]
    [InlineData(PhantomBandRules.RageCalmSeconds)]
    [InlineData(45.0)]
    [InlineData(double.MaxValue)]   // never steered at all this session
    public void RageFiresOnceCalmAndNobodyIsCasting(double secondsSinceDodge)
        => Assert.Null(PhantomBandRules.RageHoldReason(enemyCasting: false, secondsSinceDodge));

    /// <summary>A cast bar outranks a long calm: the mechanic that ends the calm is the one being cast.</summary>
    [Fact]
    public void ACastBarOutranksALongCalm()
        => Assert.NotNull(PhantomBandRules.RageHoldReason(enemyCasting: true, secondsSinceEngineSteered: 120));

    // ── wiring, against the source ─────────────────────────────────────────────────────

    /// <summary>
    /// Rage is pushed once and only behind its calm rule, and Deadly Blow only waits for a Rage that is
    /// allowed to fire. Without that second part, a Rage held through a busy phase would hold Deadly Blow
    /// for the whole phase too.
    /// </summary>
    [Fact]
    public void RageIsGatedForCalmAndDeadlyBlowIsNotStarvedByIt()
    {
        var source = File.ReadAllText(LayerSourcePath());
        var berserker = Regex.Match(source, @"case PhantomJob\.Berserker:(.*?)case PhantomJob\.", RegexOptions.Singleline);
        Assert.True(berserker.Success, "Berserker case not found in PhantomActionLayer");

        var body = berserker.Groups[1].Value;
        Assert.Contains("RageHoldReason", body);
        Assert.Single(Regex.Matches(body, @"TryPush\([^;]*\b41592\b"));
        Assert.Matches(@"if \(rageHold is null\)\s*TryPush\([^;]*\b41592\b", body);
        Assert.Matches(@"rageSlotted:\s*IsOnDutyBar\(41592\)\s*&&\s*rageHold is null", body);
    }

    /// <summary>Game data: Rage 60s, Deadly Blow 30s, Pent-up Rage status 4236.</summary>
    [Fact]
    public void CatalogMatchesTheGame()
    {
        Assert.Equal(PhantomJob.Berserker, PhantomActions.All.Single(a => a.ActionId == Rage).Job);
        Assert.Equal(PhantomJob.Berserker, PhantomActions.All.Single(a => a.ActionId == DeadlyBlow).Job);
        Assert.Equal(4236u, PhantomActions.StatusIds.PentupRage);
    }

    /// <summary>
    /// Deadly Blow must be pushed only through the hold rule. A second, unconditional push anywhere
    /// in the Berserker case would silently restore the old behaviour.
    /// </summary>
    [Fact]
    public void DeadlyBlowIsOnlyPushedBehindTheHoldRule()
    {
        var source = File.ReadAllText(LayerSourcePath());
        var berserker = Regex.Match(source, @"case PhantomJob\.Berserker:(.*?)case PhantomJob\.", RegexOptions.Singleline);
        Assert.True(berserker.Success, "Berserker case not found in PhantomActionLayer");

        var body = berserker.Groups[1].Value;
        Assert.Contains("DeadlyBlowHoldReason", body);
        Assert.Contains("PentupRage", body);
        Assert.Single(Regex.Matches(body, @"TryPush\([^;]*\b41594\b"));
    }

    private static string LayerSourcePath()
    {
        var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Daedalus.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "Daedalus", "Rotation", "Phantom", "PhantomActionLayer.cs");
    }
}
