using System.Linq;
using Daedalus.Data;
using Xunit;

namespace Daedalus.Tests.Data;

/// <summary>
/// Tests for the Occult Crescent phantom action catalog (Phase 2,
/// docs/occult-phantom-plan.md). IDs come from the RSR generated action table;
/// the original 13 jobs occupy the contiguous 41588–41651 block, the post-7.25
/// trio (Mystic Knight / Gladiator / Dancer) occupies 46590–46605.
/// </summary>
public class PhantomActionsTests
{
    [Fact]
    public void Catalog_ActionIdsAreUnique()
    {
        var ids = PhantomActions.All.Select(a => a.ActionId).ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void Catalog_ActionIdsStayInsideTheTwoKnownBlocks()
    {
        foreach (var def in PhantomActions.All)
        {
            var inOriginalBlock = def.ActionId is >= 41588 and <= 41651;
            var inNewJobBlock = def.ActionId is >= 46590 and <= 46605;
            Assert.True(inOriginalBlock || inNewJobBlock,
                $"{def.Name} ({def.ActionId}) outside both known phantom action ID blocks");
        }
    }

    [Fact]
    public void Catalog_EveryPhantomJobHasActions()
    {
        foreach (var entry in PhantomJobData.LevelStatuses)
        {
            var actions = PhantomActions.ForJob(entry.Key);
            Assert.True(actions.Count >= 2, $"{entry.Key} has {actions.Count} actions");
        }
    }

    [Fact]
    public void Catalog_FieldVerifiedCannoneerIds()
    {
        // Observed on the live duty bar 2026-07-25: Phantom Fire / Holy Cannon / Dark Cannon.
        var cannoneer = PhantomActions.ForJob(PhantomJob.Cannoneer);

        Assert.Contains(cannoneer, a => a.ActionId == 41626 && a.RequiredLevel == 1);
        Assert.Contains(cannoneer, a => a.ActionId == 41627 && a.RequiredLevel == 2);
        Assert.Contains(cannoneer, a => a.ActionId == 41628 && a.RequiredLevel == 3);
    }

    [Fact]
    public void Catalog_OracleCardsAndDancerStepsAreProcGated()
    {
        var procActions = PhantomActions.All.Where(a => a.RequiresProc).ToList();

        // 4 Oracle cards + 4 Dancer steps; the openers (Predict, Dance) are NOT proc-gated.
        Assert.Equal(8, procActions.Count);
        Assert.Equal(4, procActions.Count(a => a.Job == PhantomJob.Oracle));
        Assert.Equal(4, procActions.Count(a => a.Job == PhantomJob.Dancer));
        Assert.DoesNotContain(PhantomActions.All, a => a.ActionId == 41636 && a.RequiresProc);
        Assert.DoesNotContain(PhantomActions.All, a => a.ActionId == 46598 && a.RequiresProc);
    }

    [Fact]
    public void Catalog_RequiredLevelsArePlausible()
    {
        foreach (var def in PhantomActions.All)
        {
            // Regular jobs cap at 6; Freelancer levels via mastery count (Treasuresight = 10).
            var cap = def.Job == PhantomJob.Freelancer ? 10 : 6;
            Assert.InRange(def.RequiredLevel, 1, cap);
        }
    }

    [Fact]
    public void UnlockHints_CoverAllSixteenJobs()
    {
        foreach (var entry in PhantomJobData.LevelStatuses)
            Assert.False(string.IsNullOrEmpty(PhantomJobData.GetUnlockHint(entry.Key)),
                $"{entry.Key} has no unlock hint");
    }

    [Fact]
    public void SupportJobRowIndex_MatchesFieldVerifiedCannoneerRow()
    {
        // Field-verified 2026-07-25: current-job byte read 09 with Cannoneer active, and
        // the exp array held Cannoneer's 1760 at index 9.
        Assert.Equal(9, PhantomJobData.GetSupportJobRowIndex(PhantomJob.Cannoneer));
        Assert.Equal(0, PhantomJobData.GetSupportJobRowIndex(PhantomJob.Freelancer));
        Assert.Equal(15, PhantomJobData.GetSupportJobRowIndex(PhantomJob.Dancer));
    }
}
