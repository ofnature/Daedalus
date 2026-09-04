using System.Collections.Generic;
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
    // ── Party buff → status pairing ──
    // These ids were entered by hand from the Status sheet, so they get a guard.

    [Fact]
    public void PartyBuffStatuses_AllActionsExistInCatalog()
    {
        foreach (var actionId in PhantomActions.PartyBuffStatusByAction.Keys)
            Assert.Contains(PhantomActions.All, a => a.ActionId == actionId);
    }

    [Fact]
    public void PartyBuffStatuses_AreDistinctPerAction()
    {
        var statuses = PhantomActions.PartyBuffStatusByAction.Values.ToList();

        Assert.Equal(statuses.Count, statuses.Distinct().Count());
    }

    [Fact]
    public void PartyBuffStatuses_AreNonZero()
    {
        Assert.All(PhantomActions.PartyBuffStatusByAction.Values, id => Assert.NotEqual(0u, id));
    }

    /// <summary>
    /// The reported bug: Offensive Aria (5s recast, 70s buff) was pushed on cooldown and chain-cast.
    /// Its status must be mapped or the layer falls back to recast pacing and the spam returns.
    /// </summary>
    [Fact]
    public void PartyBuffStatuses_MapOffensiveAriaToItsBuff()
    {
        Assert.Equal(4247u, PhantomActions.PartyBuffStatusByAction[41608]);
    }

    [Fact]
    public void PartyBuffStatuses_CoverEveryBardGeomancerRangerAndMysticKnightBuff()
    {
        uint[] expected = [41608, 41607, 41610, 41611, 41619, 41599, 46590];

        Assert.All(expected, id => Assert.True(PhantomActions.PartyBuffStatusByAction.ContainsKey(id)));
    }

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
            // Top of this block is Inquiring Mind (46606), the Freelancer's Lv15 buff broadcast —
            // MKDSupportJob row 0 lists 41650 / 41651 / 46606 / 49102, so the Freelancer's own
            // actions are scattered across all three blocks rather than sitting in one.
            var inNewJobBlock = def.ActionId is >= 46590 and <= 46606;
            var inNorthHornBlock = def.ActionId is >= 49060 and <= 49150; // 7.55 jobs (Ninja 49062+, Necromancer 49097+)
            Assert.True(inOriginalBlock || inNewJobBlock || inNorthHornBlock,
                $"{def.Name} ({def.ActionId}) outside the known phantom action ID blocks");
        }
    }

    [Fact]
    public void Catalog_EveryPhantomJobHasActions()
    {
        foreach (var entry in PhantomJobData.LevelStatuses)
        {
            // North Horn jobs are cataloged incrementally from live duty-bar sightings —
            // coverage grows one screenshot at a time (Necromancer's Drain Touch first).
            if (NorthHornJobs.Contains(entry.Key))
                continue;
            var actions = PhantomActions.ForJob(entry.Key);
            Assert.True(actions.Count >= 2, $"{entry.Key} has {actions.Count} actions");
        }
    }

    private static readonly HashSet<PhantomJob> NorthHornJobs =
    [
        PhantomJob.PhantomNinja, PhantomJob.PhantomWhiteMage, PhantomJob.PhantomBlackMage,
        PhantomJob.PhantomDragoon, PhantomJob.PhantomSummoner, PhantomJob.PhantomBlueMage,
        PhantomJob.PhantomRedMage, PhantomJob.Necromancer,
    ];

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
            // Regular jobs cap at 6. Freelancer levels by mastery count instead, and the game's
            // own table gives its four unlocks as 5 / 10 / 15 / 20 (MKDSupportJob row 0) —
            // Occult Resuscitation, Occult Treasuresight, Inquiring Mind, Wisdom on the Winds.
            var cap = def.Job == PhantomJob.Freelancer ? 20 : 6;
            Assert.InRange(def.RequiredLevel, 1, cap);
        }
    }

    [Fact]
    public void UnlockHints_CoverAllSouthHornJobs()
    {
        foreach (var entry in PhantomJobData.LevelStatuses)
        {
            if (NorthHornJobs.Contains(entry.Key))
                continue; // North Horn shard shop not yet cataloged
            Assert.False(string.IsNullOrEmpty(PhantomJobData.GetUnlockHint(entry.Key)),
                $"{entry.Key} has no unlock hint");
        }
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
